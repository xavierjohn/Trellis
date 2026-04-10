namespace Trellis;

/// <summary>
/// Async CheckIf extensions where only the RIGHT (check function) is async (ValueTask), input is sync.
/// </summary>
public static partial class CheckIfExtensionsAsync
{
    /// <summary>
    /// Conditionally runs an async validation function when the boolean condition is true.
    /// Only the check function is async (ValueTask); the input is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="condition">The condition that must be true for the check to run.</param>
    /// <param name="func">The async validation function that returns a Result.</param>
    /// <returns>The original result if the condition is false or the check passes; otherwise the check's failure.</returns>
    public static async ValueTask<Result<T>> CheckIfAsync<T, TK>(this Result<T> result, bool condition, Func<T, ValueTask<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckIfExtensions.CheckIf));

        if (result.IsFailure || !condition)
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = await func(result.Value).ConfigureAwait(false);
        if (checkResult.IsFailure)
        {
            var failure = Result.Failure<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Result{T}, bool, Func{T, ValueTask{Result{TK}}})"/>
    public static ValueTask<Result<T>> CheckIfAsync<T>(this Result<T> result, bool condition, Func<T, ValueTask<Result<Unit>>> func)
        => CheckIfAsync<T, Unit>(result, condition, func);

    /// <summary>
    /// Conditionally runs an async validation function when the predicate returns true.
    /// Only the check function is async (ValueTask); the input is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="predicate">The predicate to evaluate against the success value.</param>
    /// <param name="func">The async validation function that returns a Result.</param>
    /// <returns>The original result if the predicate returns false or the check passes; otherwise the check's failure.</returns>
    public static async ValueTask<Result<T>> CheckIfAsync<T, TK>(this Result<T> result, Func<T, bool> predicate, Func<T, ValueTask<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckIfExtensions.CheckIf));

        if (result.IsFailure || !predicate(result.Value))
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = await func(result.Value).ConfigureAwait(false);
        if (checkResult.IsFailure)
        {
            var failure = Result.Failure<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Result{T}, Func{T, bool}, Func{T, ValueTask{Result{TK}}})"/>
    public static ValueTask<Result<T>> CheckIfAsync<T>(this Result<T> result, Func<T, bool> predicate, Func<T, ValueTask<Result<Unit>>> func)
        => CheckIfAsync<T, Unit>(result, predicate, func);
}