namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>
/// Extension methods to enable FluentAssertions on <see cref="Error.UnprocessableContent"/>.
/// </summary>
public static class ValidationErrorAssertionsExtensions
{
    /// <summary>
    /// Returns an assertions object for fluent assertions on <see cref="Error.UnprocessableContent"/>.
    /// </summary>
    public static ValidationErrorAssertions Should(this Error.UnprocessableContent error) => new(error);
}

/// <summary>
/// Contains assertion methods for <see cref="Error.UnprocessableContent"/>.
/// </summary>
public class ValidationErrorAssertions : ReferenceTypeAssertions<Error.UnprocessableContent, ValidationErrorAssertions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationErrorAssertions"/> class.
    /// </summary>
    public ValidationErrorAssertions(Error.UnprocessableContent error)
        : base(error)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "validationError";

    /// <summary>
    /// Asserts that the error contains a field violation for the specified field.
    /// </summary>
    /// <param name="fieldName">The field name (plain or JSON pointer form, e.g. "email" or "/email").</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders in <paramref name="because" />.</param>
    public AndConstraint<ValidationErrorAssertions> HaveFieldError(
        string fieldName,
        string because = "",
        params object[] becauseArgs)
    {
        var expectedPath = InputPointer.ForProperty(fieldName).Path;

        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject.Fields.Items.Any(fv => fv.Field.Path == expectedPath))
            .FailWith("Expected {context:validationError} to contain field {0}{reason}, but it did not. Fields: {1}",
                fieldName,
                string.Join(", ", Subject.Fields.Items.Select(fv => fv.Field.Path)));

        return new AndConstraint<ValidationErrorAssertions>(this);
    }

    /// <summary>
    /// Asserts that the error contains a field violation with the specified detail for the specified field.
    /// </summary>
    /// <param name="fieldName">The field name.</param>
    /// <param name="expectedDetail">The expected error detail for the field.</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders in <paramref name="because" />.</param>
    public AndConstraint<ValidationErrorAssertions> HaveFieldErrorWithDetail(
        string fieldName,
        string expectedDetail,
        string because = "",
        params object[] becauseArgs)
    {
        var expectedPath = InputPointer.ForProperty(fieldName).Path;
        var matching = Subject.Fields.Items.Where(fv => fv.Field.Path == expectedPath).ToArray();

        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(matching.Length > 0)
            .FailWith("Expected {context:validationError} to contain field {0}{reason}, but it did not.", fieldName)
            .Then
            .BecauseOf(because, becauseArgs)
            .ForCondition(matching.Any(fv => fv.Detail == expectedDetail))
            .FailWith("Expected field {0} to have detail '{1}'{reason}, but found: {2}",
                fieldName,
                expectedDetail,
                string.Join(", ", matching.Select(fv => fv.Detail ?? fv.ReasonCode)));

        return new AndConstraint<ValidationErrorAssertions>(this);
    }

    /// <summary>
    /// Asserts that the error has the specified number of distinct field paths with violations.
    /// </summary>
    /// <param name="expectedCount">The expected number of distinct fields with violations.</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders in <paramref name="because" />.</param>
    public AndConstraint<ValidationErrorAssertions> HaveFieldCount(
        int expectedCount,
        string because = "",
        params object[] becauseArgs)
    {
        Subject.Fields.Items.Select(fv => fv.Field.Path).Distinct().Should().HaveCount(expectedCount, because, becauseArgs);
        return new AndConstraint<ValidationErrorAssertions>(this);
    }
}