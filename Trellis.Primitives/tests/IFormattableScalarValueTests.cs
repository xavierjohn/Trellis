namespace Trellis.Primitives.Tests;

using System.Globalization;

/// <summary>
/// Tests for <see cref="IFormattableScalarValue{TSelf, TPrimitive}"/> interface
/// and its implementations on Age, MonetaryAmount, and Percentage.
/// </summary>
public class IFormattableScalarValueTests
{
    #region Interface implementation verification

    [Fact]
    public void Age_implements_IFormattableScalarValue() =>
        typeof(Age).GetInterfaces().Should().Contain(typeof(IFormattableScalarValue<Age, int>));

    [Fact]
    public void MonetaryAmount_implements_IFormattableScalarValue() =>
        typeof(MonetaryAmount).GetInterfaces().Should().Contain(typeof(IFormattableScalarValue<MonetaryAmount, decimal>));

    [Fact]
    public void Percentage_implements_IFormattableScalarValue() =>
        typeof(Percentage).GetInterfaces().Should().Contain(typeof(IFormattableScalarValue<Percentage, decimal>));

    #endregion

    #region MonetaryAmount — culture-sensitive parsing

    [Fact]
    public void MonetaryAmount_TryCreate_with_german_culture_parses_comma_decimal()
    {
        // Arrange — German uses comma for decimals and period for thousands
        var german = new CultureInfo("de-DE");

        // Act
        var result = MonetaryAmount.TryCreate("1.234,56", german);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(1234.56m);
    }

