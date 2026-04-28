namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides adapters that wrap already-computed <see cref="Result"/> values in completed task-like carriers.
/// </summary>
[DebuggerStepThrough]
public static class ResultTaskAdapterExtensions
{
    /// <summary>
    /// Wraps the result in a completed <see cref="Task{TResult}"/>.
    /// </summary>
    /// <param name="result">The result to wrap.</param>
    /// <returns>A completed task containing <paramref name="result"/>.</returns>
    public static Task<Result> AsTask(this Result result) =>
        Task.FromResult(result);

    /// <summary>
    /// Wraps the result in a completed <see cref="Task{TResult}"/>.
    /// </summary>
    /// <typeparam name="TValue">The success value type.</typeparam>
    /// <param name="result">The result to wrap.</param>
    /// <returns>A completed task containing <paramref name="result"/>.</returns>
    public static Task<Result<TValue>> AsTask<TValue>(this Result<TValue> result) =>
        Task.FromResult(result);

    /// <summary>
    /// Wraps the result in a completed <see cref="ValueTask{TResult}"/>.
    /// </summary>
    /// <param name="result">The result to wrap.</param>
    /// <returns>A completed value task containing <paramref name="result"/>.</returns>
    public static ValueTask<Result> AsValueTask(this Result result) =>
        new(result);

    /// <summary>
    /// Wraps the result in a completed <see cref="ValueTask{TResult}"/>.
    /// </summary>
    /// <typeparam name="TValue">The success value type.</typeparam>
    /// <param name="result">The result to wrap.</param>
    /// <returns>A completed value task containing <paramref name="result"/>.</returns>
    public static ValueTask<Result<TValue>> AsValueTask<TValue>(this Result<TValue> result) =>
        new(result);
}