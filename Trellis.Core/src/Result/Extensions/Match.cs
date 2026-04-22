namespace Trellis;

using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Pattern matching helpers (Match / Switch) for Result.
/// </summary>
[DebuggerStepThrough]
public static class MatchExtensions
{
    /// <summary>
    /// Executes either the success or failure function based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="result">The result to match on.</param>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onFailure">Function to execute on failure.</param>
    /// <returns>The output from either the success or failure function.</returns>
    public static TOut Match<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.TryGetValue(out var value))
            return InvokeAndTrace(activity, () => onSuccess(value), ActivityStatusCode.Ok);

        return InvokeAndTrace(activity, () => onFailure(result.Error), ActivityStatusCode.Error);
    }

    /// <summary>
    /// Executes either the success or failure action based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <param name="result">The result to switch on.</param>
    /// <param name="onSuccess">Action to execute on success.</param>
    /// <param name="onFailure">Action to execute on failure.</param>
    public static void Switch<TIn>(this Result<TIn> result, Action<TIn> onSuccess, Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.TryGetValue(out var value))
            InvokeAndTrace(activity, () => onSuccess(value), ActivityStatusCode.Ok);
        else
            InvokeAndTrace(activity, () => onFailure(result.Error), ActivityStatusCode.Error);
    }

    private static TOut InvokeAndTrace<TOut>(Activity? activity, Func<TOut> handler, ActivityStatusCode successStatus)
    {
        try
        {
            var output = handler();
            activity?.SetStatus(successStatus);
            return output;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    private static void InvokeAndTrace(Activity? activity, Action handler, ActivityStatusCode successStatus)
    {
        try
        {
            handler();
            activity?.SetStatus(successStatus);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }
}

/// <summary>
/// Asynchronous pattern matching helpers for Result.
/// </summary>
[DebuggerStepThrough]
public static class MatchExtensionsAsync
{
    /// <summary>
    /// Executes either the success or failure function based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="resultTask">The task representing the result to match on.</param>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onFailure">Function to execute on failure.</param>
    /// <returns>The output from either the success or failure function.</returns>
    public static async Task<TOut> MatchAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        var result = await resultTask.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    /// <summary>
    /// Executes either the success or failure async function based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="result">The result to match on.</param>
    /// <param name="onSuccess">Async function to execute on success.</param>
    /// <param name="onFailure">Async function to execute on failure.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the output from either the success or failure function.</returns>
    public static async Task<TOut> MatchAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, CancellationToken, Task<TOut>> onSuccess, Func<Error, CancellationToken, Task<TOut>> onFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.TryGetValue(out var value))
            return await InvokeAndTraceAsync(activity, () => onSuccess(value, cancellationToken), ActivityStatusCode.Ok).ConfigureAwait(false);

        return await InvokeAndTraceAsync(activity, () => onFailure(result.Error, cancellationToken), ActivityStatusCode.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes either the success or failure async function based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="result">The result to match on.</param>
    /// <param name="onSuccess">Async function to execute on success.</param>
    /// <param name="onFailure">Async function to execute on failure.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the output from either the success or failure function.</returns>
    public static async Task<TOut> MatchAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.TryGetValue(out var value))
            return await InvokeAndTraceAsync(activity, () => onSuccess(value), ActivityStatusCode.Ok).ConfigureAwait(false);

        return await InvokeAndTraceAsync(activity, () => onFailure(result.Error), ActivityStatusCode.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes either the success or failure async function based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="resultTask">The task representing the result to match on.</param>
    /// <param name="onSuccess">Async function to execute on success.</param>
    /// <param name="onFailure">Async function to execute on failure.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the output from either the success or failure function.</returns>
    public static async Task<TOut> MatchAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, CancellationToken, Task<TOut>> onSuccess, Func<Error, CancellationToken, Task<TOut>> onFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        var result = await resultTask.ConfigureAwait(false);
        return await result.MatchAsync(onSuccess, onFailure, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes either the success or failure async function based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="resultTask">The task representing the result to match on.</param>
    /// <param name="onSuccess">Async function to execute on success.</param>
    /// <param name="onFailure">Async function to execute on failure.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the output from either the success or failure function.</returns>
    public static async Task<TOut> MatchAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        var result = await resultTask.ConfigureAwait(false);
        return await result.MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes either the success or failure async action based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <param name="resultTask">The task representing the result to switch on.</param>
    /// <param name="onSuccess">Async action to execute on success.</param>
    /// <param name="onFailure">Async action to execute on failure.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task SwitchAsync<TIn>(this Task<Result<TIn>> resultTask, Func<TIn, CancellationToken, Task> onSuccess, Func<Error, CancellationToken, Task> onFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var result = await resultTask.ConfigureAwait(false);
        if (result.TryGetValue(out var value))
            await InvokeAndTraceAsync(activity, () => onSuccess(value, cancellationToken), ActivityStatusCode.Ok).ConfigureAwait(false);
        else
            await InvokeAndTraceAsync(activity, () => onFailure(result.Error, cancellationToken), ActivityStatusCode.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes either the success or failure async action based on the result state.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <param name="resultTask">The task representing the result to switch on.</param>
    /// <param name="onSuccess">Async action to execute on success.</param>
    /// <param name="onFailure">Async action to execute on failure.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task SwitchAsync<TIn>(this Task<Result<TIn>> resultTask, Func<TIn, Task> onSuccess, Func<Error, Task> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var result = await resultTask.ConfigureAwait(false);
        if (result.TryGetValue(out var value))
            await InvokeAndTraceAsync(activity, () => onSuccess(value), ActivityStatusCode.Ok).ConfigureAwait(false);
        else
            await InvokeAndTraceAsync(activity, () => onFailure(result.Error), ActivityStatusCode.Error).ConfigureAwait(false);
    }

    #region ValueTask-based overloads

    /// <summary>
    /// Executes either the success or failure function based on the ValueTask result state. Left is async (ValueTask), handlers are sync.
    /// </summary>
    public static async ValueTask<TOut> MatchAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        var result = await resultTask.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    /// <summary>
    /// Executes either the success or failure async function based on the result state. Right is async (ValueTask).
    /// </summary>
    public static async ValueTask<TOut> MatchAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, ValueTask<TOut>> onSuccess, Func<Error, ValueTask<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        using var activity = RopTrace.ActivitySource.StartActivity();
        if (result.TryGetValue(out var value))
            return await InvokeAndTraceValueTaskAsync(activity, () => onSuccess(value), ActivityStatusCode.Ok).ConfigureAwait(false);

        return await InvokeAndTraceValueTaskAsync(activity, () => onFailure(result.Error), ActivityStatusCode.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes either the success or failure async function based on the ValueTask result state. Both sides are async (ValueTask).
    /// </summary>
    public static async ValueTask<TOut> MatchAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, ValueTask<TOut>> onSuccess, Func<Error, ValueTask<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        var result = await resultTask.ConfigureAwait(false);
        return await result.MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes either the success or failure async action based on the ValueTask result state. Both are async (ValueTask).
    /// </summary>
    public static async ValueTask SwitchAsync<TIn>(this ValueTask<Result<TIn>> resultTask, Func<TIn, ValueTask> onSuccess, Func<Error, ValueTask> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var result = await resultTask.ConfigureAwait(false);
        if (result.TryGetValue(out var value))
            await InvokeAndTraceValueTaskAsync(activity, () => onSuccess(value), ActivityStatusCode.Ok).ConfigureAwait(false);
        else
            await InvokeAndTraceValueTaskAsync(activity, () => onFailure(result.Error), ActivityStatusCode.Error).ConfigureAwait(false);
    }

    private static async Task<TOut> InvokeAndTraceAsync<TOut>(Activity? activity, Func<Task<TOut>> handler, ActivityStatusCode successStatus)
    {
        try
        {
            var output = await handler().ConfigureAwait(false);
            activity?.SetStatus(successStatus);
            return output;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    private static async Task InvokeAndTraceAsync(Activity? activity, Func<Task> handler, ActivityStatusCode successStatus)
    {
        try
        {
            await handler().ConfigureAwait(false);
            activity?.SetStatus(successStatus);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    private static async ValueTask<TOut> InvokeAndTraceValueTaskAsync<TOut>(Activity? activity, Func<ValueTask<TOut>> handler, ActivityStatusCode successStatus)
    {
        try
        {
            var output = await handler().ConfigureAwait(false);
            activity?.SetStatus(successStatus);
            return output;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    private static async ValueTask InvokeAndTraceValueTaskAsync(Activity? activity, Func<ValueTask> handler, ActivityStatusCode successStatus)
    {
        try
        {
            await handler().ConfigureAwait(false);
            activity?.SetStatus(successStatus);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    #endregion
}