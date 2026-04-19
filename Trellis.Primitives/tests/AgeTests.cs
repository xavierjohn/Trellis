using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public class AgeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(65)]
    [InlineData(100)]
    [InlineData(150)]
    public void Can_create_valid_Age(int ageValue)
    {
        // Act
        var result = Age.TryCreate(ageValue);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(ageValue);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    [InlineData(-100)]
    public void Cannot_create_negative_Age(int ageValue)
    {
        // Act
        var result = Age.TryCreate(ageValue);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Age must be non-negative.");
    }

    [Theory]
    [InlineData(151)]
    [InlineData(200)]
    [InlineData(999)]
    public void Cannot_create_unrealistic_Age(int ageValue)
    {
        // Act
        var result = Age.TryCreate(ageValue);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Age is unrealistically high.");
    }

    [Fact]
    public void TryCreate_uses_custom_fieldName()
    {
        // Act
        var result = Age.TryCreate(-5, "PersonAge");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].FieldName.Should().Be("personAge");
    }

    [Fact]
    public void Create_returns_Age_for_valid_value()
    {
        // Act
        var age = Age.Create(30);

        // Assert
        age.Value.Should().Be(30);
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        // Act
        Action act = () => Age.Create(-5);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Two_Age_with_same_value_should_be_equal()
    {
        // Arrange
        var a = Age.TryCreate(25).Unwrap();
        var b = Age.TryCreate(25).Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Age_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = Age.TryCreate(25).Unwrap();
        var b = Age.TryCreate(30).Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_int()
    {
        // Arrange
        Age value = Age.TryCreate(25).Unwrap();

        // Act
        int intValue = value;

        // Assert
        intValue.Should().Be(25);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("25", 25)]
    [InlineData("150", 150)]
    public void Can_try_parse_valid_string(string input, int expected)
    {
        // Act
        Age.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("151")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        Age.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("25", 25)]
    [InlineData("150", 150)]
    public void Can_parse_valid_string(string input, int expected)
    {
        // Act
        var result = Age.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("-1", "Age must be non-negative.")]
    [InlineData("151", "Age is unrealistically high.")]
    public void Cannot_parse_invalid_string(string input, string expectedMessage)
    {
        // Act
        Action act = () => Age.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage(expectedMessage);
    }

    [Fact]
    public void Cannot_parse_non_integer_string()
    {
        // Act
        Action act = () => Age.Parse("abc", CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Age must be a valid integer.");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = Age.TryCreate(25).Unwrap();

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert — numeric value objects serialize as JSON numbers
        actual.Should().Be("25");
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange — deserialize from a JSON string token
        var json = JsonSerializer.Serialize("25");

        // Act
        var value = JsonSerializer.Deserialize<Age>(json);

        // Assert
        value.Should().NotBeNull();
        value!.Value.Should().Be(25);
    }

    [Fact]
    public void ConvertFromJson_NumericToken()
    {
        // Arrange — deserialize from a JSON number token
        var json = "25";

        // Act
        var value = JsonSerializer.Deserialize<Age>(json);

        // Assert
        value.Should().NotBeNull();
        value!.Value.Should().Be(25);
    }

    #region TryCreate from string

    [Theory]
    [InlineData("0", 0)]
    [InlineData("25", 25)]
    [InlineData("150", 150)]
    public void TryCreate_string_valid_returns_success(string input, int expected)
    {
        // Act
        var result = Age.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Fact]
    public void TryCreate_string_null_returns_failure()
    {
        // Act
        var result = Age.TryCreate((string?)null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_empty_returns_failure()
    {
        // Act
        var result = Age.TryCreate("");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_whitespace_returns_failure()
    {
        // Act
        var result = Age.TryCreate("  ");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12.5")]
    [InlineData("not-a-number")]
    public void TryCreate_string_invalid_format_returns_failure(string input)
    {
        // Act
        var result = Age.TryCreate(input);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_string_uses_custom_fieldName()
    {
        // Act
        var result = Age.TryCreate((string?)null, "PersonAge");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].FieldName.Should().Be("personAge");
    }

    [Fact]
    public void TryCreate_string_delegates_validation_to_int_overload()
    {
        // Act — valid parse but invalid age (> 150)
        var result = Age.TryCreate("200");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Age is unrealistically high.");
    }

    #endregion

}
