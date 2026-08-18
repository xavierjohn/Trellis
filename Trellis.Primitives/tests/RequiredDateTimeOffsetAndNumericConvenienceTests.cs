namespace Trellis.Primitives.Tests;

using System;
using Trellis.Testing;
using Xunit;

// RequiredDateTimeOffset bare base — accepts DateTimeOffset.MinValue under the lenient default.
public partial class EventOccurredAt : RequiredDateTimeOffset<EventOccurredAt> { }

// Strict RequiredDateTimeOffset — opts into MinValue rejection via [NotDefault].
[NotDefault] public partial class StrictOccurredAt : RequiredDateTimeOffset<StrictOccurredAt> { }

// Numeric convenience attributes on the three numeric Required bases.
[Positive] public partial class PositiveInt : RequiredInt<PositiveInt> { }
[NonNegative] public partial class NonNegativeInt : RequiredInt<NonNegativeInt> { }
[Negative] public partial class NegativeInt : RequiredInt<NegativeInt> { }
[NonPositive] public partial class NonPositiveInt : RequiredInt<NonPositiveInt> { }

[Positive] public partial class PositiveLong : RequiredLong<PositiveLong> { }
[NonNegative] public partial class NonNegativeLong : RequiredLong<NonNegativeLong> { }
[Negative] public partial class NegativeLong : RequiredLong<NegativeLong> { }
[NonPositive] public partial class NonPositiveLong : RequiredLong<NonPositiveLong> { }

[Positive] public partial class PositiveDecimal : RequiredDecimal<PositiveDecimal> { }
[NonNegative] public partial class NonNegativeDecimal : RequiredDecimal<NonNegativeDecimal> { }
[Negative] public partial class NegativeDecimal : RequiredDecimal<NegativeDecimal> { }
[NonPositive] public partial class NonPositiveDecimal : RequiredDecimal<NonPositiveDecimal> { }

/// <summary>
/// Tests for the additive Required<T> primitives added in PR2a:
/// the <c>RequiredDateTimeOffset&lt;T&gt;</c> base class and the
/// <c>[Positive]</c> / <c>[NonNegative]</c> / <c>[Negative]</c> / <c>[NonPositive]</c>
/// numeric convenience attributes (which translate to existing <c>[Range]</c> semantics on
/// integer types and to a direct sign-check on <c>RequiredDecimal</c>). Test method names
/// follow the codebase convention <c>[Method]_[Variant]_[Scenario]_[Expectation]</c>.
/// </summary>
public class RequiredDateTimeOffsetAndNumericConvenienceTests
{
    // ---------- RequiredDateTimeOffset ----------

    [Fact]
    public void TryCreate_LenientDateTimeOffset_AcceptsPresentValue()
    {
        var now = DateTimeOffset.UtcNow;
        var result = EventOccurredAt.TryCreate(now);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(now);
    }