    [Fact]
    public void MonetaryAmount_TryCreate_with_null_provider_defaults_to_InvariantCulture()
    {
        // Act
        var result = MonetaryAmount.TryCreate("1234.56", null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(1234.56m);
    }

    [Fact]
    public void MonetaryAmount_TryCreate_with_provider_negative_returns_failure()
    {
        // Act
        var result = MonetaryAmount.TryCreate("-100", CultureInfo.InvariantCulture);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void MonetaryAmount_TryCreate_with_provider_null_string_returns_failure()
    {
        // Act
        var result = MonetaryAmount.TryCreate((string?)null, CultureInfo.InvariantCulture);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void MonetaryAmount_TryCreate_with_provider_invalid_string_returns_failure()
    {
        // Act
        var result = MonetaryAmount.TryCreate("not-a-number", CultureInfo.InvariantCulture);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void MonetaryAmount_TryCreate_with_provider_uses_custom_fieldName()
    {
        // Act
        var result = MonetaryAmount.TryCreate((string?)null, CultureInfo.InvariantCulture, "Price");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("price");
    }

    [Fact]
    public void MonetaryAmount_TryCreate_with_french_culture_parses_space_thousands()
    {
        // Arrange — French uses space for thousands and comma for decimals
        var french = new CultureInfo("fr-FR");

        // Act
        var result = MonetaryAmount.TryCreate("1 234,56", french);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(1234.56m);
    }

    #endregion

    #region Age — culture-sensitive parsing

    [Fact]
    public void Age_TryCreate_with_provider_valid_returns_success()
    {
        // Act
        var result = Age.TryCreate("25", CultureInfo.InvariantCulture);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(25);
    }

    [Fact]
    public void Age_TryCreate_with_null_provider_defaults_to_InvariantCulture()
    {
        // Act
        var result = Age.TryCreate("25", null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(25);
    }

    [Fact]
    public void Age_TryCreate_with_provider_null_string_returns_failure()
    {
        // Act
        var result = Age.TryCreate((string?)null, (IFormatProvider?)null);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Age_TryCreate_with_provider_invalid_returns_failure()
    {
        // Act
        var result = Age.TryCreate("abc", CultureInfo.InvariantCulture);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Age_TryCreate_with_provider_out_of_range_returns_failure()
    {
        // Act
        var result = Age.TryCreate("200", CultureInfo.InvariantCulture);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Percentage — culture-sensitive parsing

    [Fact]
    public void Percentage_TryCreate_with_german_culture_parses_comma_decimal()
    {
        // Arrange
        var german = new CultureInfo("de-DE");

        // Act
        var result = Percentage.TryCreate("50,5", german);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(50.5m);
    }

    [Fact]
    public void Percentage_TryCreate_with_null_provider_defaults_to_InvariantCulture()
    {
        // Act
        var result = Percentage.TryCreate("50.5", null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(50.5m);
    }

    [Fact]
    public void Percentage_TryCreate_with_provider_strips_percent_suffix()
    {
        // Act
        var result = Percentage.TryCreate("50.5%", CultureInfo.InvariantCulture);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(50.5m);
    }

    [Fact]
    public void Percentage_TryCreate_with_provider_out_of_range_returns_failure()
    {
        // Act
        var result = Percentage.TryCreate("150", CultureInfo.InvariantCulture);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Percentage_TryCreate_with_provider_null_string_returns_failure()
    {
        // Act
        var result = Percentage.TryCreate((string?)null, (IFormatProvider?)null);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Source-generated RequiredDecimal — culture-sensitive parsing (UnitPrice)

    [Fact]
    public void UnitPrice_implements_IFormattableScalarValue() =>
        typeof(UnitPrice).GetInterfaces().Should().Contain(typeof(IFormattableScalarValue<UnitPrice, decimal>));

    [Fact]
    public void UnitPrice_TryCreate_string_no_provider_uses_InvariantCulture()
    {
        // Act — TryCreate(string?) uses InvariantCulture internally
        var result = UnitPrice.TryCreate("29.99");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(29.99m);
    }

    [Fact]
    public void UnitPrice_TryCreate_string_comma_decimal_with_InvariantCulture_treats_comma_as_thousands()
    {
        // Act — InvariantCulture treats comma as the thousands separator (not decimal),
        // so "29,99" is silently parsed as 2999. This is exactly why the culture-aware
        // TryCreate(string?, IFormatProvider?) overload exists.
        var result = UnitPrice.TryCreate("29,99");

        // Assert — InvariantCulture misinterprets "29,99" as 2999
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(2999m);
    }

    [Fact]
    public void UnitPrice_TryCreate_with_german_culture_parses_comma_decimal()
    {
        // Arrange — German uses comma for decimals
        var german = new CultureInfo("de-DE");

        // Act
        var result = UnitPrice.TryCreate("29,99", german);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(29.99m);
    }

    [Fact]
    public void UnitPrice_TryCreate_with_german_culture_parses_thousands_and_comma_decimal()
    {
        // Arrange — German uses period for thousands, comma for decimals
        var german = new CultureInfo("de-DE");

        // Act
        var result = UnitPrice.TryCreate("1.234,56", german);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(1234.56m);
    }

    #endregion

    #region Source-generated RequiredInt — culture-sensitive parsing (TicketNumber)

    [Fact]
    public void TicketNumber_implements_IFormattableScalarValue() =>
        typeof(TicketNumber).GetInterfaces().Should().Contain(typeof(IFormattableScalarValue<TicketNumber, int>));

    [Fact]
    public void TicketNumber_TryCreate_string_no_provider_uses_InvariantCulture()
    {
        // Act — TryCreate(string?) uses InvariantCulture internally
        var result = TicketNumber.TryCreate("42");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(42);
    }

    [Fact]
    public void TicketNumber_TryCreate_with_german_culture_rejects_thousands_separator()
    {
        // Arrange — German uses period for thousands (e.g. "1.000" = 1000)
        var german = new CultureInfo("de-DE");

        // Act — NumberStyles.Integer does not include AllowThousands,
        // so "1.000" is rejected even with a culture that uses period as group separator.
        var result = TicketNumber.TryCreate("1.000", german);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Source-generated RequiredLong — culture-sensitive parsing (TraceId)

    [Fact]
    public void TraceId_implements_IFormattableScalarValue() =>
        typeof(TraceId).GetInterfaces().Should().Contain(typeof(IFormattableScalarValue<TraceId, long>));

    [Fact]
    public void TraceId_TryCreate_string_no_provider_uses_InvariantCulture()
    {
        // Act — TryCreate(string?) uses InvariantCulture internally
        var result = TraceId.TryCreate("12345");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(12345L);
    }

    [Fact]
    public void TraceId_TryCreate_with_german_culture_rejects_thousands_separator()
    {
        // Arrange — German uses period for thousands (e.g. "12.345" = 12345)
        var german = new CultureInfo("de-DE");

        // Act — NumberStyles.Integer does not include AllowThousands,
        // so "12.345" is rejected even with a culture that uses period as group separator.
        var result = TraceId.TryCreate("12.345", german);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Source-generated RequiredDateTime — culture-sensitive parsing (OrderDate)

    [Fact]
    public void OrderDate_implements_IFormattableScalarValue() =>
        typeof(OrderDate).GetInterfaces().Should().Contain(typeof(IFormattableScalarValue<OrderDate, DateTime>));

    [Fact]
    public void OrderDate_TryCreate_string_no_provider_uses_InvariantCulture()
    {
        // Act — TryCreate(string?) uses InvariantCulture internally
        var result = OrderDate.TryCreate("2026-03-28T12:00:00Z");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Year.Should().Be(2026);
        result.Value.Value.Month.Should().Be(3);
        result.Value.Value.Day.Should().Be(28);
    }

    [Fact]
    public void OrderDate_TryCreate_with_german_culture_parses_german_date_format()
    {
        // Arrange — German date format is DD.MM.YYYY
        var german = new CultureInfo("de-DE");

        // Act
        var result = OrderDate.TryCreate("28.03.2026", german);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Year.Should().Be(2026);
        result.Value.Value.Month.Should().Be(3);
        result.Value.Value.Day.Should().Be(28);
    }

    #endregion

    #region Parse/TryParse delegate to TryCreate

    [Fact]
    public void MonetaryAmount_Parse_delegates_to_formattable_TryCreate()
    {
        // Arrange
        var german = new CultureInfo("de-DE");

        // Act
        var result = MonetaryAmount.Parse("1.234,56", german);

        // Assert
        result.Value.Should().Be(1234.56m);
    }

    [Fact]
    public void MonetaryAmount_TryParse_delegates_to_formattable_TryCreate()
    {
        // Arrange
        var german = new CultureInfo("de-DE");

        // Act
        var success = MonetaryAmount.TryParse("1.234,56", german, out var result);

        // Assert
        success.Should().BeTrue();
        result!.Value.Should().Be(1234.56m);
    }

    [Fact]
    public void Age_Parse_delegates_to_formattable_TryCreate()
    {
        // Act
        var result = Age.Parse("25", CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(25);
    }

    [Fact]
    public void Age_TryParse_delegates_to_formattable_TryCreate()
    {
        // Act
        var success = Age.TryParse("25", CultureInfo.InvariantCulture, out var result);

        // Assert
        success.Should().BeTrue();
        result!.Value.Should().Be(25);
    }

    [Fact]
    public void Percentage_Parse_with_german_culture()
    {
        // Arrange
        var german = new CultureInfo("de-DE");

        // Act
        var result = Percentage.Parse("50,5", german);

        // Assert
        result.Value.Should().Be(50.5m);
    }

    [Fact]
    public void Percentage_TryParse_with_german_culture()
    {
        // Arrange
        var german = new CultureInfo("de-DE");

        // Act
        var success = Percentage.TryParse("50,5", german, out var result);

        // Assert
        success.Should().BeTrue();
        result!.Value.Should().Be(50.5m);
    }

    #endregion
}
