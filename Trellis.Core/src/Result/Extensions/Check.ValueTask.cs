namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async Check extensions where BOTH input and check function are async (ValueTask).
/// </summary>
/// <remarks>
/// Users should capture CancellationToken in their lambda closures when cancellation support is needed.
/// </remarks>
[DebuggerStepThrough]
public static partial class CheckExtensionsAsync
{
    /// <summary>
    /// Asynchronously runs a validation function on the success value, discarding the check result's value
    /// on success and preserving the original value. If the check fails, its failure is returned.
    /// Both the input and the check function are async (ValueTask).
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="resultTask">The async result to check.</param>
    /// <param name="func">The async validation function that returns a Result.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static async ValueTask<Result<T>> CheckAsync<T, TK>(this ValueTask<Result<T>> resultTask, Func<T, ValueTask<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckExtensions.Check));
        Result<T> result = await resultTask.ConfigureAwait(false);

        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = await func(value).ConfigureAwait(false);
        if (checkResult.IsFailure)
        {
            var failure = Result.Fail<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously runs a validation function that returns a non-generic <see cref="Result"/> on the success value,
    /// preserving the original value on success. Both the input and the check function are async (ValueTask).
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <param name="resultTask">The async result to check.</param>
    /// <param name="func">The async validation function that returns a non-generic Result.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static async ValueTask<Result<T>> CheckAsync<T>(this ValueTask<Result<T>> resultTask, Func<T, ValueTask<Result<Unit>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckExtensions.Check));
        Result<T> result = await resultTask.ConfigureAwait(false);

        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = await func(value).ConfigureAwait(false);
        if (checkResult.IsFailure)
        {
            var failure = Result.Fail<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }
}