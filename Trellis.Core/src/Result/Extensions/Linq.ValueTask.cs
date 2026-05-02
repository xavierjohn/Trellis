namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides LINQ query expression support for <see cref="ValueTask{T}"/> of <see cref="Result{T}"/>,
/// enabling C# query syntax over fully asynchronous Result-returning operations that may complete synchronously.
/// </summary>
/// <remarks>
/// These overloads match the C# query-pattern signatures (<c>Select</c>, <c>SelectMany</c>, <c>Where</c>)
/// for an async receiver and async continuations. Semantics mirror <see cref="ResultLinqExtensions"/>:
/// failures short-circuit subsequent steps; only success values flow through.
/// </remarks>
[DebuggerStepThrough]
public static class ResultLinqExtensionsValueTaskAsync
{
    /// <summary>
    /// Projects the value of an awaited <see cref="Result{T}"/> using a synchronous selector
    /// (LINQ <c>Select</c> over <see cref="ValueTask{T}"/>).
    /// </summary>
    /// <typeparam name="TIn">The type of the input value.</typeparam>
    /// <typeparam name="TOut">The type of the output value.</typeparam>
    /// <param name="resultTask">The asynchronous result to project.</param>
    /// <param name="selector">The projection function applied to the success value.</param>
    /// <returns>A value task producing a result with the projected value, or the original failure.</returns>
    public static async ValueTask<Result<TOut>> Select<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        Func<TIn, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(selector);
    }

    /// <summary>
    /// Projects the value of an awaited <see cref="Result{T}"/> through an async collection selector
    /// and combines the results (LINQ <c>SelectMany</c> over async/async, ValueTask variant).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <typeparam name="TCollection">Type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">Type of the final projected value.</typeparam>
    /// <param name="source">The asynchronous source result.</param>
    /// <param name="collectionSelector">An async function returning the intermediate result.</param>
    /// <param name="resultSelector">A synchronous function combining the source and intermediate values.</param>
    /// <returns>A value task producing the combined result, or the first failure encountered.</returns>
    public static async ValueTask<Result<TResult>> SelectMany<TSource, TCollection, TResult>(
        this ValueTask<Result<TSource>> source,
        Func<TSource, ValueTask<Result<TCollection>>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
    {
        ArgumentNullException.ThrowIfNull(collectionSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        var s = await source.ConfigureAwait(false);
        if (!s.TryGetValue(out var sValue))
            return Result.Fail<TResult>(s.Error);

        var c = await collectionSelector(sValue).ConfigureAwait(false);
        if (!c.TryGetValue(out var cValue))
            return Result.Fail<TResult>(c.Error);

        return Result.Ok(resultSelector(sValue, cValue));
    }

    /// <summary>
    /// Filters an awaited <see cref="Result{T}"/> by a synchronous predicate
    /// (LINQ <c>Where</c> over <see cref="ValueTask{T}"/>).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <param name="source">The asynchronous result to filter.</param>
    /// <param name="predicate">The predicate to test the success value.</param>
    /// <returns>The original success when the predicate is true; otherwise a generic "filtered out" failure.</returns>
    public static async ValueTask<Result<TSource>> Where<TSource>(
        this ValueTask<Result<TSource>> source,
        Func<TSource, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var result = await source.ConfigureAwait(false);
        return result.Where(predicate);
    }
}
