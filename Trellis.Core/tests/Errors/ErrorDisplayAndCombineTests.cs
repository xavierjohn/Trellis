namespace Trellis.Core.Tests.Errors;

using Trellis.Testing;

/// <summary>
/// Bug-fix tests covering wrapper-Detail / Rules surfacing in <see cref="Error.GetDisplayMessage"/>
/// and Combine semantics for two <see cref="Error.UnprocessableContent"/> values.
/// </summary>
public class ErrorDisplayAndCombineTests
{
    // ── GetDisplayMessage ──────────────────────────────────────────────────

    [Fact]
    public void GetDisplayMessage_UnprocessableContent_with_only_Rules_returns_rule_details()
    {
        var rule1 = new RuleViolation("order.must_have_items", Detail: "Order must contain at least one item");
        var rule2 = new RuleViolation("cancel_after_ship", Detail: "Cannot cancel after shipment");
        var error = new Error.UnprocessableContent(
            EquatableArray<FieldViolation>.Empty,
            EquatableArray.Create(rule1, rule2));

        var msg = error.GetDisplayMessage();

        msg.Should().Contain("Order must contain at least one item");
        msg.Should().Contain("Cannot cancel after shipment");
    }

    [Fact]
    public void GetDisplayMessage_UnprocessableContent_rule_without_Detail_falls_back_to_ReasonCode()
    {
        var rule = new RuleViolation("passwords_must_match");
        var error = new Error.UnprocessableContent(
            EquatableArray<FieldViolation>.Empty,
            EquatableArray.Create(rule));

        error.GetDisplayMessage().Should().Contain("passwords_must_match");
    }

    [Fact]
    public void GetDisplayMessage_UnprocessableContent_with_Fields_and_Rules_includes_both()
    {
        var field = new FieldViolation(InputPointer.ForProperty("email"), "invalid") { Detail = "Bad email" };
        var rule = new RuleViolation("must_be_unique", Detail: "Email must be unique");
        var error = new Error.UnprocessableContent(
            EquatableArray.Create(field),
            EquatableArray.Create(rule));

        var msg = error.GetDisplayMessage();

        msg.Should().Contain("Bad email");
        msg.Should().Contain("Email must be unique");
    }

    // ── Combine preserves wrapper Detail ───────────────────────────────────

    [Fact]
    public void Combine_two_UnprocessableContent_preserves_both_wrapper_Details()
    {
        var a = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "A failed" };
        var b = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "B failed" };

        var combined = ((Error?)a).Combine(b);

        combined.Should().BeOfType<Error.UnprocessableContent>();
        combined.Detail.Should().Contain("A failed").And.Contain("B failed");
    }

    [Fact]
    public void Combine_two_UnprocessableContent_with_one_Detail_preserves_it()
    {
        var a = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Only A" };
        var b = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty);

        var combined = ((Error?)a).Combine(b);

        combined.Detail.Should().Be("Only A");
    }

    [Fact]
    public void Combine_two_UnprocessableContent_with_identical_Detail_dedupes()
    {
        var a = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Same" };
        var b = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Same" };

        var combined = ((Error?)a).Combine(b);

        combined.Detail.Should().Be("Same");
    }
}