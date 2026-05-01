namespace Trellis;

/// <summary>
/// Async Check extensions where only the RIGHT (check function) is async (Task), input is sync.
/// </summary>
public static partial class CheckExtensionsAsync
{
    /// <summary>
    /// Runs an async validation function on the sync success value, discarding the check result's value
    /// on success and preserving the original value. If the check fails, its failure is returned.
    /// Only the check function is async; the input is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="func">The async validation function that returns a Result.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static async Task<Result<T>> CheckAsync<T, TK>(this Result<T> result, Func<T, Task<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckExtensions.Check));

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
    /// Runs an async validation function that returns a <see cref="Result{TValue}"/> with <see cref="Unit"/> on the sync success value,
    /// preserving the original value on success. Only the check function is async; the input is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="func">The async validation function that returns a <see cref="Result{TValue}"/> with <see cref="Unit"/>.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static async Task<Result<T>> CheckAsync<T>(this Result<T> result, Func<T, Task<Result<Unit>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckExtensions.Check));

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