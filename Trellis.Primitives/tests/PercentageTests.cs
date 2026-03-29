using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;

public class PercentageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(0.5)]
    [InlineData(99.99)]
    public void Can_create_valid_Percentage(decimal value)
    {
        // Act
        var result = Percentage.TryCreate(value);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    [InlineData(100.01)]
    [InlineData(200)]
    public void Cannot_create_out_of_range_Percentage(decimal value)
    {
        // Act
        var result = Percentage.TryCreate(value);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Percentage must be between 0 and 100.");
    }

    [Fact]
    public void Cannot_create_null_Percentage()
    {
        // Act
        var result = Percentage.TryCreate((decimal?)null);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Percentage is required.");
    }

    [Fact]
    public void Create_returns_Percentage_for_valid_value()
    {
        // Act
        var percentage = Percentage.Create(50m);

        // Assert
        percentage.Value.Should().Be(50m);
    }

    [Fact]
    public void Create_throws_for_out_of_range_value()
    {
        // Act
        Action actHigh = () => Percentage.Create(150m);
        Action actLow = () => Percentage.Create(-10m);

        // Assert
        actHigh.Should().Throw<InvalidOperationException>();
        actLow.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Zero_property_returns_zero()
    {
        // Act
        var zero = Percentage.Zero;

        // Assert
        zero.Value.Should().Be(0m);
    }

    [Fact]
    public void Full_property_returns_hundred()
    {
        // Act
        var full = Percentage.Full;

        // Assert
        full.Value.Should().Be(100m);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1)]
    [InlineData(25, 0.25)]
    public void AsFraction_returns_correct_value(decimal percentage, decimal expectedFraction)
    {
        // Arrange
        var pct = Percentage.TryCreate(percentage).Value;

        // Act
        var fraction = pct.AsFraction();

        // Assert
        fraction.Should().Be(expectedFraction);
    }

    [Theory]
    [InlineData(10, 100, 10)]
    [InlineData(50, 200, 100)]
    [InlineData(25, 80, 20)]
    [InlineData(0, 100, 0)]
    [InlineData(100, 50, 50)]
    public void Of_calculates_correct_percentage(decimal percentage, decimal amount, decimal expected)
    {
        // Arrange
        var pct = Percentage.TryCreate(percentage).Value;

        // Act
        var result = pct.Of(amount);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 50)]
    [InlineData(1, 100)]
    [InlineData(0.25, 25)]
    public void FromFraction_creates_correct_percentage(decimal fraction, decimal expectedPercentage)
    {
        // Act
        var result = Percentage.FromFraction(fraction);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expectedPercentage);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void FromFraction_fails_for_out_of_range(decimal fraction)
    {
        // Act
        var result = Percentage.FromFraction(fraction);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void FromFraction_out_of_range_uses_fraction_context()
    {
        // Act
        var result = Percentage.FromFraction(1.5m, "discountRate");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = result.Error.Should().BeOfType<ValidationError>().Subject;
        validation.FieldErrors[0].FieldName.Should().Be("discountRate");
        validation.FieldErrors[0].Details[0].Should().Be("Fraction must be between 0 and 1.");
    }

    [Fact]
    public void Two_Percentage_with_same_value_should_be_equal()
    {
        // Arrange
        var a = Percentage.TryCreate(50m).Value;
        var b = Percentage.TryCreate(50m).Value;

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Percentage_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = Percentage.TryCreate(25m).Value;
        var b = Percentage.TryCreate(75m).Value;

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_decimal()
    {
        // Arrange
        Percentage value = Percentage.TryCreate(50m).Value;

        // Act
        decimal decimalValue = value;

        // Assert
        decimalValue.Should().Be(50m);
    }

    [Fact]
    public void Can_explicitly_cast_from_decimal()
    {
        // Act
        Percentage value = (Percentage)50m;

        // Assert
        value.Value.Should().Be(50m);
    }

    [Fact]
    public void Cannot_explicitly_cast_out_of_range_from_decimal()
    {
        // Act
        Action act = () => { Percentage value = (Percentage)150m; };

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    [InlineData("25.5", 25.5)]
    public void Can_try_parse_valid_string(string input, decimal expected)
    {
        // Act
        Percentage.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        Percentage.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    public void Can_parse_valid_string(string input, decimal expected)
    {
        // Act
        var result = Percentage.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    public void Cannot_parse_out_of_range_string(string input)
    {
        // Act
        Action act = () => Percentage.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Percentage must be between 0 and 100.");
    }

    [Theory]
    [InlineData("abc")]
    public void Cannot_parse_invalid_format_string(string input)
    {
        // Act
        Action act = () => Percentage.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Percentage must be a valid decimal.");
    }

    [Fact]
    public void ToString_returns_percentage_format()
    {
        // Arrange
        var pct = Percentage.TryCreate(50.5m).Value;

        // Act
        var result = pct.ToString();

        // Assert
        result.Should().Be("50.5%");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = Percentage.TryCreate(50m).Value;
        var expected = "\"50%\"";  // Percentage.ToString() adds % suffix

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = "\"50\"";

        // Act
        var actual = JsonSerializer.Deserialize<Percentage>(json)!;

        // Assert
        actual.Value.Should().Be(50m);
    }

    [Fact]
    public void ConvertFromJson_NumericToken()
    {
        // Arrange — deserialize from a JSON number token
        var json = "50";

        // Act
        var actual = JsonSerializer.Deserialize<Percentage>(json)!;

        // Assert
        actual.Value.Should().Be(50m);
    }

    [Fact]
    public void Cannot_deserialize_negative_from_JSON()
    {
        // Arrange
        var json = "\"-5\"";

        // Act
        Action act = () => JsonSerializer.Deserialize<Percentage>(json);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Percentage must be between 0 and 100.");
    }

    [Fact]
    public void Cannot_deserialize_over_100_from_JSON()
    {
        // Arrange
        var json = "\"150\"";

        // Act
        Action act = () => JsonSerializer.Deserialize<Percentage>(json);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Percentage must be between 0 and 100.");
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        // Act
        var result = Percentage.TryCreate(-1m, "discountRate");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("discountRate");
    }

    #region TryCreate from string

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    [InlineData("25.5", 25.5)]
    public void TryCreate_string_valid_returns_success(string input, decimal expected)
    {
        // Act
        var result = Percentage.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Fact]
    public void TryCreate_string_with_percent_suffix_returns_success()
    {
        // Act
        var result = Percentage.TryCreate("50%");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(50m);
    }

    [Fact]
    public void TryCreate_string_with_percent_and_space_returns_success()
    {
        // Act
        var result = Percentage.TryCreate("75 %");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(75m);
    }

    [Fact]
    public void TryCreate_string_null_returns_failure()
    {
        // Act
        var result = Percentage.TryCreate((string?)null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_empty_returns_failure()
    {
        // Act
        var result = Percentage.TryCreate("");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_whitespace_returns_failure()
    {
        // Act
        var result = Percentage.TryCreate("  ");

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
        var result = Percentage.TryCreate(input);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_uses_custom_fieldName()
    {
        // Act
        var result = Percentage.TryCreate((string?)null, "DiscountRate");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("discountRate");
    }

    [Fact]
    public void TryCreate_string_delegates_validation_to_decimal_overload()
    {
        // Act — valid parse but out of range (> 100)
        var result = Percentage.TryCreate("150");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Percentage must be between 0 and 100.");
    }

    #endregion
}