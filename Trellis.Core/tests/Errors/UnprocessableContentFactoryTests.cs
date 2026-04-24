namespace Trellis.Core.Tests.Errors;

using Trellis.Testing;

/// <summary>
/// Tests for the <see cref="Error.UnprocessableContent.ForField(string, string, string?)"/>,
/// <see cref="Error.UnprocessableContent.ForField(InputPointer, string, string?)"/>, and
/// <see cref="Error.UnprocessableContent.ForRule(string, string?)"/> static factories.
/// These exist to remove the verbose boilerplate of constructing single-violation 422 errors,
/// which are by far the most common shape (every primitive <c>TryCreate</c>, every value-object
/// invariant, every <c>RequiredEnum</c> failure produces one).
/// </summary>
public class UnprocessableContentFactoryTests
{
    // ── ForField(string, string, string?) ───────────────────────────────────

    [Fact]
    public void ForField_with_property_name_creates_single_field_violation()
    {
        var error = Error.UnprocessableContent.ForField("email", "invalid_format");

        error.Fields.Length.Should().Be(1);
        error.Fields[0].Field.Should().Be(InputPointer.ForProperty("email"));
        error.Fields[0].ReasonCode.Should().Be("invalid_format");
        error.Fields[0].Detail.Should().BeNull();
    }

    [Fact]
    public void ForField_with_property_name_and_detail_propagates_detail()
    {
        var error = Error.UnprocessableContent.ForField("email", "invalid_format", "must contain @");

        error.Fields.Length.Should().Be(1);
        error.Fields[0].Detail.Should().Be("must contain @");
    }

    [Fact]
    public void ForField_with_property_name_produces_empty_rules()
    {
        var error = Error.UnprocessableContent.ForField("name", "required");

        error.Rules.Length.Should().Be(0);
    }

    [Fact]
    public void ForField_escapes_property_name_via_InputPointer_ForProperty()
    {
        var error = Error.UnprocessableContent.ForField("a/b", "invalid");

        // ForProperty escapes '/' as "~1" per RFC 6901
        error.Fields[0].Field.Path.Should().Be("/a~1b");
    }

    [Fact]
    public void ForField_with_null_or_empty_property_falls_back_to_root_pointer()
    {
        var error = Error.UnprocessableContent.ForField(string.Empty, "object_invalid");

        error.Fields[0].Field.Should().Be(InputPointer.Root);
    }

    // ── ForField(InputPointer, string, string?) ──────────────────────────────

    [Fact]
    public void ForField_with_pointer_uses_pointer_directly()
    {
        var pointer = new InputPointer("/items/0/quantity");
        var error = Error.UnprocessableContent.ForField(pointer, "out_of_range");

        error.Fields.Length.Should().Be(1);
        error.Fields[0].Field.Should().Be(pointer);
        error.Fields[0].ReasonCode.Should().Be("out_of_range");
        error.Fields[0].Detail.Should().BeNull();
    }

    [Fact]
    public void ForField_with_pointer_and_detail_propagates_detail()
    {
        var pointer = new InputPointer("/items/0/quantity");
        var error = Error.UnprocessableContent.ForField(pointer, "out_of_range", "must be positive");

        error.Fields[0].Detail.Should().Be("must be positive");
    }

    [Fact]
    public void ForField_with_root_pointer_produces_object_level_violation()
    {
        var error = Error.UnprocessableContent.ForField(InputPointer.Root, "object_required");

        error.Fields[0].Field.Should().Be(InputPointer.Root);
    }

    // ── ForRule(string, string?) ─────────────────────────────────────────────

    [Fact]
    public void ForRule_creates_single_rule_violation_with_empty_fields()
    {
        var error = Error.UnprocessableContent.ForRule("passwords_must_match");

        error.Fields.Length.Should().Be(0);
        error.Rules.Length.Should().Be(1);
        error.Rules[0].ReasonCode.Should().Be("passwords_must_match");
        error.Rules[0].Detail.Should().BeNull();
    }

    [Fact]
    public void ForRule_with_detail_propagates_detail()
    {
        var error = Error.UnprocessableContent.ForRule("passwords_must_match", "Passwords do not match");

        error.Rules[0].Detail.Should().Be("Passwords do not match");
    }

    // ── Equality + Kind preserved ────────────────────────────────────────────

    [Fact]
    public void ForField_results_equal_manual_construction()
    {
        var fromFactory = Error.UnprocessableContent.ForField("email", "invalid_format", "must contain @");
        var manual = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"), "invalid_format", Detail: "must contain @")));

        fromFactory.Equals(manual).Should().BeTrue();
        fromFactory.GetHashCode().Should().Be(manual.GetHashCode());
    }

    [Fact]
    public void ForRule_results_equal_manual_construction()
    {
        var fromFactory = Error.UnprocessableContent.ForRule("cancel_after_ship", "Cannot cancel after shipment");
        var manual = new Error.UnprocessableContent(
            EquatableArray<FieldViolation>.Empty,
            EquatableArray.Create(new RuleViolation("cancel_after_ship", Detail: "Cannot cancel after shipment")));

        fromFactory.Equals(manual).Should().BeTrue();
        fromFactory.GetHashCode().Should().Be(manual.GetHashCode());
    }

    [Fact]
    public void Factory_results_have_correct_Kind()
    {
        Error.UnprocessableContent.ForField("x", "y").Kind.Should().Be("unprocessable-content");
        Error.UnprocessableContent.ForRule("x").Kind.Should().Be("unprocessable-content");
    }

    // ── Pluggability into Result ─────────────────────────────────────────────

    [Fact]
    public void ForField_can_be_used_as_failure_payload()
    {
        Result<int> result = Result.Fail<int>(Error.UnprocessableContent.ForField("age", "out_of_range", "must be >= 18"));

        result.IsFailure.Should().BeTrue();
        var err = result.UnwrapError();
        err.Should().BeOfType<Error.UnprocessableContent>();
        ((Error.UnprocessableContent)err).Fields[0].Detail.Should().Be("must be >= 18");
    }
}
