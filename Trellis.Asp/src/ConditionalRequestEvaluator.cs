namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Trellis;

/// <summary>
/// Decision returned by the conditional request evaluator per RFC 9110 §13.2.2.
/// </summary>
internal enum ConditionalDecision
{
    /// <summary>Preconditions satisfied — proceed with the method.</summary>
    PreconditionsSatisfied,
    /// <summary>Return 304 Not Modified (GET/HEAD only).</summary>
    NotModified,
    /// <summary>Return 412 Precondition Failed.</summary>
    PreconditionFailed,
}

/// <summary>
/// Evaluates RFC 9110 §13 conditional request headers against representation metadata,
/// applying the correct precedence rules from §13.2.2:
/// 1. If-Match → 2. If-Unmodified-Since → 3. If-None-Match → 4. If-Modified-Since
/// </summary>
internal static class ConditionalRequestEvaluator
{
    /// <summary>
    /// Evaluates all conditional request headers against the given representation metadata.
    /// </summary>
    /// <param name="request">The HTTP request containing conditional headers.</param>
    /// <param name="metadata">Metadata for the selected representation (ETag, LastModified).</param>
    /// <returns>A <see cref="ConditionalDecision"/> indicating what the server should do.</returns>
    public static ConditionalDecision Evaluate(HttpRequest request, RepresentationMetadata metadata)
    {
        var typedHeaders = request.GetTypedHeaders();
        var method = request.Method;
        var isSafeMethod = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

        // Step 1: If-Match (RFC §13.1.1 / §13.2.2 step 1)
        var ifMatch = typedHeaders.IfMatch;
        if (ifMatch is { Count: > 0 })
        {
            if (!EvaluateIfMatch(ifMatch, metadata))
                return ConditionalDecision.PreconditionFailed;
            // Proceed to step 3 (skip step 2 per §13.2.2)
        }
        else
        {
            // Step 2: If-Unmodified-Since (only when If-Match absent)
            var ifUnmodifiedSince = typedHeaders.IfUnmodifiedSince;
            if (ifUnmodifiedSince.HasValue && metadata.LastModified.HasValue)
            {
                if (metadata.LastModified.Value > ifUnmodifiedSince.Value)
                    return ConditionalDecision.PreconditionFailed;
            }
        }

        // Step 3: If-None-Match (RFC §13.1.2 / §13.2.2 step 3)
        var ifNoneMatch = typedHeaders.IfNoneMatch;
        if (ifNoneMatch is { Count: > 0 })
        {
            if (EvaluateIfNoneMatch(ifNoneMatch, metadata))
            {
                // Match found
                return isSafeMethod
                    ? ConditionalDecision.NotModified   // GET/HEAD → 304
                    : ConditionalDecision.PreconditionFailed; // Unsafe → 412
            }
            // No match — proceed (skip step 4 per §13.2.2)
        }
        else
        {
            // Step 4: If-Modified-Since (only when If-None-Match absent, GET/HEAD only)
            if (isSafeMethod)
            {
                var ifModifiedSince = typedHeaders.IfModifiedSince;
                if (ifModifiedSince.HasValue && metadata.LastModified.HasValue)
                {
                    if (metadata.LastModified.Value <= ifModifiedSince.Value)
                        return ConditionalDecision.NotModified;
                }
            }
        }

        return ConditionalDecision.PreconditionsSatisfied;
    }

    // If-Match uses strong comparison; wildcard matches any current entity
    private static bool EvaluateIfMatch(IList<EntityTagHeaderValue> ifMatch, RepresentationMetadata metadata)
    {
        foreach (var tag in ifMatch)
        {
            if (tag == EntityTagHeaderValue.Any)
                return true; // Wildcard matches any current entity — no ETag needed
        }

        if (metadata.ETag is null || metadata.ETag.IsWeak)
            return false; // No strong ETag to compare

        foreach (var tag in ifMatch)
        {
            if (tag.IsWeak)
                continue; // Strong comparison excludes weak tags
            if (string.Equals(tag.Tag.ToString().Trim('"'), metadata.ETag.OpaqueTag, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // If-None-Match uses weak comparison; wildcard matches any current entity
    private static bool EvaluateIfNoneMatch(IList<EntityTagHeaderValue> ifNoneMatch, RepresentationMetadata metadata)
    {
        foreach (var tag in ifNoneMatch)
        {
            if (tag == EntityTagHeaderValue.Any)
                return true; // Wildcard matches any current entity
        }

        if (metadata.ETag is null)
            return false;

        foreach (var tag in ifNoneMatch)
        {
            // Weak comparison: opaque-tags match regardless of weakness
            if (string.Equals(tag.Tag.ToString().Trim('"'), metadata.ETag.OpaqueTag, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}