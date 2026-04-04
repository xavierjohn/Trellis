namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async CheckIf extensions where BOTH input and check function are async (Task).
/// </summary>
[DebuggerStepThrough]
public static partial class CheckIfExtensionsAsync
{
    /// <summary>
    /// Conditionally runs an async validation function when the boolean condition is true.
    /// Both the input and the check function are async.
    /// </summary>
    public static async Task<Result<T>> CheckIfAsync<T, TK>(this Task<Result<T>> resultTask, bool condition, Func<T, Task<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckIfExtensions.CheckIf));
        Result<T> result = await resultTask.ConfigureAwait(false);

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

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Task{Result{T}}, bool, Func{T, Task{Result{TK}}})"/>
    public static Task<Result<T>> CheckIfAsync<T>(this Task<Result<T>> resultTask, bool condition, Func<T, Task<Result<Unit>>> func)
        => CheckIfAsync<T, Unit>(resultTask, condition, func);

    /// <summary>
    /// Conditionally runs an async validation function when the predicate returns true.
    /// Both the input and the check function are async.
    /// </summary>
    public static async Task<Result<T>> CheckIfAsync<T, TK>(this Task<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Task<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckIfExtensions.CheckIf));
        Result<T> result = await resultTask.ConfigureAwait(false);

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

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Task{Result{T}}, Func{T, bool}, Func{T, Task{Result{TK}}})"/>
    public static Task<Result<T>> CheckIfAsync<T>(this Task<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Task<Result<Unit>>> func)
        => CheckIfAsync<T, Unit>(resultTask, predicate, func);
}
