namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides extension methods for mapping (transforming) values inside Result objects.
/// </summary>
/// <remarks>
/// Map is used when you have a function that always succeeds and you want to transform the value
/// inside a Result without changing its success/failure state. Unlike Bind, Map functions don't
/// return a Result - they return a plain value that gets automatically wrapped in a success Result.
/// If the input Result is a failure, the Map function is skipped and the failure is propagated.
/// </remarks>
[DebuggerStepThrough]
public static partial class MapExtensions
{
    /// <summary>
    /// Maps the value of a successful result to a new value using the provided function.
    /// If the result is a failure, returns the failure without calling the function.
    /// </summary>
    /// <typeparam name="TIn">Type of the input result value.</typeparam>
    /// <typeparam name="TOut">Type of the output result value.</typeparam>
    /// <param name="result">The result to map.</param>
    /// <param name="func">The function to transform the value if the result is successful.</param>
    /// <returns>A new success result with the transformed value if success; otherwise the original failure.</returns>
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (!result.TryGetValue(out var value))
            return Result.Fail<TOut>(result.Error);

        return Result.Ok<TOut>(func(value));
    }
}

/// <summary>
/// Provides asynchronous extension methods for mapping (transforming) values inside Result objects.
/// </summary>
[DebuggerStepThrough]
public static partial class MapExtensionsAsync
{
    /// <summary>
    /// Asynchronously maps the value of a successful result to a new value using the provided async function.
    /// </summary>
    /// <typeparam name="TIn">Type of the input result value.</typeparam>
    /// <typeparam name="TOut">Type of the output result value.</typeparam>
    /// <param name="result">The result to map.</param>
    /// <param name="func">The async function to transform the value if the result is successful.</param>
    /// <returns>A new success result with the transformed value if success; otherwise the original failure.</returns>
    public static async Task<Result<TOut>> MapAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, Task<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(MapExtensions.Map));
        if (!result.TryGetValue(out var value))
            return Result.Fail<TOut>(result.Error);

        TOut outValue = await func(value).ConfigureAwait(false);

        return Result.Ok<TOut>(outValue);
    }

    /// <summary>
    /// Asynchronously maps the value of a task result to a new value using a synchronous function.
    /// </summary>
    /// <typeparam name="TIn">Type of the input result value.</typeparam>
    /// <typeparam name="TOut">Type of the output result value.</typeparam>
    /// <param name="resultTask">The task containing the result to map.</param>
    /// <param name="func">The function to transform the value if the result is successful.</param>
    /// <returns>A new success result with the transformed value if success; otherwise the original failure.</returns>
    public static async Task<Result<TOut>> MapAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, TOut> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<TIn> result = await resultTask.ConfigureAwait(false);

        return result.Map(func);
    }

    /// <summary>
    /// Asynchronously maps the value of a task result to a new value using an async function.
    /// </summary>
    /// <typeparam name="TIn">Type of the input result value.</typeparam>
    /// <typeparam name="TOut">Type of the output result value.</typeparam>
    /// <param name="resultTask">The task containing the result to map.</param>
    /// <param name="func">The async function to transform the value if the result is successful.</param>
    /// <returns>A new success result with the transformed value if success; otherwise the original failure.</returns>
    public static async Task<Result<TOut>> MapAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, Task<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<TIn> result = await resultTask.ConfigureAwait(false);

        return await result.MapAsync(func).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously maps the value of a successful result to a new value using a ValueTask function.
    /// </summary>
    /// <typeparam name="TIn">Type of the input result value.</typeparam>
    /// <typeparam name="TOut">Type of the output result value.</typeparam>
    /// <param name="result">The result to map.</param>
    /// <param name="func">The async function to transform the value if the result is successful.</param>
    /// <returns>A new success result with the transformed value if success; otherwise the original failure.</returns>
    public static async ValueTask<Result<TOut>> MapAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, ValueTask<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(MapExtensions.Map));
        if (!result.TryGetValue(out var value))
            return Result.Fail<TOut>(result.Error);

        TOut outValue = await func(value).ConfigureAwait(false);

        return Result.Ok<TOut>(outValue);
    }

    /// <summary>
    /// Asynchronously maps the value of a ValueTask result to a new value using a synchronous function.
    /// </summary>
    /// <typeparam name="TIn">Type of the input result value.</typeparam>
    /// <typeparam name="TOut">Type of the output result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result to map.</param>
    /// <param name="func">The function to transform the value if the result is successful.</param>
    /// <returns>A new success result with the transformed value if success; otherwise the original failure.</returns>
    public static async ValueTask<Result<TOut>> MapAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<TIn> result = await resultTask.ConfigureAwait(false);

        return result.Map(func);
    }

    /// <summary>
    /// Asynchronously maps the value of a ValueTask result to a new value using an async function.
    /// </summary>
    /// <typeparam name="TIn">Type of the input result value.</typeparam>
    /// <typeparam name="TOut">Type of the output result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result to map.</param>
    /// <param name="func">The async function to transform the value if the result is successful.</param>
    /// <returns>A new success result with the transformed value if success; otherwise the original failure.</returns>
    public static async ValueTask<Result<TOut>> MapAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, ValueTask<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<TIn> result = await resultTask.ConfigureAwait(false);

        return await result.MapAsync(func).ConfigureAwait(false);
    }
}