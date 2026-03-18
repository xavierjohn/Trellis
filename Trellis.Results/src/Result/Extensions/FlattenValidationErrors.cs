namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Extension methods for extracting <see cref="ValidationError"/> data from failed results.
/// </summary>
[DebuggerStepThrough]
public static class FlattenValidationErrorsExtensions
{
    /// <summary>
    /// Extracts and merges all <see cref="ValidationError"/> field errors from the result's error.
    /// If the error is an <see cref="AggregateError"/>, all nested validation errors are flattened and merged.
    /// If the error is a <see cref="ValidationError"/>, it is returned directly.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to extract validation errors from.</param>
    /// <returns>A merged <see cref="ValidationError"/> containing all field errors, or null if no validation errors exist.</returns>
    public static ValidationError? FlattenValidationErrors<T>(this Result<T> result)
        => result.Error is AggregateError aggregate ? aggregate.FlattenValidationErrors() :
           result.Error as ValidationError;
}
