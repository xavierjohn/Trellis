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
    /// Parses the <c>If-Match</c> header from an HTTP request per RFC 9110 §13.1.1
    /// and returns all strong ETag values for use with <c>OptionalETag</c>/<c>RequireETag</c>.
    /// </summary>
    /// <param name="request">The HTTP request containing the If-Match header.</param>
    /// <returns>
    /// <c>null</c> if no If-Match header is present;
    /// <c>["*"]</c> if the wildcard was specified (matches any entity);
    /// an array of unquoted strong ETag values (weak tags are excluded per §13.1.1);
    /// an empty array if the header is present but contains only weak tags (unsatisfiable precondition).
    /// </returns>
    /// <remarks>
    /// Use this in controllers instead of manually parsing the header:
    /// <code>
    /// var ifMatchETags = ETagHelper.ParseIfMatch(Request);
    /// await UpdateCommand.TryCreate(id, title, ifMatchETags)
    ///     .BindAsync(cmd => _sender.Send(cmd, ct))
    ///     .ToETagActionResultAsync(this, e => e.ETag, TodoResponse.From);
    /// </code>
    /// </remarks>
    public static string[]? ParseIfMatch(HttpRequest request)
    {
        // Check if the raw header is present before attempting typed parsing.
        // If the header exists but is malformed, return empty array (unsatisfiable precondition → 412)
        // rather than null (which would mean "no header" → unconditional update).
        if (!request.Headers.ContainsKey("If-Match"))
            return null;

        var ifMatch = request.GetTypedHeaders().IfMatch;
        if (ifMatch is not { Count: > 0 })
            return []; // Header present but unparseable → treat as unsatisfiable → 412

        var result = new List<string>();
        foreach (var tag in ifMatch)
        {
            if (tag == EntityTagHeaderValue.Any)
                return ["*"];

            // RFC 9110 §13.1.1: If-Match uses strong comparison — skip weak tags
            if (!tag.IsWeak)
                result.Add(tag.Tag.ToString().Trim('"'));
        }

        return [.. result]; // Empty array if all tags were weak
    }
}
