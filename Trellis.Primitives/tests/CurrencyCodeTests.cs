using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public class CurrencyCodeTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("JPY")]
    [InlineData("BHD")]
    public void Can_create_valid_CurrencyCode(string code)
    {
        // Act
        var result = CurrencyCode.TryCreate(code);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(code.ToUpperInvariant());
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData("eur", "EUR")]
    [InlineData("Gbp", "GBP")]
    public void CurrencyCode_is_uppercase(string input, string expected)
    {
        // Act
        var result = CurrencyCode.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cannot_create_empty_CurrencyCode(string? code)
    {
        // Act
        var result = CurrencyCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Currency code is required.");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U")]
    public void Cannot_create_CurrencyCode_with_wrong_length(string code)
    {
        // Act
        var result = CurrencyCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Currency code must be a 3-letter ISO 4217 code.");
    }

    [Theory]
    [InlineData("US1")]
    [InlineData("12 3")]
    [InlineData("U$D")]
    public void Cannot_create_CurrencyCode_with_non_letters(string code)
    {
        // Act
        var result = CurrencyCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Currency code must be a 3-letter ISO 4217 code.");
    }

    [Fact]
    public void Two_CurrencyCode_with_same_value_should_be_equal()
    {
        // Arrange
        var a = CurrencyCode.TryCreate("USD").Unwrap();
        var b = CurrencyCode.TryCreate("usd").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Can_parse_valid_CurrencyCode()
    {
        // Act
        var result = CurrencyCode.Parse("USD", CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be("USD");
    }

    [Fact]
    public void Create_returns_CurrencyCode_for_valid_input()
    {
        // Act
        var code = CurrencyCode.Create("EUR");

        // Assert
        code.Value.Should().Be("EUR");
    }

    [Fact]
    public void Create_throws_for_invalid_code()
    {
        // Act
        Action act = () => CurrencyCode.Create("INVALID");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create CurrencyCode:*");
    }

    [Fact]
    public void Create_throws_for_empty_code()
    {
        // Act
        Action act = () => CurrencyCode.Create("");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create CurrencyCode: Currency code is required.");
    }

    [Fact]
    public void Cannot_parse_invalid_CurrencyCode()
    {
        // Act
        Action act = () => CurrencyCode.Parse("INVALID", CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Currency code must be a 3-letter ISO 4217 code.");
    }

    [Fact]
    public void Can_try_parse_valid_CurrencyCode()
    {
        // Act
        CurrencyCode.TryParse("EUR", CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be("EUR");
    }

    [Fact]
    public void Cannot_try_parse_invalid_CurrencyCode()
    {
        // Act
        CurrencyCode.TryParse("XX", CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var code = CurrencyCode.TryCreate("USD").Unwrap();
        var expected = "\"USD\"";

        // Act
        var actual = JsonSerializer.Serialize(code);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = "\"EUR\"";

        // Act
        var actual = JsonSerializer.Deserialize<CurrencyCode>(json)!;

        // Assert
        actual.Value.Should().Be("EUR");
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        // Act
        var result = CurrencyCode.TryCreate("", "paymentCurrency");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].FieldName.Should().Be("paymentCurrency");
    }
}
