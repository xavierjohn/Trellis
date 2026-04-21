namespace Trellis.Core.Tests.Errors;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="Error.Unexpected"/>, the closed-ADT case used for "this shouldn't have happened"
/// situations. Most prominent use: the sentinel error returned by <c>default(Result)</c> /
/// <c>default(Result&lt;T&gt;)</c> per ADR-002 §3.5.1.
/// </summary>
/// <remarks>
/// Distinct from <see cref="Error.InternalServerError"/>:
/// <list type="bullet">
///   <item><description><see cref="Error.InternalServerError"/> = a documented server-side fault with a tracking <c>FaultId</c>.</description></item>
///   <item><description><see cref="Error.Unexpected"/> = a "shouldn't happen" condition (default-init, exhausted match, internal invariant violation) carrying a stable <c>ReasonCode</c>.</description></item>
/// </list>
/// Both map to HTTP 500 at the ASP boundary, but only <see cref="Error.InternalServerError"/> attaches a <c>faultId</c> extension.
/// </remarks>
public class UnexpectedErrorTests
{
    [Fact]
    public void Construct_with_ReasonCode_sets_ReasonCode_property()
    {
        var error = new Error.Unexpected("default_initialized");

        error.ReasonCode.Should().Be("default_initialized");
    }

    [Fact]
    public void Kind_is_unexpected()
    {
        var error = new Error.Unexpected("default_initialized");

        error.Kind.Should().Be("unexpected");
    }

    [Fact]
    public void Code_overrides_to_ReasonCode()
    {
        var error = new Error.Unexpected("internal_invariant_violated");

        error.Code.Should().Be("internal_invariant_violated");
    }

    [Fact]
    public void Detail_init_property_inherited_from_base()
    {
        var error = new Error.Unexpected("default_initialized")
        {
            Detail = "Result was default-initialized; use Result.Ok(...) or Result.Fail<T>(...).",
        };

        error.Detail.Should().Be("Result was default-initialized; use Result.Ok(...) or Result.Fail<T>(...).");
    }

    [Fact]
    public void GetDisplayMessage_prefers_Detail_when_set()
    {
        var error = new Error.Unexpected("default_initialized")
        {
            Detail = "human-readable detail",
        };

        error.GetDisplayMessage().Should().Be("human-readable detail");
    }

    [Fact]
    public void GetDisplayMessage_falls_back_to_Code_when_Detail_null()
    {
        var error = new Error.Unexpected("invariant_violation");

        error.GetDisplayMessage().Should().Be("invariant_violation");
    }

    [Fact]
    public void Two_Unexpected_with_same_ReasonCode_are_equal()
    {
        var a = new Error.Unexpected("default_initialized");
        var b = new Error.Unexpected("default_initialized");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Unexpected_with_different_ReasonCode_are_not_equal()
    {
        var a = new Error.Unexpected("default_initialized");
        var b = new Error.Unexpected("invariant_violation");

        a.Equals(b).Should().BeFalse();
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void Unexpected_does_not_equal_InternalServerError_with_matching_payload()
    {
        var unexpected = new Error.Unexpected("fault-123");
        var ise = new Error.InternalServerError("fault-123");

        ((Error)unexpected).Equals((Error)ise).Should().BeFalse();
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
        Error error = new Error.Unexpected("default_initialized");

        var matched = error switch
        {
            Error.InternalServerError => "ise",
            Error.Unexpected u => $"unexpected:{u.ReasonCode}",
            _ => "other",
        };

        matched.Should().Be("unexpected:default_initialized");
    }

    [Fact]
    public void ToString_includes_Kind_and_Code()
    {
        var error = new Error.Unexpected("invariant_violation");

        error.ToString().Should().Contain("unexpected");
        error.ToString().Should().Contain("invariant_violation");
    }
}
