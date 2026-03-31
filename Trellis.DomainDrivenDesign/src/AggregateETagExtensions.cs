namespace Trellis;

/// <summary>
/// Extension methods for ETag-based optimistic concurrency validation on aggregate results.
/// Two modes are available — the service owner chooses which to use:
/// <list type="bullet">
/// <item><see cref="OptionalETag{T}(Result{T}, string[])"/> — <c>If-Match</c> is optional (skips if absent)</item>
/// <item><see cref="RequireETag{T}(Result{T}, string[])"/> — <c>If-Match</c> is required (428 if absent)</item>
/// </list>
/// </summary>
public static class AggregateETagExtensions
{
    /// <summary>
    /// Validates that the aggregate's current <see cref="IAggregate.ETag"/> matches one of the expected values.
    /// <c>If-Match</c> is <b>optional</b> — if <paramref name="expectedETags"/> is <c>null</c>,
    /// the request proceeds unconditionally.
    /// </summary>
    /// <typeparam name="T">An aggregate type implementing <see cref="IAggregate"/>.</typeparam>
    /// <param name="result">The result containing the loaded aggregate.</param>
    /// <param name="expectedETags">
    /// Parsed ETag values from <c>ETagHelper.ParseIfMatch()</c>.
    /// <c>null</c> → no header (unconditional); <c>["*"]</c> → wildcard;
    /// <c>["a","b"]</c> → match any; <c>[]</c> → weak-only header (unsatisfiable → 412).
    /// </param>
    /// <returns>
    /// The original result if the ETag matches or no header was provided;
    /// otherwise a failure result with <see cref="PreconditionFailedError"/> (412).
    /// </returns>
    /// <example>
    /// <code>
    /// // Optional If-Match — update proceeds even without the header
    /// return await _repo.GetByIdAsync(command.OrderId, ct)
    ///     .OptionalETag(command.IfMatchETags)
    ///     .BindAsync(order => order.Submit());
    /// </code>
    /// </example>
    public static Result<T> OptionalETag<T>(this Result<T> result, string[]? expectedETags)
        where T : IAggregate =>
        expectedETags is null
            ? result
            : MatchETag(result, expectedETags);

    /// <summary>
    /// Validates that the aggregate's current <see cref="IAggregate.ETag"/> matches one of the expected values.
    /// <c>If-Match</c> is <b>required</b> — if <paramref name="expectedETags"/> is <c>null</c>,
    /// returns <see cref="PreconditionRequiredError"/> (HTTP 428).
    /// </summary>
    /// <typeparam name="T">An aggregate type implementing <see cref="IAggregate"/>.</typeparam>
    /// <param name="result">The result containing the loaded aggregate.</param>
    /// <param name="expectedETags">
    /// Parsed ETag values from <c>ETagHelper.ParseIfMatch()</c>.
    /// <c>null</c> → no header (428); <c>["*"]</c> → wildcard;
    /// <c>["a","b"]</c> → match any; <c>[]</c> → weak-only header (unsatisfiable → 412).
    /// </param>
    /// <returns>
    /// The original result if the ETag matches;
    /// <see cref="PreconditionRequiredError"/> (428) if no header was provided;
    /// <see cref="PreconditionFailedError"/> (412) if no ETag matches.
    /// </returns>
    /// <example>
    /// <code>
    /// // Required If-Match — rejects updates without the header
    /// return await _repo.GetByIdAsync(command.OrderId, ct)
    ///     .RequireETag(command.IfMatchETags)
    ///     .BindAsync(order => order.Submit());
    /// </code>
    /// </example>
    public static Result<T> RequireETag<T>(this Result<T> result, string[]? expectedETags)
        where T : IAggregate
    {
        if (result.IsFailure)
            return result;

        return expectedETags is null
            ? Result.Failure<T>(Error.PreconditionRequired("This operation requires an If-Match header."))
            : MatchETag(result, expectedETags);
    }

