namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async LINQ <c>SelectMany</c> overload where the source is a synchronous <see cref="Result{T}"/>
/// and the collection selector returns a <see cref="Task{T}"/> of <see cref="Result{T}"/>.
/// </summary>
[DebuggerStepThrough]
public static class ResultLinqExtensionsTaskRightAsync
{
    /// <summary>
    /// Projects a synchronous <see cref="Result{T}"/> through an async collection selector
    /// (LINQ <c>SelectMany</c>: sync source / async continuation).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <typeparam name="TCollection">Type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">Type of the final projected value.</typeparam>
    /// <param name="source">The synchronous source result.</param>
    /// <param name="collectionSelector">An async function returning the intermediate result.</param>
    /// <param name="resultSelector">A synchronous function combining the source and intermediate values.</param>
    /// <returns>A task producing the combined result, or the first failure encountered.</returns>
    public static async Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(
        this Result<TSource> source,
        Func<TSource, Task<Result<TCollection>>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
    {
        ArgumentNullException.ThrowIfNull(collectionSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        if (!source.TryGetValue(out var sValue))
            return Result.Fail<TResult>(source.Error);

        var c = await collectionSelector(sValue).ConfigureAwait(false);
        if (!c.TryGetValue(out var cValue))
            return Result.Fail<TResult>(c.Error);

        return Result.Ok(resultSelector(sValue, cValue));
    }
}
