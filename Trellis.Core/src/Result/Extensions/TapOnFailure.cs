namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides extension methods for executing side effects on failed Results without changing the Result.
/// </summary>
/// <remarks>
/// <para>
/// TapOnFailure is the counterpart to <see cref="TapExtensions"/>. It allows you to perform side effects
/// (like logging, metrics, or debugging) when a Result is in a failed state, without altering the Result itself.
/// The action is only executed if the Result is a failure, and the original Result is always returned unchanged.
/// </para>
/// <para>
/// This operation runs on the <b>failure track</b> - it only executes when the Result has failed.
/// If the Result is successful, the operation is skipped entirely.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var result = GetUser(id)
///     .TapOnFailure(error => _logger.LogError("Failed to get user: {Error}", error.Detail))
///     .TapOnFailure(() => _metrics.IncrementCounter("user.get.failed"));
/// </code>
/// </example>
[DebuggerStepThrough]
public static partial class TapOnFailureExtensions
{
    /// <summary>
    /// Executes the given action if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to tap.</param>
    /// <param name="action">The action to execute if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    /// <remarks>
    /// This operation runs on the failure track only. If the result is successful, the action is not executed.
    /// </remarks>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<TValue> TapOnFailure<TValue>(this Result<TValue> result, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.IsFailure)
            action();

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Executes the given action with the error if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to tap.</param>
    /// <param name="action">The action to execute with the error if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    /// <remarks>
    /// This operation runs on the failure track only. If the result is successful, the action is not executed.
    /// The error object is passed to the action for inspection or logging.
    /// </remarks>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<TValue> TapOnFailure<TValue>(this Result<TValue> result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.IsFailure)
            action(result.Error);

        result.LogActivityStatus();
        return result;
    }
}

/// <summary>
/// Provides asynchronous extension methods for executing side effects on failed Results without changing the Result.
/// </summary>
/// <remarks>
/// <para>
/// These methods enable async error handling patterns such as logging to external services,
/// sending notifications, or recording metrics when operations fail.
/// </para>
/// <para>
/// All operations run on the <b>failure track</b> - they only execute when the Result has failed.
/// </para>
/// </remarks>
[DebuggerStepThrough]
public static partial class TapOnFailureExtensionsAsync
{
    /// <summary>
    /// Asynchronously executes the given action with the error if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to tap.</param>
    /// <param name="action">The action to execute with the error if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<TValue>> TapOnFailureAsync<TValue>(this Task<Result<TValue>> resultTask, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.TapOnFailure(action);
    }

    /// <summary>
    /// Asynchronously executes the given action if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to tap.</param>
    /// <param name="action">The action to execute if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<TValue>> TapOnFailureAsync<TValue>(this Task<Result<TValue>> resultTask, Action action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.TapOnFailure(action);
    }

    /// <summary>
    /// Asynchronously executes the given async action if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to tap.</param>
    /// <param name="func">The async action to execute if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<TValue>> TapOnFailureAsync<TValue>(this Result<TValue> result, Func<Task> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.IsFailure)
            await func().ConfigureAwait(false);

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously executes the given async action with the error if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to tap.</param>
    /// <param name="func">The async action to execute with the error if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<TValue>> TapOnFailureAsync<TValue>(this Result<TValue> result, Func<Error, Task> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.IsFailure)
            await func(result.Error).ConfigureAwait(false);

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously executes the given async action if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to tap.</param>
    /// <param name="func">The async action to execute if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<TValue>> TapOnFailureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<Task> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return await result.TapOnFailureAsync(func).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously executes the given async action with the error if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to tap.</param>
    /// <param name="func">The async action to execute with the error if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<TValue>> TapOnFailureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<Error, Task> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return await result.TapOnFailureAsync(func).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously executes the given action if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result to tap.</param>
    /// <param name="action">The action to execute if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<TValue>> TapOnFailureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.TapOnFailure(action);
    }

    /// <summary>
    /// Asynchronously executes the given action with the error if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result to tap.</param>
    /// <param name="action">The action to execute with the error if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<TValue>> TapOnFailureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.TapOnFailure(action);
    }

    /// <summary>
    /// Asynchronously executes the given async action if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to tap.</param>
    /// <param name="func">The async action to execute if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<TValue>> TapOnFailureAsync<TValue>(this Result<TValue> result, Func<ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.IsFailure)
            await func().ConfigureAwait(false);

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously executes the given async action with the error if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to tap.</param>
    /// <param name="func">The async action to execute with the error if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<TValue>> TapOnFailureAsync<TValue>(this Result<TValue> result, Func<Error, ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.IsFailure)
            await func(result.Error).ConfigureAwait(false);

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously executes the given async action if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result to tap.</param>
    /// <param name="func">The async action to execute if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<TValue>> TapOnFailureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Func<ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return await result.TapOnFailureAsync(func).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously executes the given async action with the error if the result is a failure. Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result to tap.</param>
    /// <param name="func">The async action to execute with the error if the result is a failure.</param>
    /// <returns>The original result unchanged.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<TValue>> TapOnFailureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Func<Error, ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return await result.TapOnFailureAsync(func).ConfigureAwait(false);
    }
}