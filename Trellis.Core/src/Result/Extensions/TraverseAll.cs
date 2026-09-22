namespace Trellis;

using System.Diagnostics;
using System.Runtime.CompilerServices;

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
    [OverloadResolutionPriority(1)]
    public static Result<IReadOnlyList<TOut>> TraverseAll<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, Result<TOut>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var values = source is ICollection<TIn> coll ? new List<TOut>(coll.Count) : new List<TOut>();
        Error? accumulated = null;
        bool persistOnFailure = false;

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
                persistOnFailure |= result.PersistOnFailureFlag;
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.ProjectFailure<IReadOnlyList<TOut>>(accumulated, persistOnFailure);
        }

        return Result.Ok<IReadOnlyList<TOut>>(values);
    }

    /// <summary>
    /// Transforms every item with its zero-based source index, accumulating all failures.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to enumerate once.</param>
    /// <param name="selector">Transformation receiving the item and its original zero-based index.</param>
    /// <returns>
    /// Success with every transformed value in source order, or the combined failure.
    /// A single error is preserved; persist-on-failure intent is accumulated.
    /// </returns>
    /// <remarks>
    /// Indices advance for every item, including failures. Selector and enumeration exceptions propagate.
    /// The unindexed overload has higher overload-resolution priority to preserve null-literal calls.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="OverflowException">Thrown before invoking the selector for an index greater than <see cref="int.MaxValue"/>.</exception>
    public static Result<IReadOnlyList<TOut>> TraverseAll<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, int, Result<TOut>> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        var index = -1;
        return source.TraverseAll(item => selector(item, checked(++index)));
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
        bool persistOnFailure = false;

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
                persistOnFailure |= result.PersistOnFailureFlag;
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.ProjectFailure<IReadOnlyList<TOut>>(accumulated, persistOnFailure);
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
        bool persistOnFailure = false;

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
                persistOnFailure |= result.PersistOnFailureFlag;
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.ProjectFailure<IReadOnlyList<TOut>>(accumulated, persistOnFailure);
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
        bool persistOnFailure = false;

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
                persistOnFailure |= result.PersistOnFailureFlag;
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.ProjectFailure<IReadOnlyList<TOut>>(accumulated, persistOnFailure);
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
        bool persistOnFailure = false;

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
                persistOnFailure |= result.PersistOnFailureFlag;
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.ProjectFailure<IReadOnlyList<TOut>>(accumulated, persistOnFailure);
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
        bool persistOnFailure = false;

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await selector(item, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                accumulated = accumulated.Combine(result.Error);
                persistOnFailure |= result.PersistOnFailureFlag;
            }
        }

        if (accumulated is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result.ProjectFailure<Unit>(accumulated, persistOnFailure);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Sequentially transforms every item with its zero-based source index and cancellation token,
    /// accumulating all failures.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to enumerate once.</param>
    /// <param name="selector">Async transformation receiving the item, original zero-based index, and cancellation token.</param>
    /// <param name="cancellationToken">Token checked before each selector invocation and forwarded to the selector.</param>
    /// <returns>A task containing every transformed value in source order, or the combined failure.</returns>
    /// <remarks>
    /// Indices advance for every item, including failures. Exceptions and cancellation propagate
    /// through the returned task, abandoning accumulated state. The token argument is optional;
    /// the selector always takes three parameters to avoid ambiguity with unindexed token-aware selectors.
    /// Inline async lambdas prefer this Task overload over the ValueTask overload.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is observed.</exception>
    /// <exception cref="OverflowException">Thrown before invoking the selector for an index greater than <see cref="int.MaxValue"/>.</exception>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, int, CancellationToken, Task<Result<TOut>>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        var index = -1;
        return await source.TraverseAllAsync(
            (item, token) => selector(item, checked(++index), token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sequentially transforms every item using a ValueTask selector with its zero-based source index
    /// and cancellation token, accumulating all failures.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <typeparam name="TOut">Type of output items.</typeparam>
    /// <param name="source">Source collection to enumerate once.</param>
    /// <param name="selector">Async transformation receiving the item, original zero-based index, and cancellation token.</param>
    /// <param name="cancellationToken">Token checked before each selector invocation and forwarded to the selector.</param>
    /// <returns>A ValueTask containing every transformed value in source order, or the combined failure.</returns>
    /// <remarks>
    /// Indices advance for every item, including failures. Exceptions and cancellation propagate
    /// through the returned ValueTask, abandoning accumulated state. The token argument is optional;
    /// the selector always takes three parameters to avoid ambiguity with unindexed token-aware selectors.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is observed.</exception>
    /// <exception cref="OverflowException">Thrown before invoking the selector for an index greater than <see cref="int.MaxValue"/>.</exception>
    public static async ValueTask<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, int, CancellationToken, ValueTask<Result<TOut>>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        var index = -1;
        return await source.TraverseAllAsync(
            (item, token) => selector(item, checked(++index), token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sequentially invokes a no-payload selector with each item's zero-based source index and
    /// cancellation token, accumulating all failures.
    /// </summary>
    /// <typeparam name="TIn">Type of input items.</typeparam>
    /// <param name="source">Source collection to enumerate once.</param>
    /// <param name="selector">Async no-payload selector receiving the item, original zero-based index, and cancellation token.</param>
    /// <param name="cancellationToken">Token checked before each selector invocation and forwarded to the selector.</param>
    /// <returns>A task containing success with no payload, or the combined failure.</returns>
    /// <remarks>
    /// Indices advance for every item, including failures. Exceptions and cancellation propagate
    /// through the returned task, abandoning accumulated state. The token argument is optional;
    /// the selector always takes three parameters to avoid ambiguity with unindexed token-aware selectors.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is observed.</exception>
    /// <exception cref="OverflowException">Thrown before invoking the selector for an index greater than <see cref="int.MaxValue"/>.</exception>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<Unit>> TraverseAllAsync<TIn>(
        this IEnumerable<TIn> source,
        Func<TIn, int, CancellationToken, Task<Result<Unit>>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        var index = -1;
        return await source.TraverseAllAsync(
            (item, token) => selector(item, checked(++index), token), cancellationToken).ConfigureAwait(false);
    }
}