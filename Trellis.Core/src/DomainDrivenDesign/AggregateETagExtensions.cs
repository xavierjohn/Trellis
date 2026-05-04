namespace Trellis;

/// <summary>
/// Extension methods for ETag-based optimistic concurrency validation on aggregate results.
/// Two modes are available — the service owner chooses which to use:
/// <list type="bullet">
/// <item><see cref="OptionalETag{T}(Result{T}, EntityTagValue[])"/> — <c>If-Match</c> is optional (skips if absent)</item>
/// <item><see cref="RequireETag{T}(Result{T}, EntityTagValue[])"/> — <c>If-Match</c> is required (428 if absent)</item>
/// </list>
/// </summary>
public static class AggregateETagExtensions
{
    /// <summary>
    /// Validates that the aggregate's ETag matches one of the expected typed EntityTagValues.
    /// Uses strong comparison per RFC 9110 §13.1.1.
    /// <c>If-Match</c> is <b>optional</b> — if <paramref name="expectedETags"/> is <c>null</c>,
    /// the request proceeds unconditionally.
    /// </summary>
    public static Result<T> OptionalETag<T>(this Result<T> result, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        expectedETags is null
            ? result
            : MatchETag(result, expectedETags);

    /// <summary>
    /// Validates that the aggregate's ETag matches one of the expected typed EntityTagValues.
    /// Uses strong comparison per RFC 9110 §13.1.1.
    /// <c>If-Match</c> is <b>required</b> — if <paramref name="expectedETags"/> is <c>null</c>,
    /// returns <see cref="Error.PreconditionRequired"/> (HTTP 428).
    /// </summary>
    public static Result<T> RequireETag<T>(this Result<T> result, EntityTagValue[]? expectedETags)
        where T : IAggregate
    {
        if (result.IsFailure) return result;
        return expectedETags is null
            ? Result.Fail<T>(new Error.PreconditionRequired(PreconditionKind.IfMatch) { Detail = "This operation requires an If-Match header." })
            : MatchETag(result, expectedETags);
    }

    /// <summary>Async Task overload of <see cref="OptionalETag{T}(Result{T}, EntityTagValue[])"/>.</summary>
    public static async Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).OptionalETag(expectedETags);

    /// <summary>Async ValueTask overload of <see cref="OptionalETag{T}(Result{T}, EntityTagValue[])"/>.</summary>
    public static async ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).OptionalETag(expectedETags);

    /// <summary>Async Task overload of <see cref="RequireETag{T}(Result{T}, EntityTagValue[])"/>.</summary>
    public static async Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).RequireETag(expectedETags);

    /// <summary>Async ValueTask overload of <see cref="RequireETag{T}(Result{T}, EntityTagValue[])"/>.</summary>
    public static async ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).RequireETag(expectedETags);

    private static Result<T> MatchETag<T>(Result<T> result, EntityTagValue[] expectedETags)
        where T : IAggregate
    {
        if (result.IsFailure) return result;

        if (expectedETags.Length == 0)
            return Result.Fail<T>(new Error.PreconditionFailed(ResourceRef.For<T>(), PreconditionKind.IfMatch) { Detail = "If-Match header is empty." });

        // Wildcard check (any wildcard satisfies If-Match)
        if (Array.Exists(expectedETags, tag => tag.IsWildcard))
            return result;

        // Strong comparison per RFC 9110 §13.1.1: weak tags don't satisfy If-Match.
        var strongTags = Array.FindAll(expectedETags, tag => !tag.IsWeak);
        if (strongTags.Length == 0)
            return Result.Fail<T>(new Error.PreconditionFailed(ResourceRef.For<T>(), PreconditionKind.IfMatch) { Detail = "If-Match contains only weak ETags. Strong comparison is required." });

        return result.Ensure(
            aggregate => Array.Exists(strongTags,
                tag => string.Equals(aggregate.ETag, tag.OpaqueTag, StringComparison.Ordinal)),
            _ => new Error.PreconditionFailed(ResourceRef.For<T>(), PreconditionKind.IfMatch) { Detail = "Resource has been modified. Please reload and retry." });
    }
}