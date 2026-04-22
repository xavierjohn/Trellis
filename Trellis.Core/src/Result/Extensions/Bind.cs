namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides extension methods for binding (chaining) operations over Result values.
/// </summary>
/// <remarks>
/// Bind is the core operation in Railway Oriented Programming. It allows you to chain together
/// operations that can fail, automatically short-circuiting on the first failure.
/// If the input Result is a success, the bind function is called with the value.
/// If the input Result is a failure, the bind function is skipped and the failure is propagated.
/// </remarks>
[DebuggerStepThrough]
public static partial class BindExtensions
{
    /// <summary>
    /// Binds the result to a function that returns a new result.
    /// If the starting result is a success, the function is called with the value.
    /// If the starting result is a failure, the function is skipped and the failure is returned.
    /// </summary>
    /// <typeparam name="TValue">Type of the input result value.</typeparam>
    /// <typeparam name="TResult">Type of the output result value.</typeparam>
    /// <param name="result">The result to bind.</param>
    /// <param name="func">The function to call if the result is successful.</param>
    /// <returns>A new result from the function if success; otherwise the original failure.</returns>
    public static Result<TResult> Bind<TValue, TResult>(this Result<TValue> result, Func<TValue, Result<TResult>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (!result.TryGetValue(out var value))
            return Result.Fail<TResult>(result.Error);

        var output = func(value);
        output.LogActivityStatus();
        return output;
    }
}

/// <summary>
/// Provides asynchronous extension methods for binding (chaining) operations over Result values.
/// </summary>
[DebuggerStepThrough]
public static partial class BindExtensionsAsync
{
    /// <summary>
    /// Asynchronously binds the result to a function that returns a new result.
    /// </summary>
    /// <typeparam name="TValue">Type of the input result value.</typeparam>
    /// <typeparam name="TResult">Type of the output result value.</typeparam>
    /// <param name="result">The result to bind.</param>
    /// <param name="func">The async function to call if the result is successful.</param>
    /// <returns>A new result from the function if success; otherwise the original failure.</returns>
    public static async Task<Result<TResult>> BindAsync<TValue, TResult>(this Result<TValue> result, Func<TValue, Task<Result<TResult>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(BindExtensions.Bind));
        if (!result.TryGetValue(out var value))
            return Result.Fail<TResult>(result.Error);

        var output = await func(value).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Asynchronously binds a task result to a function that returns a new result.
    /// </summary>
    /// <typeparam name="TValue">Type of the input result value.</typeparam>
    /// <typeparam name="TResult">Type of the output result value.</typeparam>
    /// <param name="resultTask">The task containing the result to bind.</param>
    /// <param name="func">The async function to call if the result is successful.</param>
    /// <returns>A new result from the function if success; otherwise the original failure.</returns>
    public static async Task<Result<TResult>> BindAsync<TValue, TResult>(this Task<Result<TValue>> resultTask, Func<TValue, Task<Result<TResult>>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously binds a task result to a synchronous function that returns a new result.
    /// </summary>
    /// <typeparam name="TValue">Type of the input result value.</typeparam>
    /// <typeparam name="TResult">Type of the output result value.</typeparam>
    /// <param name="resultTask">The task containing the result to bind.</param>
    /// <param name="func">The function to call if the result is successful.</param>
    /// <returns>A new result from the function if success; otherwise the original failure.</returns>
    public static async Task<Result<TResult>> BindAsync<TValue, TResult>(this Task<Result<TValue>> resultTask, Func<TValue, Result<TResult>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.Bind(func);
    }

    /// <summary>
    /// Asynchronously binds a ValueTask result to a function that returns a new result.
    /// </summary>
    /// <typeparam name="TValue">Type of the input result value.</typeparam>
    /// <typeparam name="TResult">Type of the output result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result to bind.</param>
    /// <param name="valueTask">The async function to call if the result is successful.</param>
    /// <returns>A new result from the function if success; otherwise the original failure.</returns>
    public static async ValueTask<Result<TResult>> BindAsync<TValue, TResult>(this ValueTask<Result<TValue>> resultTask, Func<TValue, ValueTask<Result<TResult>>> valueTask)
    {
        ArgumentNullException.ThrowIfNull(valueTask);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(valueTask).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously binds a ValueTask result to a synchronous function that returns a new result.
    /// </summary>
    /// <typeparam name="TValue">Type of the input result value.</typeparam>
    /// <typeparam name="TResult">Type of the output result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result to bind.</param>
    /// <param name="func">The function to call if the result is successful.</param>
    /// <returns>A new result from the function if success; otherwise the original failure.</returns>
    public static async ValueTask<Result<TResult>> BindAsync<TValue, TResult>(this ValueTask<Result<TValue>> resultTask, Func<TValue, Result<TResult>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.Bind(func);
    }

    /// <summary>
    /// Binds the result to an async function that returns a ValueTask result.
    /// </summary>
    /// <typeparam name="TValue">Type of the input result value.</typeparam>
    /// <typeparam name="TResult">Type of the output result value.</typeparam>
    /// <param name="result">The result to bind.</param>
    /// <param name="valueTask">The async function to call if the result is successful.</param>
    /// <returns>A new result from the function if success; otherwise the original failure.</returns>
    public static async ValueTask<Result<TResult>> BindAsync<TValue, TResult>(this Result<TValue> result, Func<TValue, ValueTask<Result<TResult>>> valueTask)
    {
        ArgumentNullException.ThrowIfNull(valueTask);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(BindExtensions.Bind));
        if (!result.TryGetValue(out var value))
            return Result.Fail<TResult>(result.Error);

        var output = await valueTask(value).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }
}