    /// <summary>Async Task overload of <see cref="OptionalETag{T}(Result{T}, string[])"/>.</summary>
    public static async Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>> resultTask, string[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).OptionalETag(expectedETags);

    /// <summary>Async ValueTask overload of <see cref="OptionalETag{T}(Result{T}, string[])"/>.</summary>
    public static async ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>> resultTask, string[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).OptionalETag(expectedETags);

    /// <summary>Async Task overload of <see cref="RequireETag{T}(Result{T}, string[])"/>.</summary>
    public static async Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>> resultTask, string[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).RequireETag(expectedETags);

    /// <summary>Async ValueTask overload of <see cref="RequireETag{T}(Result{T}, string[])"/>.</summary>
    public static async ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>> resultTask, string[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).RequireETag(expectedETags);

    private static Result<T> MatchETag<T>(Result<T> result, string[] expectedETags)
        where T : IAggregate
    {
        // Preserve any existing failure; ETag validation never overrides an existing error
        if (result.IsFailure)
            return result;

        // Empty array = header present but all tags were weak (unsatisfiable)
        if (expectedETags.Length == 0)
            return Result.Failure<T>(Error.PreconditionFailed(
                "If-Match header contains only weak ETags. Strong comparison is required."));

        // RFC 9110 §13.1.1: "*" matches any current entity
        if (expectedETags.Length == 1 && expectedETags[0] == "*")
            return result;

        // Check if aggregate ETag matches ANY of the provided tags
        return result.Ensure(
            aggregate => Array.Exists(expectedETags,
                tag => string.Equals(aggregate.ETag, tag, StringComparison.Ordinal)),
            Error.PreconditionFailed("Resource has been modified. Please reload and retry."));
    }

    #region Typed EntityTagValue overloads

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
    /// returns <see cref="PreconditionRequiredError"/> (HTTP 428).
    /// </summary>
    public static Result<T> RequireETag<T>(this Result<T> result, EntityTagValue[]? expectedETags)
        where T : IAggregate
    {
        if (result.IsFailure) return result;
        return expectedETags is null
            ? Result.Failure<T>(Error.PreconditionRequired("This operation requires an If-Match header."))
            : MatchETag(result, expectedETags);
    }

    /// <summary>Async Task overload of typed <see cref="OptionalETag{T}(Result{T}, EntityTagValue[])"/>.</summary>
    public static async Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).OptionalETag(expectedETags);

    /// <summary>Async ValueTask overload of typed <see cref="OptionalETag{T}(Result{T}, EntityTagValue[])"/>.</summary>
    public static async ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).OptionalETag(expectedETags);

    /// <summary>Async Task overload of typed <see cref="RequireETag{T}(Result{T}, EntityTagValue[])"/>.</summary>
    public static async Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).RequireETag(expectedETags);

    /// <summary>Async ValueTask overload of typed <see cref="RequireETag{T}(Result{T}, EntityTagValue[])"/>.</summary>
    public static async ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags)
        where T : IAggregate =>
        (await resultTask.ConfigureAwait(false)).RequireETag(expectedETags);

    private static Result<T> MatchETag<T>(Result<T> result, EntityTagValue[] expectedETags)
        where T : IAggregate
    {
        if (result.IsFailure) return result;

        if (expectedETags.Length == 0)
            return Result.Failure<T>(Error.PreconditionFailed("If-Match header contains only weak ETags. Strong comparison is required."));

        // Wildcard check
        if (expectedETags.Length == 1 && expectedETags[0].OpaqueTag == "*" && !expectedETags[0].IsWeak)
            return result;

        // Strong comparison: both must be strong and opaque-tags match
        return result.Ensure(
            aggregate => Array.Exists(expectedETags,
                tag => !tag.IsWeak && string.Equals(aggregate.ETag, tag.OpaqueTag, StringComparison.Ordinal)),
            Error.PreconditionFailed("Resource has been modified. Please reload and retry."));
    }

    #endregion
}