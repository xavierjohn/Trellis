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
    /// <remarks>
    /// <para><strong>Warning:</strong> Non-validation errors within an <see cref="AggregateError"/> are silently
    /// discarded. If the aggregate contains a mix of <see cref="ValidationError"/> and other error types
    /// (e.g., <see cref="DomainError"/>), only the validation errors are returned.</para>
    /// <para>For full control over all error types in an aggregate, use the <c>onAggregate</c> parameter
    /// on <see cref="MatchErrorExtensions.MatchError{TValue,TOut}"/> instead.</para>
    /// </remarks>
    public static ValidationError? FlattenValidationErrors<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return null;

        return result.Error is AggregateError aggregate ? aggregate.FlattenValidationErrors() :
               result.Error as ValidationError;
    }
}
