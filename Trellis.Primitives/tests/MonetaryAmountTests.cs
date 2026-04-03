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

    #region TryCreate from string

    [Theory]
    [InlineData("0", 0)]
    [InlineData("29.99", 29.99)]
    [InlineData("1234.56", 1234.56)]
    public void TryCreate_string_valid_returns_success(string input, decimal expected)
    {
        // Act
        var result = MonetaryAmount.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Fact]
    public void TryCreate_string_null_returns_failure()
    {
        // Act
        var result = MonetaryAmount.TryCreate((string?)null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_empty_returns_failure()
    {
        // Act
        var result = MonetaryAmount.TryCreate("");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_whitespace_returns_failure()
    {
        // Act
        var result = MonetaryAmount.TryCreate("  ");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("not-a-number")]
    public void TryCreate_string_invalid_format_returns_failure(string input)
    {
        // Act
        var result = MonetaryAmount.TryCreate(input);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_uses_custom_fieldName()
    {
        // Act
        var result = MonetaryAmount.TryCreate((string?)null, "Price");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("price");
    }

    [Fact]
    public void TryCreate_string_delegates_validation_to_decimal_overload()
    {
        // Act — valid parse but negative amount
        var result = MonetaryAmount.TryCreate("-5.00");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Amount cannot be negative.");
    }

    [Fact]
    public void TryCreate_string_uses_invariant_culture()
    {
        // Act — "1,234.56" should parse correctly with InvariantCulture
        var result = MonetaryAmount.TryCreate("1234.56");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(1234.56m);
    }

    #endregion

    #region Sum Tests

    [Fact]
    public void Sum_SingleItem_ReturnsThatItem()
    {
        var items = new[] { MonetaryAmount.Create(10.00m) };

        var result = MonetaryAmount.Sum(items);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(10.00m);
    }

    [Fact]
    public void Sum_MultipleItems_ReturnsTotal()
    {
        var items = new[]
        {
            MonetaryAmount.Create(10.00m),
            MonetaryAmount.Create(20.50m),
            MonetaryAmount.Create(5.25m),
        };

        var result = MonetaryAmount.Sum(items);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(35.75m);
    }

    [Fact]
    public void Sum_EmptyCollection_ReturnsZero()
    {
        var result = MonetaryAmount.Sum(Array.Empty<MonetaryAmount>());

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(0m);
    }

    [Fact]
    public void Sum_NullCollection_ThrowsArgumentNull()
    {
        var act = () => MonetaryAmount.Sum(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}