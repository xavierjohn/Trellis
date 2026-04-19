namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Transforms a collection of items into a Result containing a collection, 
/// short-circuiting on the first failure.
/// Useful for processing collections where each item can fail independently.
/// </summary>
/// <example>
/// <code>
/// // Validate all items in a collection
/// var items = new[] { "item1", "item2", "item3" };
/// var result = items.Traverse(item => ValidateItem(item));
/// 
/// // Process collection asynchronously with cancellation
/// var orderIds = new[] { "order1", "order2", "order3" };
/// var orders = await orderIds.TraverseAsync(
///     async (id, ct) => await FetchOrderAsync(id, ct),
///     cancellationToken
/// );
/// 
/// // If any item fails, the entire operation fails with that error
/// // If all succeed, returns Success with IReadOnlyList of all results
/// </code>
/// </example>
[DebuggerStepThrough]
public static class TraverseExtensions
{
    /// <summary>
    /// Transforms a collection of items into a Result containing all transformed items.
    /// Short-circuits on the first failure.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Transformation function returning a Result.</param>
    /// <returns>Success with all items if all succeed; otherwise the first failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, Result<TOut>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var results = new List<TOut>();

        foreach (var item in source)
        {
            var result = selector(item);
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(result.Value);
        }

        return Result.Ok<IReadOnlyList<TOut>>(results);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items into a Result containing all transformed items.
    /// Short-circuits on the first failure.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async transformation function returning a Result.</param>
    /// <returns>Task producing Success with all items if all succeed; otherwise the first failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static async Task<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, Task<Result<TOut>>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var results = new List<TOut>();

        foreach (var item in source)
        {
            var result = await selector(item).ConfigureAwait(false);
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(result.Value);
        }

        return Result.Ok<IReadOnlyList<TOut>>(results);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items into a Result containing all transformed items.
    /// Short-circuits on the first failure. Supports cancellation.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async transformation function with cancellation support.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>Task producing Success with all items if all succeed; otherwise the first failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static async Task<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, CancellationToken, Task<Result<TOut>>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var results = new List<TOut>();

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await selector(item, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(result.Value);
        }

        return Result.Ok<IReadOnlyList<TOut>>(results);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items using ValueTask for zero-allocation scenarios.
    /// Short-circuits on the first failure.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async transformation function returning a Result.</param>
    /// <returns>ValueTask producing Success with all items if all succeed; otherwise the first failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static async ValueTask<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, ValueTask<Result<TOut>>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var results = new List<TOut>();

        foreach (var item in source)
        {
            var result = await selector(item).ConfigureAwait(false);
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(result.Value);
        }

        return Result.Ok<IReadOnlyList<TOut>>(results);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items using ValueTask with cancellation support.
    /// Short-circuits on the first failure.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async transformation function with cancellation support.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>ValueTask producing Success with all items if all succeed; otherwise the first failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static async ValueTask<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, CancellationToken, ValueTask<Result<TOut>>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var results = new List<TOut>();

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await selector(item, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(result.Value);
        }

        return Result.Ok<IReadOnlyList<TOut>>(results);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items using a non-generic Result selector.
    /// Short-circuits on the first failure. Returns a non-generic Result (no collected values).
    /// </summary>
    public static async Task<Result> TraverseAsync<TIn>(
        this IEnumerable<TIn> source,
        Func<TIn, CancellationToken, Task<Result>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await selector(item, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail(result.Error);
            }
        }

        return Result.Ok();
    }
}