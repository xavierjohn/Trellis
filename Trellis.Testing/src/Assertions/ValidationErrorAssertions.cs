namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>
/// Extension methods to enable FluentAssertions on <see cref="Error.InvalidInput"/>.
/// </summary>
public static class ValidationErrorAssertionsExtensions
{
    /// <summary>
    /// Returns an assertions object for fluent assertions on <see cref="Error.InvalidInput"/>.
    /// </summary>
    public static ValidationErrorAssertions Should(this Error.InvalidInput error) => new(error);
}

/// <summary>
/// Contains assertion methods for <see cref="Error.InvalidInput"/>.
/// </summary>
public class ValidationErrorAssertions : ReferenceTypeAssertions<Error.InvalidInput, ValidationErrorAssertions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationErrorAssertions"/> class.
    /// </summary>
    public ValidationErrorAssertions(Error.InvalidInput error)
        : base(error)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "validationError";

    private bool RequireNonNullSubject(string because, object[] becauseArgs) =>
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject is not null)
            .FailWith("Expected {context:validationError} to not be <null>{reason}.");

    /// <summary>Asserts that exactly one violation matches the full pointer and reason code.</summary>
    /// <param name="field">The expected pointer, including its input location.</param>
    /// <param name="reasonCode">The expected machine-readable reason code.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>The original matching violation for further payload assertions.</returns>
    public AndWhichConstraint<ValidationErrorAssertions, FieldViolation> HaveFieldViolation(
        InputPointer field, string reasonCode, string because = "", params object[] becauseArgs)
    {
        if (!RequireNonNullSubject(because, becauseArgs))
            return new(this, default(FieldViolation)!);

        var matches = Subject.Fields.Items.Where(violation => violation.Field == field && violation.ReasonCode == reasonCode).ToArray();
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(matches.Length == 1)
            .FailWith("Expected {context:validationError} to contain exactly one field violation at {0} with reason code {1}{reason}, but found {2}. Violations: {3}",
                field, reasonCode, matches.Length, Subject.Fields.Items);

        return new(this, matches.Length == 1 ? matches[0] : default!);
    }

    /// <summary>Asserts that exactly one rule violation matches the reason code.</summary>
    /// <param name="reasonCode">The expected machine-readable reason code.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>The original matching violation for further payload assertions.</returns>
    public AndWhichConstraint<ValidationErrorAssertions, RuleViolation> HaveRuleViolation(
        string reasonCode, string because = "", params object[] becauseArgs)
    {
        if (!RequireNonNullSubject(because, becauseArgs))
            return new(this, default(RuleViolation)!);

        var matches = Subject.Rules.Items.Where(violation => violation.ReasonCode == reasonCode).ToArray();
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(matches.Length == 1)
            .FailWith("Expected {context:validationError} to contain exactly one rule violation with reason code {0}{reason}, but found {1}. Violations: {2}",
                reasonCode, matches.Length, Subject.Rules.Items);

        return new(this, matches.Length == 1 ? matches[0] : default!);
    }

    /// <summary>Asserts the number of field violations, including duplicate paths.</summary>
    /// <param name="expectedCount">The expected number of violations.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    public AndConstraint<ValidationErrorAssertions> HaveFieldViolationCount(
        int expectedCount, string because = "", params object[] becauseArgs)
    {
        if (RequireNonNullSubject(because, becauseArgs))
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.Fields.Items.Length == expectedCount)
                .FailWith("Expected {context:validationError} to contain {0} field violations{reason}, but found {1}. Violations: {2}",
                    expectedCount, Subject.Fields.Items.Length, Subject.Fields.Items);
        return new(this);
    }

    /// <summary>Asserts the number of rule violations, including duplicates.</summary>
    /// <param name="expectedCount">The expected number of violations.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    public AndConstraint<ValidationErrorAssertions> HaveRuleViolationCount(
        int expectedCount, string because = "", params object[] becauseArgs)
    {
        if (RequireNonNullSubject(because, becauseArgs))
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.Rules.Items.Length == expectedCount)
                .FailWith("Expected {context:validationError} to contain {0} rule violations{reason}, but found {1}. Violations: {2}",
                    expectedCount, Subject.Rules.Items.Length, Subject.Rules.Items);
        return new(this);
    }

    /// <summary>Asserts an unordered multiset of field violations using domain value equality.</summary>
    /// <param name="expected">The exact payloads, including typed arguments, detail, location and duplicate multiplicity.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    public AndConstraint<ValidationErrorAssertions> HaveOnlyFieldViolations(
        IEnumerable<FieldViolation> expected, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (RequireNonNullSubject(because, becauseArgs))
            Subject.Fields.Items.Should().BeEquivalentTo(expected,
                options => options.WithoutStrictOrdering().ComparingByValue<FieldViolation>(), because, becauseArgs);
        return new(this);
    }

    /// <summary>Asserts an unordered multiset of rule violations using domain value equality.</summary>
    /// <param name="expected">The exact payloads; related field order and duplicate multiplicity remain significant.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    public AndConstraint<ValidationErrorAssertions> HaveOnlyRuleViolations(
        IEnumerable<RuleViolation> expected, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (RequireNonNullSubject(because, becauseArgs))
            Subject.Rules.Items.Should().BeEquivalentTo(expected,
                options => options.WithoutStrictOrdering().ComparingByValue<RuleViolation>(), because, becauseArgs);
        return new(this);
    }

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