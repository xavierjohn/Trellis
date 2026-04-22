namespace Trellis.Core.Tests.Results;

using Trellis.Testing;

/// <summary>
/// Invariants for the default-state of <see cref="Result"/> and <see cref="Result{TValue}"/>
/// per ADR-002 §3.5.1.
/// </summary>
/// <remarks>
/// <para>
/// <c>default(Result)</c> and <c>default(Result&lt;T&gt;)</c> must be observationally
/// equivalent to <c>Result.Fail(new Error.Unexpected("default_initialized"))</c> /
/// <c>Result.Fail&lt;T&gt;(new Error.Unexpected("default_initialized"))</c>. This means every failure-facing API
/// (<see cref="Result.Error"/>, <c>TryGetError</c>, <c>Deconstruct</c>, <c>Equals</c>,
/// <c>GetHashCode</c>, <c>ToString</c>, <c>AsUnit</c>) must route through the same
/// effective-error helper so that callers cannot distinguish the two forms.
/// </para>
/// </remarks>
public class DefaultStateInvariantTests
{
    private static Error.Unexpected MakeSentinel() => new Error.Unexpected("default_initialized")
    {
        Detail = "Result was default-initialized; use Result.Ok(...) or Result.Fail(...) instead.",
    };

    // ── default(Result) — non-generic ────────────────────────────────────────

    [Fact]
    public void Default_Result_is_failure()
    {
        Result r = default;

        r.IsFailure.Should().BeTrue();
        r.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Default_Result_Error_is_Unexpected_default_initialized()
    {
        Result r = default;

        r.Error!.Should().NotBeNull();
        r.Error!.Should().BeOfType<Error.Unexpected>();
        ((Error.Unexpected)r.Error!).ReasonCode.Should().Be("default_initialized");
    }

    [Fact]
    public void Default_Result_TryGetError_returns_true_with_sentinel()
    {
        Result r = default;

        r.TryGetError(out var error).Should().BeTrue();
        error!.Should().BeOfType<Error.Unexpected>();
    }

    [Fact]
    public void Default_Result_Deconstruct_yields_failure_and_sentinel()
    {
        Result r = default;

        var (isSuccess, error) = r;

        isSuccess.Should().BeFalse();
        error!.Should().BeOfType<Error.Unexpected>();
    }

    [Fact]
    public void Default_Result_equals_explicit_Fail_with_sentinel()
    {
        Result a = default;
        Result b = Result.Fail(MakeSentinel());

        a.Equals(b).Should().BeTrue();
        b.Equals(a).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_default_Results_are_equal()
    {
        Result a = default;
        Result b = default;

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Default_Result_does_not_equal_Result_Ok()
    {
        Result a = default;
        Result b = Result.Ok();

        a.Equals(b).Should().BeFalse();
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void Default_Result_ToString_does_not_say_Success()
    {
        Result r = default;

        r.ToString().Should().NotContain("Success");
        r.ToString().Should().Contain("Failure");
    }

    // ── default(Result<T>) — generic ─────────────────────────────────────────

    [Fact]
    public void Default_ResultT_is_failure()
    {
        Result<int> r = default;

        r.IsFailure.Should().BeTrue();
        r.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Default_ResultT_Error_is_Unexpected_default_initialized()
    {
        Result<string> r = default;

        r.Error!.Should().NotBeNull();
        r.Error!.Should().BeOfType<Error.Unexpected>();
        ((Error.Unexpected)r.Error!).ReasonCode.Should().Be("default_initialized");
    }

    [Fact]
    public void Default_ResultT_TryGetError_returns_true_with_sentinel()
    {
        Result<int> r = default;

        r.TryGetError(out var error).Should().BeTrue();
        error!.Should().BeOfType<Error.Unexpected>();
    }

    [Fact]
    public void Default_ResultT_Deconstruct_yields_failure_default_value_and_sentinel()
    {
        Result<int> r = default;

        var (isSuccess, value, error) = r;

        isSuccess.Should().BeFalse();
        value.Should().Be(0);
        error!.Should().BeOfType<Error.Unexpected>();
    }

    [Fact]
    public void Result_T_does_not_expose_throwing_Value_property()
    {
        // ADR-002 §3.1 + ga-03 API freeze: Result<T>.Value (which threw on failure) is removed.
        // Use TryGetValue, Match, or Deconstruct to extract the success value safely.
        // Error stays (it never throws — see ga-03 commit message for rationale).
        var prop = typeof(Result<int>).GetProperty("Value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        prop.Should().BeNull("Result<T>.Value was removed in v2 to eliminate TRLS003 (unsafe Value access). Use TryGetValue / Match / Deconstruct instead.");
    }

    [Fact]
    public void Default_ResultT_TryGetValue_returns_false()
    {
        Result<int> r = default;

        r.TryGetValue(out var value).Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public void Default_ResultT_equals_explicit_Fail_with_sentinel()
    {
        Result<int> a = default;
        Result<int> b = Result.Fail<int>(MakeSentinel());

        a.Equals(b).Should().BeTrue();
        b.Equals(a).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_default_ResultT_are_equal_no_NRE()
    {
        Result<int> a = default;
        Result<int> b = default;

        // pre-fix: this NRE'd inside Equals because both _error fields were null
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Default_ResultT_does_not_equal_Ok_value()
    {
        Result<int> a = default;
        Result<int> b = Result.Ok(0);

        a.Equals(b).Should().BeFalse();
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void Default_ResultT_ToString_does_not_say_Success()
    {
        Result<int> r = default;

        r.ToString().Should().NotContain("Success");
        r.ToString().Should().Contain("Failure");
    }

    // ── AsUnit observational equivalence ─────────────────────────────────────

    [Fact]
    public void Default_ResultT_AsUnit_returns_explicit_failure_not_default()
    {
        Result<int> r = default;

        Result asUnit = r.AsUnit();

        // Must NOT be `default(Result)` — must be an explicit Fail with the sentinel.
        // We assert observational equivalence via Equals(explicit Fail).
        asUnit.IsFailure.Should().BeTrue();
        asUnit.Error!.Should().BeOfType<Error.Unexpected>();
        asUnit.Equals(Result.Fail(MakeSentinel())).Should().BeTrue();
    }

    // ── Maybe<T> default = None (already correct, regression guard) ──────────

    [Fact]
    public void Default_MaybeT_is_None()
    {
        Maybe<int> m = default;

        m.HasValue.Should().BeFalse();
        m.Equals(Maybe<int>.None).Should().BeTrue();
    }

    // ── Sentinel sharing — single allocation across closed generics ──────────

    [Fact]
    public void Sentinel_is_shared_across_closed_generic_Result_types()
    {
        Result<int> ri = default;
        Result<string> rs = default;
        Result rn = default;

        // Same Error reference (single shared sentinel allocation per program).
        ReferenceEquals(ri.Error, rs.Error).Should().BeTrue();
        ReferenceEquals(ri.Error, rn.Error).Should().BeTrue();
    }

    // ── Activity status tagging is best-effort and NOT required for default ──

    [Fact]
    public void Default_Result_does_not_throw_when_observed_under_no_activity()
    {
        // Just construct + observe — confirms no NRE / no required init path.
        Result r = default;
        Result<int> rt = default;

        _ = r.Error;
        _ = r.IsFailure;
        _ = r.ToString();
        _ = rt.Error;
        _ = rt.IsFailure;
        _ = rt.ToString();
    }
}
