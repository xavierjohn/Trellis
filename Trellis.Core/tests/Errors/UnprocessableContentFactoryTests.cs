namespace Trellis.Core.Tests.Errors;

using Trellis.Testing;

/// <summary>
/// Tests for the <c>Error.InvalidInput.ForField</c> and
/// <c>Error.InvalidInput.ForRule</c> static factories.
/// These exist to remove the verbose boilerplate of constructing single-violation 422 errors,
/// which are by far the most common shape (every primitive <c>TryCreate</c>, every value-object
/// invariant, every <c>RequiredEnum</c> failure produces one).
/// </summary>
public class UnprocessableContentFactoryTests
{
    // ── ForField with a property name ──────────────────────────────────────

    [Fact]
    public void ForField_with_property_name_creates_single_field_violation()
    {
        var error = Error.InvalidInput.ForField(field: "email", code: "invalid_format");

        error.Fields.Length.Should().Be(1);
        error.Fields[0].Field.Should().Be(InputPointer.ForProperty("email"));
        error.Fields[0].ReasonCode.Should().Be("invalid_format");
        error.Fields[0].Detail.Should().BeNull();
    }

    [Fact]
    public void ForField_with_property_name_and_detail_propagates_detail()
    {
        var error = Error.InvalidInput.ForField(field: "email", code: "invalid_format", detail: "must contain @");

        error.Fields.Length.Should().Be(1);
        error.Fields[0].Detail.Should().Be("must contain @");
    }

    [Fact]
    public void ForField_with_property_name_produces_empty_rules()
    {
        var error = Error.InvalidInput.ForField(field: "name", code: "required");

        error.Rules.Length.Should().Be(0);
    }

    [Fact]
    public void ForField_escapes_property_name_via_InputPointer_ForProperty()
    {
        var error = Error.InvalidInput.ForField(field: "a/b", code: "invalid");

        // ForProperty escapes '/' as "~1" per RFC 6901
        error.Fields[0].Field.Path.Should().Be("/a~1b");
    }

    [Fact]
    public void ForField_with_null_or_empty_property_falls_back_to_root_pointer()
    {
        var error = Error.InvalidInput.ForField(field: string.Empty, code: "object_invalid");

        error.Fields[0].Field.Should().Be(InputPointer.Root);
    }

    // ── ForField with an input pointer ─────────────────────────────────────

    [Fact]
    public void ForField_with_pointer_uses_pointer_directly()
    {
        var pointer = new InputPointer("/items/0/quantity");
        var error = Error.InvalidInput.ForField(field: pointer, code: "out_of_range");

        error.Fields.Length.Should().Be(1);
        error.Fields[0].Field.Should().Be(pointer);
        error.Fields[0].ReasonCode.Should().Be("out_of_range");
        error.Fields[0].Detail.Should().BeNull();
    }

    [Fact]
    public void ForField_with_pointer_and_detail_propagates_detail()
    {
        var pointer = new InputPointer("/items/0/quantity");
        var error = Error.InvalidInput.ForField(field: pointer, code: "out_of_range", detail: "must be positive");

        error.Fields[0].Detail.Should().Be("must be positive");
    }

    [Fact]
    public void ForField_with_root_pointer_produces_object_level_violation()
    {
        var error = Error.InvalidInput.ForField(field: InputPointer.Root, code: "object_required");

        error.Fields[0].Field.Should().Be(InputPointer.Root);
    }

    // ── ForRule ────────────────────────────────────────────────────────────

    [Fact]
    public void ForRule_creates_single_rule_violation_with_empty_fields()
    {
        var error = Error.InvalidInput.ForRule(code: "passwords_must_match");

        error.Fields.Length.Should().Be(0);
        error.Rules.Length.Should().Be(1);
        error.Rules[0].ReasonCode.Should().Be("passwords_must_match");
        error.Rules[0].Detail.Should().BeNull();
    }

    [Fact]
    public void ForRule_with_detail_propagates_detail()
    {
        var error = Error.InvalidInput.ForRule(code: "passwords_must_match", detail: "Passwords do not match");

        error.Rules[0].Detail.Should().Be("Passwords do not match");
    }

