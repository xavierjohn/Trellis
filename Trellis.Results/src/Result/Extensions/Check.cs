namespace Trellis;

using System;
using System.Diagnostics;

/// <summary>
/// Provides extension methods for running validation functions on Result values while preserving
/// the original value on success. If the check fails, its failure is returned.
/// </summary>
/// <remarks>
/// Check is semantically equivalent to <c>result.Bind(v =&gt; func(v).Map(_ =&gt; v))</c> but is
/// implemented directly for efficiency. This is useful when you want to validate a value using
/// a function that returns a Result, but you want to keep the original value flowing through
/// the pipeline rather than the check function's return value.
/// </remarks>
[DebuggerStepThrough]
public static class CheckExtensions
{
    /// <summary>
    /// Runs a validation function on the success value, discarding the check result's value on success
    /// and preserving the original value. If the check fails, its failure is returned.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="func">The validation function that returns a Result.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static Result<T> Check<T, TK>(this Result<T> result, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        using var activity = RopTrace.ActivitySource.StartActivity();

        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        var checkResult = func(result.Value);
        if (checkResult.IsFailure)
        {
            var failure = Result.Failure<T>(checkResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Runs a validation function that returns <see cref="Result{Unit}"/> on the success value,
    /// preserving the original value on success. Common for void validations.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <param name="result">The result to check.</param>
    /// <param name="func">The validation function that returns a Result of Unit.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static Result<T> Check<T>(this Result<T> result, Func<T, Result<Unit>> func)
        => Check<T, Unit>(result, func);
}