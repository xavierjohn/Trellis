namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides extension methods for recovering from failed results by executing fallback operations.
/// </summary>
/// <remarks>
/// <para>
///  This operation runs on the <b>failure track</b> - it only executes when the Result has failed.
///  If the Result is successful, the recovery function is not called.
///  </para>
/// Recovery allows you to provide alternative paths when a Result fails, similar to try-catch recovery logic
/// but in a functional style. This is useful for implementing fallback strategies, default values, or error recovery.
/// </remarks>
[DebuggerStepThrough]
public static class RecoverOnFailureExtensions
{
    /// <summary>
    /// Recovers from a failed result by calling the given function.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to recover if it's a failure.</param>
    /// <param name="func">The function to call for recovery.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<T> RecoverOnFailure<T>(this Result<T> result, Func<Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = func();
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Recovers from a failed result by calling the given function with the error.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to recover if it's a failure.</param>
    /// <param name="func">The function that receives the error and returns a recovery result.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<T> RecoverOnFailure<T>(this Result<T> result, Func<Error, Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = func(result.Error);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Recovers from a failed result by calling the given function if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="func">The function to call for recovery if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<T> RecoverOnFailure<T>(this Result<T> result, Func<Error, bool> predicate, Func<Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Error))
        {
            var output = func();
            output.LogActivityStatus();
            return output;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Recovers from a failed result by calling the given function with the error if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="func">The function that receives the error and returns a recovery result if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static Result<T> RecoverOnFailure<T>(this Result<T> result, Func<Error, bool> predicate, Func<Error, Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Error))
        {
            var output = func(result.Error);
            output.LogActivityStatus();
            return output;
        }

        result.LogActivityStatus();
        return result;
    }
}

/// <summary>
/// Provides asynchronous extension methods for recovering from failed results.
/// </summary>
/// <remarks>
/// <para>
/// This operation runs on the <b>failure track</b> - it only executes when the Result has failed.
/// If the Result is successful, the recovery function is not called.
/// </para>
/// <para>
/// Users should capture CancellationToken in their lambda closures when cancellation support is needed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var ct = cancellationToken;
/// var result = await GetUserAsync(id)
///     .RecoverOnFailureAsync(error => GetFromCacheAsync(id, ct));
/// </code>
/// </example>
[DebuggerStepThrough]
public static class RecoverOnFailureExtensionsAsync
{
    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given function.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to recover if it's a failure.</param>
    /// <param name="func">The function to call for recovery.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.RecoverOnFailure(func);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to recover if it's a failure.</param>
    /// <param name="funcAsync">The async function to call for recovery.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Result<T> result, Func<Task<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(funcAsync);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await funcAsync().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to recover if it's a failure.</param>
    /// <param name="funcAsync">The async function to call for recovery.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Task<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(funcAsync);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return await result.RecoverOnFailureAsync(funcAsync).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given function with the error.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to recover if it's a failure.</param>
    /// <param name="func">The function that receives the error and returns a recovery result.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Error, Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.RecoverOnFailure(func);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function with the error.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to recover if it's a failure.</param>
    /// <param name="funcAsync">The async function that receives the error.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Result<T> result, Func<Error, Task<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(funcAsync);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await funcAsync(result.Error).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function with the error.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to recover if it's a failure.</param>
    /// <param name="funcAsync">The async function that receives the error.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Error, Task<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(funcAsync);

        Result<T> result = await resultTask.ConfigureAwait(false);

