namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async LINQ <c>SelectMany</c> overload where the source is a <see cref="Task{T}"/> of <see cref="Maybe{T}"/>
/// and the collection selector is synchronous.
/// </summary>
[DebuggerStepThrough]
public static class MaybeLinqExtensionsTaskLeftAsync
{
    /// <summary>
    /// Projects an awaited <see cref="Maybe{T}"/> through a synchronous collection selector
    /// (LINQ <c>SelectMany</c>: async source / sync continuation).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <typeparam name="TCollection">Type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">Type of the final projected value.</typeparam>
    /// <param name="source">The asynchronous source Maybe.</param>
    /// <param name="collectionSelector">A synchronous function returning the intermediate Maybe.</param>
    /// <param name="resultSelector">A synchronous function combining the source and intermediate values.</param>
    /// <returns>A task producing the combined Maybe, or <c>None</c> if any step has no value.</returns>
    public static async Task<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(
        this Task<Maybe<TSource>> source,
        Func<TSource, Maybe<TCollection>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
        where TSource : notnull
        where TCollection : notnull
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(collectionSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        var s = await source.ConfigureAwait(false);
        return s.SelectMany(collectionSelector, resultSelector);
    }
}