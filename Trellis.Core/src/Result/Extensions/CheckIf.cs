namespace Trellis;

using System;
using System.Diagnostics;

/// <summary>
/// Provides conditional check extension methods for Result values.
/// The check function is only invoked when the condition or predicate is true;
/// otherwise the original result passes through unchanged.
/// </summary>
/// <remarks>
/// CheckIf combines the conditional behavior of <see cref="MapIfExtensions"/> with the
/// validation semantics of <see cref="CheckExtensions"/>. Use it when a validation step
/// should only run under certain circumstances.
/// </remarks>
[DebuggerStepThrough]
public static class CheckIfExtensions
{
    /// <summary>
    /// Conditionally runs a validation function when the boolean condition is true.
    /// If the result is a failure, or the condition is false, returns the original result unchanged.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="condition">The condition that must be true for the check to run.</param>
    /// <param name="func">The validation function that returns a Result.</param>
    /// <returns>The original result if the condition is false or the check passes; otherwise the check's failure.</returns>
    public static Result<T> CheckIf<T, TK>(this Result<T> result, bool condition, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();

        if (!result.TryGetValue(out var value) || !condition)
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = func(value);
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
    /// Conditionally runs a validation function when the boolean condition is true.
    /// Convenience overload for check functions returning <see cref="Result{Unit}"/>.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="condition">The condition that must be true for the check to run.</param>
    /// <param name="func">The validation function that returns a Result of Unit.</param>
    /// <returns>The original result if the condition is false or the check passes; otherwise the check's failure.</returns>
    public static Result<T> CheckIf<T>(this Result<T> result, bool condition, Func<T, Result> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();

        if (!result.TryGetValue(out var value) || !condition)
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = func(value);
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
    /// Conditionally runs a validation function when the predicate returns true for the success value.
    /// If the result is a failure, or the predicate returns false, returns the original result unchanged.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="predicate">The predicate to evaluate against the success value.</param>
    /// <param name="func">The validation function that returns a Result.</param>
    /// <returns>The original result if the predicate is false or the check passes; otherwise the check's failure.</returns>
    public static Result<T> CheckIf<T, TK>(this Result<T> result, Func<T, bool> predicate, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();

        if (!result.TryGetValue(out var value) || !predicate(value))
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = func(value);
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
    /// Conditionally runs a validation function when the predicate returns true for the success value.
    /// Convenience overload for check functions returning <see cref="Result{Unit}"/>.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="predicate">The predicate to evaluate against the success value.</param>
    /// <param name="func">The validation function that returns a Result of Unit.</param>
    /// <returns>The original result if the predicate is false or the check passes; otherwise the check's failure.</returns>
    public static Result<T> CheckIf<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Result> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();

        if (!result.TryGetValue(out var value) || !predicate(value))
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = func(value);
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