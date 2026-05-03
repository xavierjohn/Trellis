namespace Trellis.Asp.Tests;

using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Regression tests for m-8: ErrorCollector must preserve the full
/// <see cref="FieldViolation"/> shape (ReasonCode, Args, Detail) and the
/// top-level <see cref="Error.UnprocessableContent.Rules"/> entries when
/// merging via <see cref="ValidationErrorsContext.AddError(Error.UnprocessableContent)"/>.
/// Previously the collector flattened violations into (field, detail) strings,
/// dropping ReasonCode/Args and discarding Rules entirely.
/// </summary>
public class ValidationErrorsContextPreservationTests
{
    [Fact]
    public void AddError_UnprocessableContent_PreservesFieldReasonCodeAndArgs()
    {
        using (ValidationErrorsContext.BeginScope())
        {
            var args = ImmutableDictionary<string, string>.Empty.Add("min", "3").Add("max", "50");
            var source = new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("name"), "length_out_of_range", args, "Name length out of range.")));

            ValidationErrorsContext.AddError(source);

            var aggregated = ValidationErrorsContext.GetUnprocessableContent();
            aggregated.Should().NotBeNull();
            var violation = aggregated!.Fields.Items.Single();
            violation.Field.Path.Should().Be("/name");
            violation.ReasonCode.Should().Be("length_out_of_range");
            violation.Detail.Should().Be("Name length out of range.");
            violation.Args.Should().NotBeNull();
            violation.Args!["min"].Should().Be("3");
            violation.Args!["max"].Should().Be("50");
        }
    }

    [Fact]
    public void AddError_UnprocessableContent_PreservesTopLevelRules()
    {
        using (ValidationErrorsContext.BeginScope())
        {
            var source = new Error.UnprocessableContent(
                EquatableArray<FieldViolation>.Empty,
                EquatableArray.Create(
                    new RuleViolation("passwords_must_match", Detail: "Password and confirmation must match."),
                    new RuleViolation("order_must_have_items")));

            ValidationErrorsContext.AddError(source);

            var aggregated = ValidationErrorsContext.GetUnprocessableContent();
            aggregated.Should().NotBeNull();
            aggregated!.Rules.Items.Length.Should().Be(2);
            aggregated.Rules.Items[0].ReasonCode.Should().Be("passwords_must_match");
            aggregated.Rules.Items[0].Detail.Should().Be("Password and confirmation must match.");
            aggregated.Rules.Items[1].ReasonCode.Should().Be("order_must_have_items");
        }
    }

    [Fact]
    public void AddError_UnprocessableContent_DedupesIdenticalViolations()
    {
        using (ValidationErrorsContext.BeginScope())
        {
            var source = new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("email"), "invalid_format", Detail: "Email is invalid.")));

            ValidationErrorsContext.AddError(source);
            ValidationErrorsContext.AddError(source);

            var aggregated = ValidationErrorsContext.GetUnprocessableContent();
            aggregated!.Fields.Items.Length.Should().Be(1);
        }
    }

    [Fact]
    public void AddError_UnprocessableContent_KeepsDistinctReasonsForSameField()
    {
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("password"), "too_short", Detail: "Password is too short."))));
            ValidationErrorsContext.AddError(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("password"), "missing_digit", Detail: "Password must contain a digit."))));

            var aggregated = ValidationErrorsContext.GetUnprocessableContent();
            aggregated!.Fields.Items.Length.Should().Be(2);
            aggregated.Fields.Items.Select(f => f.ReasonCode).Should().BeEquivalentTo("too_short", "missing_digit");
        }
    }

    [Fact]
    public void AddError_StringOverload_StillUsesValidationErrorReasonCode()
    {
        // The string-based AddError(field, message) overload is used by the JSON converter
        // path which has no semantic ReasonCode available; it should continue to default
        // to "validation.error" with the message stored as Detail.
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("email", "Email is required.");

            var aggregated = ValidationErrorsContext.GetUnprocessableContent();
            var violation = aggregated!.Fields.Items.Single();
            violation.ReasonCode.Should().Be("validation.error");
            violation.Detail.Should().Be("Email is required.");
        }
    }
}