    [Fact]
    public void TryCreate_LenientDateTimeOffset_AcceptsMinValue()
    {
        var result = EventOccurredAt.TryCreate(DateTimeOffset.MinValue);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public void TryCreate_LenientDateTimeOffset_RejectsNull()
    {
        var result = EventOccurredAt.TryCreate((DateTimeOffset?)null);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_LenientDateTimeOffsetFromString_PreservesOffsetOnRoundTrip()
    {
        var fixedOffset = new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.FromHours(-5));
        var parsed = EventOccurredAt.TryCreate(fixedOffset.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        parsed.IsSuccess.Should().BeTrue();
        parsed.Unwrap().Value.Should().Be(fixedOffset);
        parsed.Unwrap().Value.Offset.Should().Be(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void TryCreate_StrictDateTimeOffset_RejectsMinValue()
    {
        var result = StrictOccurredAt.TryCreate(DateTimeOffset.MinValue);
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().GetDisplayMessage().Should().Contain("DateTimeOffset.MinValue");
    }

    [Fact]
    public void TryCreate_StrictDateTimeOffset_AcceptsPresentValue()
    {
        var now = DateTimeOffset.UtcNow;
        var result = StrictOccurredAt.TryCreate(now);
        result.IsSuccess.Should().BeTrue();
    }

    // ---------- [Positive] ----------

    [Fact]
    public void TryCreate_PositiveInt_AcceptsPositivesRejectsZeroAndNegatives()
    {
        PositiveInt.TryCreate(1).IsSuccess.Should().BeTrue();
        PositiveInt.TryCreate(int.MaxValue).IsSuccess.Should().BeTrue();
        PositiveInt.TryCreate(0).IsFailure.Should().BeTrue();
        PositiveInt.TryCreate(-1).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_PositiveLong_AcceptsPositivesRejectsZeroAndNegatives()
    {
        PositiveLong.TryCreate(1L).IsSuccess.Should().BeTrue();
        PositiveLong.TryCreate(0L).IsFailure.Should().BeTrue();
        PositiveLong.TryCreate(-1L).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_PositiveDecimal_AcceptsPositivesRejectsZeroAndNegatives()
    {
        PositiveDecimal.TryCreate(0.01m).IsSuccess.Should().BeTrue();
        PositiveDecimal.TryCreate(0m).IsFailure.Should().BeTrue();
        PositiveDecimal.TryCreate(-0.01m).IsFailure.Should().BeTrue();
    }

    // ---------- [NonNegative] ----------

    [Fact]
    public void TryCreate_NonNegativeInt_AcceptsZeroAndPositivesRejectsNegatives()
    {
        NonNegativeInt.TryCreate(0).IsSuccess.Should().BeTrue();
        NonNegativeInt.TryCreate(1).IsSuccess.Should().BeTrue();
        NonNegativeInt.TryCreate(-1).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_NonNegativeLong_AcceptsZeroAndPositivesRejectsNegatives()
    {
        NonNegativeLong.TryCreate(0L).IsSuccess.Should().BeTrue();
        NonNegativeLong.TryCreate(-1L).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_NonNegativeDecimal_AcceptsZeroAndPositivesRejectsNegatives()
    {
        NonNegativeDecimal.TryCreate(0m).IsSuccess.Should().BeTrue();
        NonNegativeDecimal.TryCreate(0.01m).IsSuccess.Should().BeTrue();
        NonNegativeDecimal.TryCreate(-0.01m).IsFailure.Should().BeTrue();
    }

    // ---------- [Negative] ----------

    [Fact]
    public void TryCreate_NegativeInt_AcceptsNegativesRejectsZeroAndPositives()
    {
        NegativeInt.TryCreate(-1).IsSuccess.Should().BeTrue();
        NegativeInt.TryCreate(int.MinValue).IsSuccess.Should().BeTrue();
        NegativeInt.TryCreate(0).IsFailure.Should().BeTrue();
        NegativeInt.TryCreate(1).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_NegativeLong_AcceptsNegativesRejectsZeroAndPositives()
    {
        NegativeLong.TryCreate(-1L).IsSuccess.Should().BeTrue();
        NegativeLong.TryCreate(0L).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_NegativeDecimal_AcceptsNegativesRejectsZeroAndPositives()
    {
        NegativeDecimal.TryCreate(-0.01m).IsSuccess.Should().BeTrue();
        NegativeDecimal.TryCreate(0m).IsFailure.Should().BeTrue();
    }

    // ---------- [NonPositive] ----------

    [Fact]
    public void TryCreate_NonPositiveInt_AcceptsZeroAndNegativesRejectsPositives()
    {
        NonPositiveInt.TryCreate(0).IsSuccess.Should().BeTrue();
        NonPositiveInt.TryCreate(-1).IsSuccess.Should().BeTrue();
        NonPositiveInt.TryCreate(1).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_NonPositiveLong_AcceptsZeroAndNegativesRejectsPositives()
    {
        NonPositiveLong.TryCreate(0L).IsSuccess.Should().BeTrue();
        NonPositiveLong.TryCreate(-1L).IsSuccess.Should().BeTrue();
        NonPositiveLong.TryCreate(1L).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_NonPositiveDecimal_AcceptsZeroAndNegativesRejectsPositives()
    {
        NonPositiveDecimal.TryCreate(0m).IsSuccess.Should().BeTrue();
        NonPositiveDecimal.TryCreate(-0.01m).IsSuccess.Should().BeTrue();
        NonPositiveDecimal.TryCreate(0.01m).IsFailure.Should().BeTrue();
    }
}