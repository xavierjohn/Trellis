namespace Trellis;

using System.Runtime.CompilerServices;

/// <summary>
/// Async CheckIf extensions where only the RIGHT (check function) is async (Task), input is sync.
/// </summary>
public static partial class CheckIfExtensionsAsync
{
    /// <summary>
    /// Conditionally runs an async validation function when the boolean condition is true.
    /// Only the check function is async; the input is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="condition">The condition that must be true for the check to run.</param>
    /// <param name="func">The async validation function that returns a Result.</param>
    /// <returns>The original result if the condition is false or the check passes; otherwise the check's failure.</returns>
    /// <remarks>
    /// <see cref="OverloadResolutionPriorityAttribute"/> resolves the historical CS0121 ambiguity
    /// against the sibling <see cref="ValueTask{T}"/>-delegate overload on the same sync
    /// <see cref="Result{T}"/> receiver for inline async lambdas.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<T>> CheckIfAsync<T, TK>(this Result<T> result, bool condition, Func<T, Task<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckIfExtensions.CheckIf));

        if (!result.TryGetValue(out var value) || !condition)
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = await func(value).ConfigureAwait(false);
        if (checkResult.IsFailure)
        {
            var failure = checkResult.ProjectFailure<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Result{T}, bool, Func{T, Task{Result{TK}}})"/>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<T>> CheckIfAsync<T>(this Result<T> result, bool condition, Func<T, Task<Result<Unit>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckIfExtensions.CheckIf));

        if (!result.TryGetValue(out var value) || !condition)
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = await func(value).ConfigureAwait(false);
        if (checkResult.IsFailure)
        {
            var failure = checkResult.ProjectFailure<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally runs an async validation function when the predicate returns true.
    /// Only the check function is async; the input is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="predicate">The predicate to evaluate against the success value.</param>
    /// <param name="func">The async validation function that returns a Result.</param>
    /// <returns>The original result if the predicate returns false or the check passes; otherwise the check's failure.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<T>> CheckIfAsync<T, TK>(this Result<T> result, Func<T, bool> predicate, Func<T, Task<Result<TK>>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckIfExtensions.CheckIf));

        if (!result.TryGetValue(out var value) || !predicate(value))
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = await func(value).ConfigureAwait(false);
        if (checkResult.IsFailure)
        {
            var failure = checkResult.ProjectFailure<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Result{T}, Func{T, bool}, Func{T, Task{Result{TK}}})"/>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<T>> CheckIfAsync<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Task<Result<Unit>>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CheckIfExtensions.CheckIf));

        if (!result.TryGetValue(out var value) || !predicate(value))
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = await func(value).ConfigureAwait(false);
        if (checkResult.IsFailure)
        {
            var failure = checkResult.ProjectFailure<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }
}