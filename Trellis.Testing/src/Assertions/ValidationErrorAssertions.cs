namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>
/// Extension methods to enable FluentAssertions on ValidationError types.
/// </summary>
public static class ValidationErrorAssertionsExtensions
{
    /// <summary>
    /// Returns an assertions object for fluent assertions on ValidationError.
    /// </summary>
    public static ValidationErrorAssertions Should(this ValidationError error) => new ValidationErrorAssertions(error);
}

/// <summary>
/// Contains assertion methods for ValidationError types.
/// </summary>
public class ValidationErrorAssertions : ReferenceTypeAssertions<ValidationError, ValidationErrorAssertions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationErrorAssertions"/> class.
    /// </summary>
    public ValidationErrorAssertions(ValidationError error)
        : base(error)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "validationError";

    /// <summary>
    /// Asserts that the validation error contains an error for the specified field.
    /// </summary>
    /// <param name="fieldName">The field name that should have an error.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ValidationErrorAssertions> HaveFieldError(
        string fieldName,
        string because = "",
        params object[] becauseArgs)
    {
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject.FieldErrors.Any(fe => fe.FieldName == fieldName))
            .FailWith("Expected {context:validationError} to contain field {0}{reason}, but it did not. Fields: {1}",
                fieldName,
                string.Join(", ", Subject.FieldErrors.Select(fe => fe.FieldName)));

        return new AndConstraint<ValidationErrorAssertions>(this);
    }

    /// <summary>
    /// Asserts that the validation error contains a specific detail for the specified field.
    /// </summary>
    /// <param name="fieldName">The field name.</param>
    /// <param name="expectedDetail">The expected error detail for the field.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ValidationErrorAssertions> HaveFieldErrorWithDetail(
        string fieldName,
        string expectedDetail,
        string because = "",
        params object[] becauseArgs)
    {
        var fieldError = Subject.FieldErrors.FirstOrDefault(fe => fe.FieldName == fieldName);

        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(fieldError != default)
            .FailWith("Expected {context:validationError} to contain field {0}{reason}, but it did not.", fieldName)
            .Then
            .BecauseOf(because, becauseArgs)
            .ForCondition(fieldError.Details.Contains(expectedDetail))
            .FailWith("Expected field {0} to have detail '{1}'{reason}, but found: {2}",
                fieldName,
                expectedDetail,
                string.Join(", ", fieldError.Details));

        return new AndConstraint<ValidationErrorAssertions>(this);
    }

    /// <summary>
    /// Asserts that the validation error has the specified number of fields with errors.
    /// </summary>
    /// <param name="expectedCount">The expected number of fields with errors.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<ValidationErrorAssertions> HaveFieldCount(
        int expectedCount,
        string because = "",
        params object[] becauseArgs)
    {
        Subject.FieldErrors.Should().HaveCount(expectedCount, because, becauseArgs);
        return new AndConstraint<ValidationErrorAssertions>(this);
    }
}