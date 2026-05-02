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
}
