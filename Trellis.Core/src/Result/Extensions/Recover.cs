namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides extension methods for recovering from failures with a fallback value.
/// Unlike <see cref="RecoverOnFailureExtensions"/> which requires a function returning a Result,
/// Recover takes a simple fallback value and always produces a success.
/// </summary>
/// <remarks>
/// This is the complement to ToMaybe. Where ToMaybe says "I don't care about the error, give me Maybe",
/// Recover says "I don't care about the error, use this fallback and stay on the success track."
/// </remarks>
[DebuggerStepThrough]
public static class RecoverExtensions
{
    /// <summary>
    /// Recovers from a failure by substituting a fallback value.
    /// If the result is a success, returns it unchanged.
    /// If the result is a failure, returns a success with the fallback value.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to recover from.</param>
    /// <param name="fallback">The fallback value to use if the result is a failure.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<TValue> Recover<TValue>(this Result<TValue> result, TValue fallback)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Recover));

        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = Result.Ok(fallback);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Recovers from a failure by calling a function to produce a fallback value.
    /// If the result is a success, returns it unchanged.
    /// If the result is a failure, calls the function and returns a success with its return value.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to recover from.</param>
    /// <param name="fallbackFunc">The function to produce a fallback value if the result is a failure.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<TValue> Recover<TValue>(this Result<TValue> result, Func<TValue> fallbackFunc)
    {
        ArgumentNullException.ThrowIfNull(fallbackFunc);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Recover));

        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = Result.Ok(fallbackFunc());
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Recovers from a failure by calling a function that receives the error to produce a fallback value.
    /// If the result is a success, returns it unchanged.
    /// If the result is a failure, calls the function with the error and returns a success with its return value.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to recover from.</param>
    /// <param name="fallbackFunc">The function that receives the error and produces a fallback value.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<TValue> Recover<TValue>(this Result<TValue> result, Func<Error, TValue> fallbackFunc)
    {
        ArgumentNullException.ThrowIfNull(fallbackFunc);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Recover));

        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = Result.Ok(fallbackFunc(result.Error));
        output.LogActivityStatus();
        return output;
    }
}

/// <summary>
/// Provides asynchronous extension methods for recovering from failures with a fallback value.
/// </summary>
[DebuggerStepThrough]
public static class RecoverExtensionsAsync
{
    /// <summary>
    /// Asynchronously recovers from a failure by substituting a fallback value.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task producing the result to recover from.</param>
    /// <param name="fallback">The fallback value to use if the result is a failure.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    public static async Task<Result<TValue>> RecoverAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        TValue fallback)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return result.Recover(fallback);
    }

    /// <summary>
    /// Asynchronously recovers from a failure by calling a function to produce a fallback value.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task producing the result to recover from.</param>
    /// <param name="fallbackFunc">The function to produce a fallback value if the result is a failure.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    public static async Task<Result<TValue>> RecoverAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        Func<TValue> fallbackFunc)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(fallbackFunc);
        var result = await resultTask.ConfigureAwait(false);
        return result.Recover(fallbackFunc);
    }

    /// <summary>
    /// Asynchronously recovers from a failure by calling a function that receives the error.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task producing the result to recover from.</param>
    /// <param name="fallbackFunc">The function that receives the error and produces a fallback value.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    public static async Task<Result<TValue>> RecoverAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        Func<Error, TValue> fallbackFunc)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(fallbackFunc);
        var result = await resultTask.ConfigureAwait(false);
        return result.Recover(fallbackFunc);
    }

    /// <summary>
    /// Asynchronously recovers from a failure by substituting a fallback value (ValueTask).
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask producing the result to recover from.</param>
    /// <param name="fallback">The fallback value to use if the result is a failure.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    public static async ValueTask<Result<TValue>> RecoverAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        TValue fallback)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.Recover(fallback);
    }

    /// <summary>
    /// Asynchronously recovers from a failure by calling a function to produce a fallback value (ValueTask).
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask producing the result to recover from.</param>
    /// <param name="fallbackFunc">The function to produce a fallback value if the result is a failure.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    public static async ValueTask<Result<TValue>> RecoverAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        Func<TValue> fallbackFunc)
    {
        ArgumentNullException.ThrowIfNull(fallbackFunc);
        var result = await resultTask.ConfigureAwait(false);
        return result.Recover(fallbackFunc);
    }

    /// <summary>
    /// Asynchronously recovers from a failure by calling a function that receives the error (ValueTask).
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask producing the result to recover from.</param>
    /// <param name="fallbackFunc">The function that receives the error and produces a fallback value.</param>
    /// <returns>The original result if success; otherwise a success with the fallback value.</returns>
    public static async ValueTask<Result<TValue>> RecoverAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        Func<Error, TValue> fallbackFunc)
    {
        ArgumentNullException.ThrowIfNull(fallbackFunc);
        var result = await resultTask.ConfigureAwait(false);
        return result.Recover(fallbackFunc);
    }
}