namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides LINQ query expression support for <see cref="ValueTask{T}"/> of <see cref="Maybe{T}"/>,
/// enabling C# query syntax over fully asynchronous Maybe-returning operations that may complete synchronously.
/// </summary>
/// <remarks>
/// These overloads match the C# query-pattern signatures (<c>Select</c>, <c>SelectMany</c>, <c>Where</c>)
/// for an async receiver and async continuations. Semantics mirror <see cref="MaybeLinqExtensions"/>:
/// a <c>None</c> short-circuits subsequent steps; only present values flow through.
/// </remarks>
[DebuggerStepThrough]
public static class MaybeLinqExtensionsValueTaskAsync
{
    /// <summary>
    /// Projects the value of an awaited <see cref="Maybe{T}"/> using a synchronous selector
    /// (LINQ <c>Select</c> over <see cref="ValueTask{T}"/>).
    /// </summary>
    /// <typeparam name="TIn">The type of the input value.</typeparam>
    /// <typeparam name="TOut">The type of the output value.</typeparam>
    /// <param name="maybeTask">The asynchronous Maybe to project.</param>
    /// <param name="selector">The projection function applied to the value.</param>
    /// <returns>A value task producing a Maybe with the projected value, or <c>None</c> if the input has no value.</returns>
    public static async ValueTask<Maybe<TOut>> Select<TIn, TOut>(
        this ValueTask<Maybe<TIn>> maybeTask,
        Func<TIn, TOut> selector)
        where TIn : notnull
        where TOut : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);
        var maybe = await maybeTask.ConfigureAwait(false);
        return maybe.Map(selector);
    }

    /// <summary>
    /// Projects the value of an awaited <see cref="Maybe{T}"/> through an async collection selector
    /// and combines the results (LINQ <c>SelectMany</c> over async/async, ValueTask variant).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <typeparam name="TCollection">Type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">Type of the final projected value.</typeparam>
    /// <param name="source">The asynchronous source Maybe.</param>
    /// <param name="collectionSelector">An async function returning the intermediate Maybe.</param>
    /// <param name="resultSelector">A synchronous function combining the source and intermediate values.</param>
    /// <returns>A value task producing the combined Maybe, or <c>None</c> if any step has no value.</returns>
    public static async ValueTask<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(
        this ValueTask<Maybe<TSource>> source,
        Func<TSource, ValueTask<Maybe<TCollection>>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
        where TSource : notnull
        where TCollection : notnull
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(collectionSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        var s = await source.ConfigureAwait(false);
        if (s.HasNoValue)
            return default;

        var c = await collectionSelector(s.Value).ConfigureAwait(false);
        return c.Map(cv => resultSelector(s.Value, cv));
    }

    /// <summary>
    /// Filters an awaited <see cref="Maybe{T}"/> by a synchronous predicate
    /// (LINQ <c>Where</c> over <see cref="ValueTask{T}"/>).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <param name="source">The asynchronous Maybe to filter.</param>
    /// <param name="predicate">The predicate to test the value.</param>
    /// <returns>The original Maybe when the predicate is true; otherwise <c>None</c>. <c>None</c> inputs stay <c>None</c>.</returns>
    public static async ValueTask<Maybe<TSource>> Where<TSource>(
        this ValueTask<Maybe<TSource>> source,
        Func<TSource, bool> predicate)
        where TSource : notnull
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var maybe = await source.ConfigureAwait(false);
        return maybe.Where(predicate);
    }
}