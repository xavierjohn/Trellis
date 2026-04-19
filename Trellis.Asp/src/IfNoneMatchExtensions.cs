namespace Trellis.Asp;

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
    public static Result<T> EnforceIfNoneMatchPrecondition<T>(this Result<T> result, EntityTagValue[]? ifNoneMatchETags)
    {
        if (ifNoneMatchETags is null)
            return result;
        if (result.IsFailure)
            return result;
        if (ifNoneMatchETags.Any(tag => tag.IsWildcard))
            return Result.Fail<T>(new Error.PreconditionFailed(new ResourceRef(typeof(T).Name, null), PreconditionKind.IfNoneMatch) { Detail = "Resource already exists. If-None-Match: * requires the resource to be absent." });
        return result;
    }

    /// <summary>Async Task overload.</summary>
    public static async Task<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? ifNoneMatchETags) =>
        (await resultTask.ConfigureAwait(false)).EnforceIfNoneMatchPrecondition(ifNoneMatchETags);

    /// <summary>Async ValueTask overload.</summary>
    public static async ValueTask<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? ifNoneMatchETags) =>
        (await resultTask.ConfigureAwait(false)).EnforceIfNoneMatchPrecondition(ifNoneMatchETags);
}