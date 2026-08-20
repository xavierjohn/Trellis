namespace Trellis.Core.Tests.Errors;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="Error.Unexpected"/>, the closed-ADT case used for "this shouldn't have happened"
/// situations: default-initialized <c>Result</c>/<c>Result&lt;T&gt;</c>, unhandled exceptions captured by
/// the mediator pipeline, and other internal invariant violations. <see cref="Error.Unexpected.FaultId"/>
/// optionally correlates the case to deeper diagnostics.
/// </summary>
public class UnexpectedErrorTests
{
    [Fact]
    public void Kind_is_unexpected() =>
        new Error.Unexpected("default-initialized").Kind.Should().Be("unexpected");

    [Fact]
    public void Code_overrides_to_ReasonCode() =>
        new Error.Unexpected("internal_invariant_violated").Code.Should().Be("internal_invariant_violated");

    [Fact]
    public void Construct_with_ReasonCode_only_leaves_FaultId_null()
    {
        var error = new Error.Unexpected("default-initialized");

        error.ReasonCode.Should().Be("default-initialized");
        error.FaultId.Should().BeNull();
    }

    [Fact]
    public void Construct_with_ReasonCode_and_FaultId_preserves_both()
    {
        var error = new Error.Unexpected("unhandled-exception", "fault-7");

        error.ReasonCode.Should().Be("unhandled-exception");
        error.FaultId.Should().Be("fault-7");
    }

    [Fact]
    public void Detail_init_property_inherited_from_base()
    {
        var error = new Error.Unexpected("default-initialized") { Detail = "Result was default-initialized." };

        error.Detail.Should().Be("Result was default-initialized.");
    }

    [Fact]
    public void GetDisplayMessage_prefers_Detail_when_set()
    {
        var error = new Error.Unexpected("default-initialized") { Detail = "human-readable detail" };

        error.GetDisplayMessage().Should().Be("human-readable detail");
    }

    [Fact]
    public void GetDisplayMessage_falls_back_to_Code_when_Detail_null() =>
        new Error.Unexpected("invariant_violation").GetDisplayMessage().Should().Be("invariant_violation");

    [Fact]
    public void Two_Unexpected_with_same_payload_are_equal()
    {
        var a = new Error.Unexpected("default-initialized");
        var b = new Error.Unexpected("default-initialized");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Unexpected_with_different_ReasonCode_are_not_equal()
    {
        var a = new Error.Unexpected("default-initialized");
        var b = new Error.Unexpected("invariant_violation");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Two_Unexpected_with_different_FaultId_are_not_equal()
    {
        var a = new Error.Unexpected("unhandled-exception", "fault-1");
        var b = new Error.Unexpected("unhandled-exception", "fault-2");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Unexpected_with_null_vs_set_FaultId_are_not_equal()
    {
        var a = new Error.Unexpected("unhandled-exception");
        var b = new Error.Unexpected("unhandled-exception", "fault-1");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Cause_init_property_inherited_and_chained()
    {
        var inner = new Error.Unexpected("inner");
        var outer = new Error.Unexpected("outer") { Cause = inner };

        outer.Cause.Should().BeSameAs(inner);
    }

    [Fact]
    public void Switch_pattern_matches_as_distinct_case()
    {
        Error error = new Error.Unexpected("default-initialized");

        var matched = error switch
        {
            Error.Unexpected u => $"unexpected:{u.ReasonCode}",
            _ => "other",
        };

        matched.Should().Be("unexpected:default-initialized");
    }

    [Fact]
    public void ToString_includes_Kind_and_Code()
    {
        var error = new Error.Unexpected("invariant_violation");

        error.ToString().Should().Contain("unexpected");
        error.ToString().Should().Contain("invariant_violation");
    }
}