namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Trellis;

/// <summary>
/// Extension methods for If-None-Match validation on unsafe methods (create-if-absent patterns).
/// </summary>
public static class IfNoneMatchExtensions
{
    /// <summary>
    /// For create-if-absent (PUT/POST) patterns: checks If-None-Match: * against resource existence.
    /// Returns PreconditionFailed (412) if the resource already exists and If-None-Match: * was sent.
    /// No-op if no If-None-Match header is present.
    /// </summary>
    public static Result<T> EnforceIfNoneMatchPrecondition<T>(this Result<T> result, string[]? ifNoneMatchETags)
    {
        if (ifNoneMatchETags is null)
            return result; // No header — proceed
        if (result.IsFailure)
            return result; // Already failed
        // If-None-Match: * means "only succeed if resource does NOT exist"
        // But we have a successful result (resource exists), so 412
        if (ifNoneMatchETags.Length == 1 && ifNoneMatchETags[0] == "*")
            return Result.Failure<T>(Error.PreconditionFailed("Resource already exists. If-None-Match: * requires the resource to be absent."));
        return result;
    }

    /// <summary>Async Task overload.</summary>
    public static async Task<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(this Task<Result<T>> resultTask, string[]? ifNoneMatchETags) =>
        (await resultTask.ConfigureAwait(false)).EnforceIfNoneMatchPrecondition(ifNoneMatchETags);

    /// <summary>Async ValueTask overload.</summary>
    public static async ValueTask<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(this ValueTask<Result<T>> resultTask, string[]? ifNoneMatchETags) =>
        (await resultTask.ConfigureAwait(false)).EnforceIfNoneMatchPrecondition(ifNoneMatchETags);
}
