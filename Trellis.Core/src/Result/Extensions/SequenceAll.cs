namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Accumulating-error counterpart to <see cref="TraverseExtensions.Sequence{T}(IEnumerable{Result{T}})"/>.
/// Runs through every item; folds failures via the existing <see cref="CombineErrorExtensions.Combine"/>
/// extension so two <see cref="Error.UnprocessableContent"/> failures merge their fields/rules and
/// heterogeneous failures flatten into <see cref="Error.Aggregate"/>.
/// </summary>
[DebuggerStepThrough]
public static class SequenceAllExtensions
{
    /// <summary>
    /// Sequences a collection of <see cref="Result{T}"/> into a single result containing every value
    /// in source order. Unlike <see cref="TraverseExtensions.Sequence{T}(IEnumerable{Result{T}})"/>,
    /// this overload does not short-circuit: it visits every item and folds failures via
    /// <see cref="CombineErrorExtensions.Combine"/>.
    /// </summary>
    /// <typeparam name="T">Type of value carried by each result.</typeparam>
    /// <param name="source">Source collection of results.</param>
    /// <returns>
    /// Success carrying every value in source order if every item succeeds; otherwise a failure
    /// carrying the combined error. A single failure is returned unchanged (no <see cref="Error.Aggregate"/> wrap).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public static Result<IReadOnlyList<T>> SequenceAll<T>(this IEnumerable<Result<T>> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var values = source is ICollection<Result<T>> coll ? new List<T>(coll.Count) : new List<T>();
        Error? accumulated = null;

        foreach (var result in source)
        {
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
            return Result.Fail<IReadOnlyList<T>>(accumulated);
        }

        return Result.Ok<IReadOnlyList<T>>(values);
    }

    /// <summary>
    /// Sequences a collection of no-payload <c>Result&lt;Unit&gt;</c> values into a single
    /// <c>Result&lt;Unit&gt;</c>, accumulating every failure via <see cref="CombineErrorExtensions.Combine"/>.
    /// </summary>
    /// <param name="source">Source collection of results.</param>
    /// <returns>
    /// Success if every item succeeds; otherwise a failure carrying the combined error. A single
    /// failure is returned unchanged (no <see cref="Error.Aggregate"/> wrap).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public static Result<Unit> SequenceAll(this IEnumerable<Result<Unit>> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var activity = RopTrace.ActivitySource.StartActivity();
        Error? accumulated = null;

        foreach (var result in source)
        {
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