        return await result.RecoverOnFailureAsync(funcAsync).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given function if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="func">The function to call for recovery if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Error, bool> predicate, Func<Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.RecoverOnFailure(predicate, func);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given function with the error if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="func">The function that receives the error and returns a recovery result if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Error, bool> predicate, Func<Error, Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.RecoverOnFailure(predicate, func);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="funcAsync">The async function to call for recovery if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Result<T> result, Func<Error, bool> predicate, Func<Task<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(funcAsync);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Error))
        {
            var output = await funcAsync().ConfigureAwait(false);
            output.LogActivityStatus();
            return output;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function with the error if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="funcAsync">The async function that receives the error if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Result<T> result, Func<Error, bool> predicate, Func<Error, Task<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(funcAsync);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Error))
        {
            var output = await funcAsync(result.Error).ConfigureAwait(false);
            output.LogActivityStatus();
            return output;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="funcAsync">The async function to call for recovery if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Error, bool> predicate, Func<Task<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(funcAsync);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return await result.RecoverOnFailureAsync(predicate, funcAsync).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function with the error if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="funcAsync">The async function that receives the error if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Error, bool> predicate, Func<Error, Task<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(funcAsync);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return await result.RecoverOnFailureAsync(predicate, funcAsync).ConfigureAwait(false);
    }

    // ValueTask overloads

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given function.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to recover if it's a failure.</param>
    /// <param name="func">The function to call for recovery.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<T>> RecoverOnFailureAsync<T>(this ValueTask<Result<T>> resultTask, Func<Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.RecoverOnFailure(func);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to recover if it's a failure.</param>
    /// <param name="funcAsync">The async function to call for recovery.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<T>> RecoverOnFailureAsync<T>(this ValueTask<Result<T>> resultTask, Func<ValueTask<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(funcAsync);

        Result<T> result = await resultTask.ConfigureAwait(false);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await funcAsync().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given function with the error.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to recover if it's a failure.</param>
    /// <param name="func">The function that receives the error and returns a recovery result.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<T>> RecoverOnFailureAsync<T>(this ValueTask<Result<T>> resultTask, Func<Error, Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.RecoverOnFailure(func);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function with the error.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to recover if it's a failure.</param>
    /// <param name="funcAsync">The async function that receives the error.</param>
    /// <returns>The original result if success; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<T>> RecoverOnFailureAsync<T>(this ValueTask<Result<T>> resultTask, Func<Error, ValueTask<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(funcAsync);

        Result<T> result = await resultTask.ConfigureAwait(false);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await funcAsync(result.Error).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given function if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="func">The function to call for recovery if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<T>> RecoverOnFailureAsync<T>(this ValueTask<Result<T>> resultTask, Func<Error, bool> predicate, Func<Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.RecoverOnFailure(predicate, func);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given function with the error if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="func">The function that receives the error and returns a recovery result if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<T>> RecoverOnFailureAsync<T>(this ValueTask<Result<T>> resultTask, Func<Error, bool> predicate, Func<Error, Result<T>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.RecoverOnFailure(predicate, func);
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="funcAsync">The async function to call for recovery if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<T>> RecoverOnFailureAsync<T>(this ValueTask<Result<T>> resultTask, Func<Error, bool> predicate, Func<ValueTask<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(funcAsync);

        Result<T> result = await resultTask.ConfigureAwait(false);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Error))
        {
            var output = await funcAsync().ConfigureAwait(false);
            output.LogActivityStatus();
            return output;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously Recovers from a failed result by calling the given async function with the error if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to recover if it's a failure.</param>
    /// <param name="predicate">The predicate to test the error.</param>
    /// <param name="funcAsync">The async function that receives the error if the predicate is true.</param>
    /// <returns>The original result if success or predicate is false; otherwise the result from the recovery function.</returns>
    [RailwayTrack(TrackBehavior.Failure)]
    public static async ValueTask<Result<T>> RecoverOnFailureAsync<T>(this ValueTask<Result<T>> resultTask, Func<Error, bool> predicate, Func<Error, ValueTask<Result<T>>> funcAsync)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(funcAsync);

        Result<T> result = await resultTask.ConfigureAwait(false);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(RecoverOnFailureExtensions.RecoverOnFailure));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Error))
        {
            var output = await funcAsync(result.Error).ConfigureAwait(false);
            output.LogActivityStatus();
            return output;
        }

        result.LogActivityStatus();
        return result;
    }
}