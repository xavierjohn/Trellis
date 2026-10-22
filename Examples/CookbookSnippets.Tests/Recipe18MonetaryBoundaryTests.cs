namespace CookbookSnippets.Tests;

using CookbookSnippets.Recipe18;
using Trellis.Primitives;
using Trellis.Testing;

public class Recipe18MonetaryBoundaryTests
{
    [Fact]
    public void TryCreateMatchingQuote_ExactInput_MatchingQuote_ReturnsAmount()
    {
        var quote = MonetaryAmount.Create(20.25m);

        var result = PaymentAmountBoundary.TryCreateMatchingQuote(20.25m, quote);

        result.Should().BeSuccess().Which.Value.Should().Be(20.25m);
    }

    [Fact]
    public void TryCreateMatchingQuote_ExactInput_EquivalentDecimalScale_ReturnsAmount()
    {
        var quote = MonetaryAmount.Create(20.25m);

        var result = PaymentAmountBoundary.TryCreateMatchingQuote(20.2500m, quote);

        result.Should().BeSuccess().Which.Should().Be(quote);
    }

    [Fact]
    public void TryCreateMatchingQuote_ExactInput_ZeroQuote_ReturnsAmount()
    {
        var result = PaymentAmountBoundary.TryCreateMatchingQuote(0m, MonetaryAmount.Zero);

        result.Should().BeSuccess().Which.Value.Should().Be(0m);
    }

    [Fact]
    public void TryCreateMatchingQuote_ExactInput_RoundingCollision_ReturnsMismatch()
    {
        var quote = MonetaryAmount.Create(1.00m);
        var normalized = MonetaryAmount.TryCreate(1.004m).Unwrap();
        normalized.Should().Be(quote);

        var result = PaymentAmountBoundary.TryCreateMatchingQuote(1.004m, quote);

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        var violation = error.Fields.Items.Should().ContainSingle().Which;
        violation.Field.Path.Should().Be("/amount");
        violation.ReasonCode.Should().Be("payment.amount-mismatch");
    }

    [Fact]
    public void TryCreateMatchingQuote_ExactInput_DifferentAmount_ReturnsMismatch()
    {
        var result = PaymentAmountBoundary.TryCreateMatchingQuote(
            1.01m, MonetaryAmount.Create(1.00m));

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        var violation = error.Fields.Items.Should().ContainSingle().Which;
        violation.Field.Path.Should().Be("/amount");
        violation.ReasonCode.Should().Be("payment.amount-mismatch");
    }

    [Fact]
    public void TryCreateMatchingQuote_ExactInput_MissingAmount_ReturnsRequiredViolation()
    {
        var result = PaymentAmountBoundary.TryCreateMatchingQuote(
            null, MonetaryAmount.Create(1.00m));

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        var violation = error.Fields.Items.Should().ContainSingle().Which;
        violation.Field.Path.Should().Be("/amount");
        violation.ReasonCode.Should().Be(ValidationCodes.ValueNotNull);
    }

    [Fact]
    public void TryCreateMatchingQuote_ExactInput_NegativeAmount_ReturnsMismatch()
    {
        var result = PaymentAmountBoundary.TryCreateMatchingQuote(
            -0.001m, MonetaryAmount.Zero);

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        var violation = error.Fields.Items.Should().ContainSingle().Which;
        violation.Field.Path.Should().Be("/amount");
        violation.ReasonCode.Should().Be("payment.amount-mismatch");
    }

    [Fact]
    public void TryCreateMatchingQuote_ExactInput_MissingQuote_ThrowsArgumentNullException()
    {
        Action action = () => _ = PaymentAmountBoundary.TryCreateMatchingQuote(1m, null!);

        action.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("quotedAmount");
    }

    [Fact]
    public void TryCreate_MonetaryAmount_Midpoints_RoundsAwayFromZero()
    {
        MonetaryAmount.TryCreate(1.004m).Unwrap().Value.Should().Be(1.00m);
        MonetaryAmount.TryCreate(1.005m).Unwrap().Value.Should().Be(1.01m);
    }

    [Fact]
    public void TryCreate_MonetaryAmount_DistinctRawAmounts_ShareNormalizedIdentity()
    {
        var first = MonetaryAmount.TryCreate(1.001m).Unwrap();
        var second = MonetaryAmount.TryCreate(1.004m).Unwrap();
        HashSet<MonetaryAmount> amounts = [first, second];

        first.Should().Be(second);
        amounts.Should().ContainSingle();
    }

    [Fact]
    public void TryCreate_MonetaryAmount_NegativeSubCentAmount_ReturnsFailure()
    {
        var result = MonetaryAmount.TryCreate(-0.001m);

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        var violation = error.Fields.Items.Should().ContainSingle().Which;
        violation.Field.Path.Should().Be("/amount");
        violation.ReasonCode.Should().Be(ValidationCodes.ValueGreaterThanOrEqual);
    }
}