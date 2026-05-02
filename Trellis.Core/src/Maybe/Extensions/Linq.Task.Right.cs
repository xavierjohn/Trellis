namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async LINQ <c>SelectMany</c> overload where the source is a synchronous <see cref="Maybe{T}"/>
/// and the collection selector returns a <see cref="Task{T}"/> of <see cref="Maybe{T}"/>.
/// </summary>
[DebuggerStepThrough]
public static class MaybeLinqExtensionsTaskRightAsync
{
    /// <summary>
    /// Projects a synchronous <see cref="Maybe{T}"/> through an async collection selector
    /// (LINQ <c>SelectMany</c>: sync source / async continuation).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <typeparam name="TCollection">Type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">Type of the final projected value.</typeparam>
    /// <param name="source">The synchronous source Maybe.</param>
    /// <param name="collectionSelector">An async function returning the intermediate Maybe.</param>
    /// <param name="resultSelector">A synchronous function combining the source and intermediate values.</param>
    /// <returns>A task producing the combined Maybe, or <c>None</c> if any step has no value.</returns>
    public static async Task<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(
        this Maybe<TSource> source,
        Func<TSource, Task<Maybe<TCollection>>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
        where TSource : notnull
        where TCollection : notnull
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(collectionSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        if (source.HasNoValue)
            return default;

        var c = await collectionSelector(source.Value).ConfigureAwait(false);
        return c.Map(cv => resultSelector(source.Value, cv));
    }
}