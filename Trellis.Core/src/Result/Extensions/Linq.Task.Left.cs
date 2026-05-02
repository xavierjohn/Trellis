namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async LINQ <c>SelectMany</c> overload where the source is a <see cref="Task{T}"/> of <see cref="Result{T}"/>
/// and the collection selector is synchronous.
/// </summary>
[DebuggerStepThrough]
public static class ResultLinqExtensionsTaskLeftAsync
{
    /// <summary>
    /// Projects an awaited <see cref="Result{T}"/> through a synchronous collection selector
    /// (LINQ <c>SelectMany</c>: async source / sync continuation).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <typeparam name="TCollection">Type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">Type of the final projected value.</typeparam>
    /// <param name="source">The asynchronous source result.</param>
    /// <param name="collectionSelector">A synchronous function returning the intermediate result.</param>
    /// <param name="resultSelector">A synchronous function combining the source and intermediate values.</param>
    /// <returns>A task producing the combined result, or the first failure encountered.</returns>
    public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(
        this Task<Result<TSource>> source,
        Func<TSource, Result<TCollection>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(collectionSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        return source.BindAsync(s => collectionSelector(s).Map(c => resultSelector(s, c)));
    }
}
