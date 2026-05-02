namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Accumulating-error counterpart to <see cref="TraverseExtensions.Traverse{TIn, TOut}"/>.
/// Runs the selector over every item (no short-circuit) and folds failures via the existing
/// <see cref="CombineErrorExtensions.Combine"/> extension. Useful for form-style validation where
/// every error matters, not just the first one encountered.
/// </summary>
[DebuggerStepThrough]
public static class TraverseAllExtensions
{
    /// <summary>
    /// Transforms a collection of items into a Result containing all transformed items, accumulating
    /// any failures via <see cref="CombineErrorExtensions.Combine"/>. Unlike
    /// <see cref="TraverseExtensions.Traverse{TIn, TOut}"/>, this method does not short-circuit:
    /// the selector is invoked for every item.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Transformation function returning a Result.</param>
    /// <returns>
    /// Success carrying every transformed value in source order if every item succeeds; otherwise a
    /// failure carrying the combined error. A single failure is returned unchanged (no
    /// <see cref="Error.Aggregate"/> wrap).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static Result<IReadOnlyList<TOut>> TraverseAll<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, Result<TOut>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var values = source is ICollection<TIn> coll ? new List<TOut>(coll.Count) : new List<TOut>();
        Error? accumulated = null;

        foreach (var item in source)
        {
            var result = selector(item);
            if (result.TryGetValue(out var value))
            {
                values.Add(value);
            }
            else
            {
                accumulated = accumulated.Combine(result.Error);
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.Fail<IReadOnlyList<TOut>>(accumulated);
        }

        return Result.Ok<IReadOnlyList<TOut>>(values);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items into a Result containing all transformed items,
    /// accumulating failures via <see cref="CombineErrorExtensions.Combine"/>. Selectors are awaited
    /// sequentially (mirroring <see cref="TraverseExtensions.TraverseAsync{TIn, TOut}(IEnumerable{TIn}, Func{TIn, Task{Result{TOut}}})"/>).
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async transformation function returning a Result.</param>
    /// <returns>
    /// Task producing success with every transformed value if all items succeed; otherwise a failure
    /// carrying the combined error.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static async Task<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, Task<Result<TOut>>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var values = source is ICollection<TIn> coll ? new List<TOut>(coll.Count) : new List<TOut>();
        Error? accumulated = null;

        foreach (var item in source)
        {
            var result = await selector(item).ConfigureAwait(false);
            if (result.TryGetValue(out var value))
            {
                values.Add(value);
            }
            else
            {
                accumulated = accumulated.Combine(result.Error);
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.Fail<IReadOnlyList<TOut>>(accumulated);
        }

        return Result.Ok<IReadOnlyList<TOut>>(values);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items, accumulating failures via
    /// <see cref="CombineErrorExtensions.Combine"/>. Supports cancellation. Mirrors
    /// <see cref="TraverseExtensions.TraverseAsync{TIn, TOut}(IEnumerable{TIn}, Func{TIn, CancellationToken, Task{Result{TOut}}}, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async transformation function with cancellation support.</param>
    /// <param name="cancellationToken">Cancellation token to observe. Cancellation throws <see cref="OperationCanceledException"/> and abandons accumulated state.</param>
    /// <returns>
    /// Task producing success with every transformed value if all items succeed; otherwise a failure
    /// carrying the combined error.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, CancellationToken, Task<Result<TOut>>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var values = source is ICollection<TIn> coll ? new List<TOut>(coll.Count) : new List<TOut>();
        Error? accumulated = null;

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await selector(item, cancellationToken).ConfigureAwait(false);
            if (result.TryGetValue(out var value))
            {
                values.Add(value);
            }
            else
            {
                accumulated = accumulated.Combine(result.Error);
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.Fail<IReadOnlyList<TOut>>(accumulated);
        }

        return Result.Ok<IReadOnlyList<TOut>>(values);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items using <see cref="ValueTask{T}"/>, accumulating
    /// failures via <see cref="CombineErrorExtensions.Combine"/>. Mirrors
    /// <see cref="TraverseExtensions.TraverseAsync{TIn, TOut}(IEnumerable{TIn}, Func{TIn, ValueTask{Result{TOut}}})"/>.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async transformation function returning a Result.</param>
    /// <returns>
    /// ValueTask producing success with every transformed value if all items succeed; otherwise a
    /// failure carrying the combined error.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static async ValueTask<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, ValueTask<Result<TOut>>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var values = source is ICollection<TIn> coll ? new List<TOut>(coll.Count) : new List<TOut>();
        Error? accumulated = null;

        foreach (var item in source)
        {
            var result = await selector(item).ConfigureAwait(false);
            if (result.TryGetValue(out var value))
            {
                values.Add(value);
            }
            else
            {
                accumulated = accumulated.Combine(result.Error);
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.Fail<IReadOnlyList<TOut>>(accumulated);
        }

        return Result.Ok<IReadOnlyList<TOut>>(values);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items using <see cref="ValueTask{T}"/> with cancellation
    /// support, accumulating failures via <see cref="CombineErrorExtensions.Combine"/>. Mirrors
    /// <see cref="TraverseExtensions.TraverseAsync{TIn, TOut}(IEnumerable{TIn}, Func{TIn, CancellationToken, ValueTask{Result{TOut}}}, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async transformation function with cancellation support.</param>
    /// <param name="cancellationToken">Cancellation token to observe. Cancellation throws <see cref="OperationCanceledException"/> and abandons accumulated state.</param>
    /// <returns>
    /// ValueTask producing success with every transformed value if all items succeed; otherwise a
    /// failure carrying the combined error.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async ValueTask<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, CancellationToken, ValueTask<Result<TOut>>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var values = source is ICollection<TIn> coll ? new List<TOut>(coll.Count) : new List<TOut>();
        Error? accumulated = null;

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await selector(item, cancellationToken).ConfigureAwait(false);
            if (result.TryGetValue(out var value))
            {
                values.Add(value);
            }
            else
            {
                accumulated = accumulated.Combine(result.Error);
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.Fail<IReadOnlyList<TOut>>(accumulated);
        }

        return Result.Ok<IReadOnlyList<TOut>>(values);
    }

    /// <summary>
    /// Asynchronously transforms a collection of items using a no-payload <c>Result&lt;Unit&gt;</c>
    /// selector with cancellation support, accumulating failures via
    /// <see cref="CombineErrorExtensions.Combine"/>. Mirrors
    /// <see cref="TraverseExtensions.TraverseAsync{TIn}(IEnumerable{TIn}, Func{TIn, CancellationToken, Task{Result{Unit}}}, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <param name="source">Source collection to transform.</param>
    /// <param name="selector">Async no-payload selector with cancellation support.</param>
    /// <param name="cancellationToken">Cancellation token to observe. Cancellation throws <see cref="OperationCanceledException"/> and abandons accumulated state.</param>
    /// <returns>
    /// Task producing success if all items succeed; otherwise a failure carrying the combined error.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<Unit>> TraverseAllAsync<TIn>(
        this IEnumerable<TIn> source,
        Func<TIn, CancellationToken, Task<Result<Unit>>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        Error? accumulated = null;

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await selector(item, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
                accumulated = accumulated.Combine(result.Error);
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.Fail(accumulated);
        }

        return Result.Ok();
    }
}
