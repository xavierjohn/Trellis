namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Represents an aggregate of multiple errors that occurred together.
/// Use this when multiple independent errors need to be returned as a single failure result.
/// </summary>
/// <remarks>
/// <para>
/// This is useful for scenarios where multiple operations fail independently,
/// such as batch processing or parallel validation of multiple entities.
/// </para>
/// <para>
/// The aggregated errors can be of different types and are accessible via the <see cref="Errors"/> property.
/// This type is immutable - the errors collection cannot be modified after creation.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var errors = new List&lt;Error&gt;
/// {
///     Error.Validation("Invalid email", "email"),
///     Error.NotFound("User not found", userId),
///     Error.Domain("Insufficient balance")
/// };
/// var aggregateError = new AggregateError(errors);
/// </code>
/// </example>
[DebuggerDisplay("Aggregate: {Errors.Count} error(s)")]
[DebuggerTypeProxy(typeof(AggregateErrorDebugView))]
public sealed class AggregateError : Error
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateError"/> class with the specified errors and custom error code.
    /// </summary>
    /// <param name="errors">The collection of errors to aggregate. Must contain at least one error.</param>
    /// <param name="code">The custom error code for this aggregate error.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public AggregateError(IReadOnlyList<Error> errors, string code) : base("Aggregated error", code)
    {
        if (errors.Count < 1)
            throw new ArgumentException("At least one error is required", nameof(errors));

        Errors = Array.AsReadOnly([.. errors]);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateError"/> class with the specified errors.
    /// Uses the default error code "aggregate.error".
    /// </summary>
    /// <param name="errors">The collection of errors to aggregate. Must contain at least one error.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public AggregateError(IReadOnlyList<Error> errors) : this(errors, "aggregate.error")
    {
    }

    /// <summary>
    /// Gets the collection of aggregated errors.
    /// </summary>
    /// <value>A read-only list of <see cref="Error"/> instances that were aggregated together.</value>
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>
    /// Extracts and merges all <see cref="ValidationError"/> field errors from the aggregated errors.
    /// </summary>
    /// <returns>A merged <see cref="ValidationError"/> containing all field errors, or null if no validation errors exist.</returns>
    /// <remarks>
    /// <para><strong>Warning:</strong> Non-validation errors are silently discarded.
    /// For full control over all error types, use the <c>onAggregate</c> parameter on <c>MatchError</c> instead.</para>
    /// </remarks>
    public ValidationError? FlattenValidationErrors()
    {
        ValidationError? merged = null;

        foreach (var error in Errors)
        {
            var validation = error switch
            {
                ValidationError ve => ve,
                AggregateError nested => nested.FlattenValidationErrors(),
                _ => null
            };

            if (validation is null)
                continue;

            merged = merged is null ? validation : merged.Merge(validation);
        }

        return merged;
    }
}