namespace Trellis.Primitives.Tests;

using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="MonetaryAmount"/> — a single-currency monetary value.
/// Like <see cref="Money"/> but without the currency column.
/// </summary>
public class MonetaryAmountTests
{
    #region Creation and Validation

    [Fact]
    public void TryCreate_ValidAmount_ReturnsSuccess()
    {
        var result = MonetaryAmount.TryCreate(29.99m);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(29.99m);
    }

    [Fact]
    public void TryCreate_Zero_ReturnsSuccess()
    {
        var result = MonetaryAmount.TryCreate(0m);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(0m);
    }

    [Fact]
    public void TryCreate_NegativeAmount_ReturnsFailure()
    {
        var result = MonetaryAmount.TryCreate(-1m);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_RoundsToTwoDecimalPlaces()
    {
        var result = MonetaryAmount.TryCreate(29.999m);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(30.00m);
    }

    [Fact]
    public void TryCreate_NullableDecimal_Null_ReturnsFailure()
    {
        decimal? value = null;
        var result = MonetaryAmount.TryCreate(value);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_NullableDecimal_ValidValue_ReturnsSuccess()
    {
        decimal? value = 15.50m;
        var result = MonetaryAmount.TryCreate(value);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(15.50m);
    }

    [Fact]
    public void Create_ValidAmount_ReturnsInstance()
    {
        var amount = MonetaryAmount.Create(49.95m);
        amount.Value.Should().Be(49.95m);
    }

    [Fact]
    public void Zero_ReturnsZeroAmount()
    {
        var zero = MonetaryAmount.Zero;
        zero.Value.Should().Be(0m);
    }

    [Fact]
    public void ExplicitCast_FromDecimal_ReturnsInstance()
    {
        var amount = (MonetaryAmount)29.99m;
        amount.Value.Should().Be(29.99m);
    }

    #endregion

    #region Arithmetic

    [Fact]
    public void Add_ReturnsSum()
    {
        var a = MonetaryAmount.Create(10.00m);
        var b = MonetaryAmount.Create(20.50m);

        var result = a.Add(b);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(30.50m);
    }

    [Fact]
    public void Subtract_ReturnsResult()
    {
        var a = MonetaryAmount.Create(50.00m);
        var b = MonetaryAmount.Create(20.00m);

        var result = a.Subtract(b);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(30.00m);
    }

    [Fact]
    public void Subtract_NegativeResult_ReturnsFailure()
    {
        var a = MonetaryAmount.Create(10.00m);
        var b = MonetaryAmount.Create(20.00m);

        var result = a.Subtract(b);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Multiply_ByInt_ReturnsResult()
    {
        var amount = MonetaryAmount.Create(10.00m);

        var result = amount.Multiply(3);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(30.00m);
    }

    [Fact]
    public void Multiply_ByNegative_ReturnsFailure()
    {
        var amount = MonetaryAmount.Create(10.00m);

        var result = amount.Multiply(-1);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Multiply_ByDecimal_ReturnsResult()
    {
        var amount = MonetaryAmount.Create(10.00m);

        var result = amount.Multiply(1.5m);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(15.00m);
    }

    [Fact]
    public void Multiply_ByNegativeDecimal_ReturnsFailure()
    {
        var amount = MonetaryAmount.Create(10.00m);

        var result = amount.Multiply(-0.5m);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Add_NearMaxValue_ReturnsFailure()
    {
        var a = MonetaryAmount.Create(decimal.MaxValue - 1m);
        var b = MonetaryAmount.Create(decimal.MaxValue - 1m);

        var result = a.Add(b);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Multiply_LargeValueOverflow_ReturnsFailure()
    {
        var amount = MonetaryAmount.Create(decimal.MaxValue - 1m);

        var result = amount.Multiply(2);
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Equality and Comparison

    [Fact]
    public void EqualAmounts_AreEqual()
    {
        var a = MonetaryAmount.Create(99.99m);
        var b = MonetaryAmount.Create(99.99m);

        a.Should().Be(b);
    }

    [Fact]
    public void DifferentAmounts_AreNotEqual()
    {
        var a = MonetaryAmount.Create(99.99m);
        var b = MonetaryAmount.Create(100.00m);

        a.Should().NotBe(b);
    }

    #endregion

    #region JSON Serialization

    [Fact]
    public void Json_SerializesAsDecimal()
    {
        var amount = MonetaryAmount.Create(29.99m);
        var json = System.Text.Json.JsonSerializer.Serialize(amount);

        json.Should().Be("29.99");
    }

    [Fact]
    public void Json_DeserializesFromDecimal()
    {
        var amount = System.Text.Json.JsonSerializer.Deserialize<MonetaryAmount>("29.99");

        amount.Should().NotBeNull();
        amount!.Value.Should().Be(29.99m);
    }

    #endregion

    #region Parsing

    [Fact]
    public void Parse_ValidInput_ReturnsInstance()
    {
        var amount = MonetaryAmount.Parse("29.99", System.Globalization.CultureInfo.InvariantCulture);
        amount.Value.Should().Be(29.99m);
    }

    [Fact]
    public void Parse_NullInput_ThrowsFormatException()
    {
        var act = () => MonetaryAmount.Parse(null, System.Globalization.CultureInfo.InvariantCulture);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_EmptyInput_ThrowsFormatException()
    {
        var act = () => MonetaryAmount.Parse(string.Empty, System.Globalization.CultureInfo.InvariantCulture);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_NegativeInput_ThrowsFormatException()
    {
        var act = () => MonetaryAmount.Parse("-5.00", System.Globalization.CultureInfo.InvariantCulture);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_ValidInput_ReturnsTrue()
    {
        var success = MonetaryAmount.TryParse("42.50", System.Globalization.CultureInfo.InvariantCulture, out var result);
        success.Should().BeTrue();
        result!.Value.Should().Be(42.50m);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        var success = MonetaryAmount.TryParse("not-a-number", System.Globalization.CultureInfo.InvariantCulture, out var result);
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    #endregion

    #region ToString

    [Fact]
    public void ToString_ReturnsFormattedAmount()
    {
        var amount = MonetaryAmount.Create(1234.56m);
        amount.ToString().Should().Be("1234.56");
    }

    #endregion
}
