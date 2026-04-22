namespace Trellis;

/// <summary>
/// Async Check extensions where BOTH input and check function are async (Task).
/// </summary>
public static partial class CheckExtensionsAsync
{
    /// <summary>
    /// Asynchronously runs a validation function on the success value, discarding the check result's value
    /// on success and preserving the original value. If the check fails, its failure is returned.
    /// Both the input and the check function are async.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="resultTask">The task containing the result to check.</param>
    /// <param name="func">The async validation function that returns a Result.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static async Task<Result<T>> CheckAsync<T, TK>(this Task<Result<T>> resultTask, Func<T, Task<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
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
    /// Asynchronously runs a validation function that returns <see cref="Result{Unit}"/> on the success value,
    /// preserving the original value on success. Both the input and the check function are async.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <param name="resultTask">The task containing the result to check.</param>
    /// <param name="func">The async validation function that returns a Result of Unit.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static async Task<Result<T>> CheckAsync<T>(this Task<Result<T>> resultTask, Func<T, Task<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
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