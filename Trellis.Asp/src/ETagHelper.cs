namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

/// <summary>
/// RFC 9110-compliant entity tag comparison helpers.
/// </summary>
public static class ETagHelper
{
    /// <summary>
    /// Checks whether the <c>If-None-Match</c> header matches the current ETag
    /// using weak comparison per RFC 9110 §13.1.2.
    /// </summary>
    /// <param name="ifNoneMatchHeader">Raw If-None-Match header values.</param>
    /// <param name="currentETag">The current resource ETag (unquoted).</param>
    /// <returns><c>true</c> if any tag matches (meaning 304 should be returned).</returns>
    public static bool IfNoneMatchMatches(IList<EntityTagHeaderValue> ifNoneMatchHeader, string currentETag)
    {
        if (ifNoneMatchHeader.Count == 0 || string.IsNullOrEmpty(currentETag))
            return false;

        var current = new EntityTagHeaderValue($"\"{currentETag}\"");

        foreach (var tag in ifNoneMatchHeader)
        {
            // RFC 9110 §13.1.2: "*" matches any current entity
            if (tag == EntityTagHeaderValue.Any)
                return true;

            // RFC 9110 §13.1.2: If-None-Match uses weak comparison
            // Weak comparison: two ETags are equivalent if their opaque-tags match,
            // regardless of the weak indicator.
            if (string.Equals(tag.Tag.ToString(), current.Tag.ToString(), StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the <c>If-Match</c> header matches the current ETag
    /// using strong comparison per RFC 9110 §13.1.1.
    /// </summary>
    /// <param name="ifMatchHeader">Raw If-Match header values.</param>
    /// <param name="currentETag">The current resource ETag (unquoted).</param>
    /// <returns><c>true</c> if the precondition is satisfied (request should proceed).</returns>
    public static bool IfMatchSatisfied(IList<EntityTagHeaderValue> ifMatchHeader, string currentETag)
    {
        if (ifMatchHeader.Count == 0)
            return true; // No If-Match header → unconditional request

        if (string.IsNullOrEmpty(currentETag))
            return false; // Resource has no ETag → precondition fails

        foreach (var tag in ifMatchHeader)
        {
            // RFC 9110 §13.1.1: "*" matches any current entity
            if (tag == EntityTagHeaderValue.Any)
                return true;

            // RFC 9110 §13.1.1: If-Match uses strong comparison
            // Strong comparison: both must NOT be weak and opaque-tags must match
            if (tag.IsWeak)
                continue; // Weak ETags never satisfy If-Match

            if (string.Equals(tag.Tag.ToString(), $"\"{currentETag}\"", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parses the If-None-Match header from an HTTP request.
    /// </summary>
    /// <returns>null if absent; a wildcard <see cref="EntityTagValue"/> if wildcard; array of entity tag values (both strong and weak).</returns>
    public static EntityTagValue[]? ParseIfNoneMatch(HttpRequest request)
    {
        if (!request.Headers.ContainsKey("If-None-Match"))
            return null;
        var ifNoneMatch = request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch is not { Count: > 0 })
            return [];
        var result = new List<EntityTagValue>();
        foreach (var tag in ifNoneMatch)
        {
            if (tag == EntityTagHeaderValue.Any)
                return [EntityTagValue.Wildcard()];
            // If-None-Match includes both strong and weak tags (weak comparison)
            var opaqueTag = tag.Tag.ToString().Trim('"');
            result.Add(tag.IsWeak ? EntityTagValue.Weak(opaqueTag) : EntityTagValue.Strong(opaqueTag));
        }

        return [.. result];
    }

    /// <summary>
    /// Parses the If-Modified-Since header.
    /// </summary>
    public static DateTimeOffset? ParseIfModifiedSince(HttpRequest request)
    {
        var typed = request.GetTypedHeaders();
        return typed.IfModifiedSince;
    }

    /// <summary>
    /// Parses the If-Unmodified-Since header.
    /// </summary>
    public static DateTimeOffset? ParseIfUnmodifiedSince(HttpRequest request)
    {
        var typed = request.GetTypedHeaders();
        return typed.IfUnmodifiedSince;
    }

    /// <summary>
    /// Parses the <c>If-Match</c> header from an HTTP request and returns
    /// <see cref="EntityTagValue"/> instances for use with
    /// <c>OptionalETag</c>/<c>RequireETag</c>.
    /// </summary>
    /// <param name="request">The HTTP request containing the If-Match header.</param>
    /// <returns>
    /// <c>null</c> if no If-Match header is present;
    /// an array of <see cref="EntityTagValue"/> instances (weak tags excluded per §13.1.1);
    /// an empty array if the header is present but contains only weak tags.
    /// </returns>
    public static EntityTagValue[]? ParseIfMatch(HttpRequest request)
    {
        if (!request.Headers.ContainsKey("If-Match"))
            return null;

        var ifMatch = request.GetTypedHeaders().IfMatch;
        if (ifMatch is not { Count: > 0 })
            return []; // Header present but unparseable → treat as unsatisfiable → 412

        var result = new List<EntityTagValue>();
        foreach (var tag in ifMatch)
        {
            if (tag == EntityTagHeaderValue.Any)
                return [EntityTagValue.Wildcard()];

            // RFC 9110 §13.1.1: If-Match uses strong comparison — skip weak tags
            if (!tag.IsWeak)
                result.Add(EntityTagValue.Strong(tag.Tag.ToString().Trim('"')));
        }

        return [.. result]; // Empty array if all tags were weak
    }
}