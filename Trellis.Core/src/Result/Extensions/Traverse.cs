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
            if (!result.TryGetValue(out var value))
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(value);
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
            if (!result.TryGetValue(out var value))
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(value);
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
            if (!result.TryGetValue(out var value))
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(value);
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
            if (!result.TryGetValue(out var value))
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(value);
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
            if (!result.TryGetValue(out var value))
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<TOut>>(result.Error);
            }

            results.Add(value);
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

    /// <summary>
    /// Sequences a collection of <see cref="Result{T}"/> into a single Result containing
    /// all values in source order. Short-circuits on the first failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Sequence</c> is the identity-selector form of <see cref="Traverse{TIn,TOut}"/>:
    /// <c>source.Sequence()</c> ≡ <c>source.Traverse(x =&gt; x)</c>. Use it when you already
    /// have an <see cref="IEnumerable{T}"/> of Results — typically the output of a
    /// <c>Select</c> over a function that returns <see cref="Result{T}"/>.
    /// </para>
    /// <para>
    /// Failure semantics are first-failure-wins (matching <see cref="Traverse{TIn,TOut}"/>
    /// and the current first-failure-wins design). For per-field validation aggregation,
    /// use the <c>Validate</c> builder which accumulates into a single <see cref="Error.UnprocessableContent"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Aggregate per-item Result&lt;Money&gt; subtotals, then sum the values.
    /// var subtotalsResult = lineItems
    ///     .Select(item =&gt; item.ComputeSubtotal())   // IEnumerable&lt;Result&lt;Money&gt;&gt;
    ///     .Sequence();                                // Result&lt;IReadOnlyList&lt;Money&gt;&gt;
    ///
    /// var totalResult = subtotalsResult.Bind(Money.Sum);
    /// </code>
    /// </example>
    /// <typeparam name="T">Type of value carried by each result.</typeparam>
    /// <param name="source">Source collection of results.</param>
    /// <returns>Success carrying all values in order if every item succeeds; otherwise the first failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public static Result<IReadOnlyList<T>> Sequence<T>(this IEnumerable<Result<T>> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var values = source is ICollection<Result<T>> coll ? new List<T>(coll.Count) : new List<T>();

        foreach (var result in source)
        {
            if (!result.TryGetValue(out var value))
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail<IReadOnlyList<T>>(result.Error);
            }

            values.Add(value);
        }

        return Result.Ok<IReadOnlyList<T>>(values);
    }

    /// <summary>
    /// Sequences a collection of no-payload <see cref="Result"/> values into a single
    /// <see cref="Result"/>. Short-circuits on the first failure.
    /// </summary>
    /// <remarks>
    /// First-failure-wins semantics, matching <see cref="Traverse{TIn,TOut}"/>.
    /// For per-field validation aggregation, use the <c>Validate</c> builder.
    /// </remarks>
    /// <param name="source">Source collection of results.</param>
    /// <returns>Success if every item succeeds; otherwise the first failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public static Result Sequence(this IEnumerable<Result> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var activity = RopTrace.ActivitySource.StartActivity();

        foreach (var result in source)
        {
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Fail(result.Error);
            }
        }

        return Result.Ok();
    }
}