    [Fact]
    public void ForRule_RelatedFields_CopiesInOrderAndPreservesMetadata()
    {
        var start = InputPointer.ForBody("start");
        var end = InputPointer.ForBody("end");
        var fields = new List<InputPointer> { start, end };
        var args = ValidationArgs.Of("comparisonProperty", "start");

        var error = Error.InvalidInput.ForRule(
            code: "period.end-after-start", fields: fields, args: args, detail: "End must follow start.");
        fields.Clear();

        error.Code.Should().Be(ValidationCodes.Unspecified);
        error.Fields.Items.Should().BeEmpty();
        var rule = error.Rules.Items.Should().ContainSingle().Which;
        rule.Fields.Items.Should().Equal([start, end]);
        rule.Args.Should().BeSameAs(args);
        rule.ReasonCode.Should().Be("period.end-after-start");
        rule.Detail.Should().Be("End must follow start.");
        error.Detail.Should().Be(rule.Detail);
    }

    [Fact]
    public void ForField_CodeFirst_PreservesLocationArgumentsAndEscaping()
    {
        var args = ValidationArgs.Of("comparisonValue", 0);
        var located = Error.InvalidInput.ForField(
            code: ValidationCodes.ValueGreaterThan, field: InputPointer.ForQuery("a/b"), args: args, detail: "Positive only.");
        var named = Error.InvalidInput.ForField(ValidationCodes.ValueGreaterThan, "a/b", args, "Positive only.");

        var field = located.Fields.Items.Should().ContainSingle().Which;
        field.Field.In.Should().Be(InputLocation.Query);
        field.Field.Path.Should().Be("/a~1b");
        field.ReasonCode.Should().Be(ValidationCodes.ValueGreaterThan);
        field.Args.Should().BeSameAs(args);
        field.Detail.Should().Be("Positive only.");
        named.Fields[0].Field.Path.Should().Be(field.Field.Path);
        named.Fields[0].Field.In.Should().Be(InputLocation.Unspecified);
    }

    // ── Equality + Kind preserved ────────────────────────────────────────────

    [Fact]
    public void ForField_results_equal_manual_construction()
    {
        var fromFactory = Error.InvalidInput.ForField(field: "email", code: "invalid_format", detail: "must contain @");
        var manual = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"), "invalid_format", Detail: "must contain @")));

        fromFactory.Equals(manual).Should().BeTrue();
        fromFactory.GetHashCode().Should().Be(manual.GetHashCode());
    }

    [Fact]
    public void ForRule_results_equal_manual_construction()
    {
        var fromFactory = Error.InvalidInput.ForRule(code: "cancel_after_ship", detail: "Cannot cancel after shipment");
        var manual = new Error.InvalidInput(
            EquatableArray<FieldViolation>.Empty,
            EquatableArray.Create(new RuleViolation("cancel_after_ship", Detail: "Cannot cancel after shipment")))
        { Detail = "Cannot cancel after shipment" };

        fromFactory.Equals(manual).Should().BeTrue();
        fromFactory.GetHashCode().Should().Be(manual.GetHashCode());
    }

    [Fact]
    public void Factory_results_have_correct_Kind()
    {
        Error.InvalidInput.ForField(field: "x", code: "y").Kind.Should().Be("invalid-input");
        Error.InvalidInput.ForRule(code: "x").Kind.Should().Be("invalid-input");
    }

    // ── Pluggability into Result ─────────────────────────────────────────────

    [Fact]
    public void ForField_can_be_used_as_failure_payload()
    {
        Result<int> result = Result.Fail<int>(Error.InvalidInput.ForField(field: "age", code: "out_of_range", detail: "must be >= 18"));

        result.IsFailure.Should().BeTrue();
        var err = result.UnwrapError();
        err.Should().BeOfType<Error.InvalidInput>();
        ((Error.InvalidInput)err).Fields[0].Detail.Should().Be("must be >= 18");
    }
}