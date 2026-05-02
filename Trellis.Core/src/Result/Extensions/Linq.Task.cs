namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides LINQ query expression support for <see cref="Task{T}"/> of <see cref="Result{T}"/>,
/// enabling C# query syntax over fully asynchronous Result-returning operations.
/// </summary>
/// <remarks>
/// <para>
/// These overloads match the C# query-pattern signatures (<c>Select</c>, <c>SelectMany</c>, <c>Where</c>)
/// for an async receiver and async continuations. With them, <c>from x in GetAsync()</c> compiles when
/// <c>GetAsync()</c> returns <c>Task&lt;Result&lt;T&gt;&gt;</c>, removing the need to <c>await</c> each
/// step and re-enter a sync query block.
/// </para>
/// <para>
/// The semantics mirror the synchronous <see cref="ResultLinqExtensions"/> overloads: failures
/// short-circuit subsequent steps; only success values are passed to the next selector.
/// </para>
/// </remarks>
[DebuggerStepThrough]
public static class ResultLinqExtensionsTaskAsync
{
    /// <summary>
    /// Projects the value of a successful awaited <see cref="Result{T}"/> using a synchronous selector
    /// (LINQ <c>Select</c> over <see cref="Task{T}"/>).
    /// </summary>
    /// <typeparam name="TIn">The type of the input value.</typeparam>
    /// <typeparam name="TOut">The type of the output value.</typeparam>
    /// <param name="resultTask">The asynchronous result to project.</param>
    /// <param name="selector">The projection function applied to the success value.</param>
    /// <returns>A task producing a result with the projected value, or the original failure.</returns>
    public static async Task<Result<TOut>> Select<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(selector);
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(selector);
    }

    /// <summary>
    /// Projects the value of an awaited <see cref="Result{T}"/> through an async collection selector
    /// and combines the results (LINQ <c>SelectMany</c> over async/async).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <typeparam name="TCollection">Type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">Type of the final projected value.</typeparam>
    /// <param name="source">The asynchronous source result.</param>
    /// <param name="collectionSelector">An async function returning the intermediate result.</param>
    /// <param name="resultSelector">A synchronous function combining the source and intermediate values.</param>
    /// <returns>A task producing the combined result, or the first failure encountered.</returns>
    public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(
        this Task<Result<TSource>> source,
        Func<TSource, Task<Result<TCollection>>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(collectionSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        return source.BindAsync(s => collectionSelector(s).MapAsync(c => resultSelector(s, c)));
    }

    /// <summary>
    /// Filters an awaited <see cref="Result{T}"/> by a synchronous predicate (LINQ <c>Where</c> over async).
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <param name="source">The asynchronous result to filter.</param>
    /// <param name="predicate">The predicate to test the success value.</param>
    /// <returns>The original success when the predicate is true; otherwise a generic "filtered out" failure.</returns>
    /// <remarks>
    /// For meaningful error messages, prefer <see cref="EnsureExtensionsAsync"/> directly.
    /// </remarks>
    public static async Task<Result<TSource>> Where<TSource>(
        this Task<Result<TSource>> source,
        Func<TSource, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        var result = await source.ConfigureAwait(false);
        return result.Where(predicate);
    }
}
