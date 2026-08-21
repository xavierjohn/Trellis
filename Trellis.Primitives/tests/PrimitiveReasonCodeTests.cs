namespace Trellis.Primitives.Tests;

using Trellis;
using Trellis.Primitives;
using Trellis.Testing;

/// <summary>
/// Pins the reason codes and operands the hand-written primitives put on the wire.
/// </summary>
/// <remarks>
/// These primitives are hand-written rather than generated, so nothing else forces them to agree with
/// the generator on the same failure. The vocabulary freezes on release, which makes a disagreement
/// here permanent rather than a bug someone fixes later.
/// </remarks>
public class PrimitiveReasonCodeTests
{
    private static FieldViolation FirstViolation<T>(Result<T> result) =>
        ((Error.InvalidInput)result.UnwrapError()).Fields[0];

    [Fact]
    public void Age_below_the_floor_reports_a_directional_code_naming_the_bound()
    {
        var violation = FirstViolation(Age.TryCreate(-1));

        violation.ReasonCode.Should().Be(ValidationCodes.ValueGreaterThanOrEqual);
        violation.Args.Should().Contain(new KeyValuePair<string, string>("comparisonValue", "0"));
    }

    [Fact]
    public void Age_above_the_ceiling_reports_the_opposite_direction()
    {
        var violation = FirstViolation(Age.TryCreate(151));

        violation.ReasonCode.Should().Be(ValidationCodes.ValueLessThanOrEqual);
        violation.Args.Should().Contain(new KeyValuePair<string, string>("comparisonValue", "150"));
    }

    [Fact]
    public void Percentage_bounds_are_directional_too()
    {
        FirstViolation(Percentage.TryCreate(-0.5m)).ReasonCode.Should().Be(ValidationCodes.ValueGreaterThanOrEqual);
        FirstViolation(Percentage.TryCreate(100.5m)).ReasonCode.Should().Be(ValidationCodes.ValueLessThanOrEqual);
    }

    [Fact]
    public void Percentage_FromFraction_names_the_fraction_bound_not_the_percent_bound() =>
        // The caller passed a fraction, so `comparisonValue: 100` would name a bound they never
        // supplied a value against.
        FirstViolation(Percentage.FromFraction(1.5m)).Args
            .Should().Contain(new KeyValuePair<string, string>("comparisonValue", "1"));

    [Theory]
    [InlineData(null, "value.not-null")]
    [InlineData("", "value.not-empty")]
    [InlineData("   ", "value.not-empty")]
    public void EmailAddress_separates_absent_from_blank_before_reaching_the_shape_check(string? input, string expected) =>
        FirstViolation(EmailAddress.TryCreate(input)).ReasonCode.Should().Be(expected);

    [Fact]
    public void EmailAddress_reports_the_shape_code_only_when_a_value_actually_arrived() =>
        FirstViolation(EmailAddress.TryCreate("not-an-email")).ReasonCode.Should().Be(ValidationCodes.StringEmail);

    [Fact]
    public void PhoneNumber_reports_blank_the_same_way_however_long_the_whitespace_run_is() =>
        // The length cap exists to stop a megabyte of spaces being walked; it must not change the
        // code, or the answer would depend on how much whitespace the caller happened to send.
        FirstViolation(PhoneNumber.TryCreate(new string(' ', 5_000))).ReasonCode
            .Should().Be(FirstViolation(PhoneNumber.TryCreate("  ")).ReasonCode);

    [Fact]
    public void Money_currency_mismatch_carries_both_currencies()
    {
        var usd = Money.TryCreate(1m, CurrencyCode.TryCreate("USD").Unwrap()).Unwrap();
        var eur = Money.TryCreate(1m, CurrencyCode.TryCreate("EUR").Unwrap()).Unwrap();

        var violation = FirstViolation(usd.Add(eur));

        violation.ReasonCode.Should().Be(ValidationCodes.MoneyCurrencyMismatch);
        violation.Args.Should().Contain(new KeyValuePair<string, string>("expected", "USD"));
        violation.Args.Should().Contain(new KeyValuePair<string, string>("actual", "EUR"));
    }
}
