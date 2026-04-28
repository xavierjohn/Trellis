using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+14155551234")]
    [InlineData("+442071234567")]
    [InlineData("+33123456789")]
    [InlineData("+8613800138000")]
    [InlineData("+12025551234")]
    public void Can_create_valid_PhoneNumber(string phone)
    {
        // Act
        var result = PhoneNumber.TryCreate(phone);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(phone);
    }

    [Theory]
    [InlineData("+1 415 555 1234", "+14155551234")]
    [InlineData("+1-415-555-1234", "+14155551234")]
    [InlineData("+1 (415) 555-1234", "+14155551234")]
    public void Can_create_PhoneNumber_with_formatting_normalized(string input, string expected)
    {
        // Act
        var result = PhoneNumber.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("555-1234")]
    [InlineData("4155551234")]
    [InlineData("1-800-555-1234")]
    [InlineData("+1234")]
    [InlineData("+0123456789")]
    [InlineData("phone")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cannot_create_invalid_PhoneNumber(string? phone)
    {
        // Act
        var result = PhoneNumber.TryCreate(phone);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public void Cannot_create_empty_PhoneNumber()
    {
        // Act
        var result = PhoneNumber.TryCreate("");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Phone number is required.");
    }

    [Fact]
    public void Cannot_create_invalid_format_PhoneNumber()
    {
        // Act
        var result = PhoneNumber.TryCreate("555-1234");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Phone number must be in E.164 format (e.g., +14155551234).");
    }

    [Fact]
    public void Create_returns_PhoneNumber_for_valid_input()
    {
        // Act
        var phone = PhoneNumber.Create("+14155551234");

        // Assert
        phone.Value.Should().Be("+14155551234");
    }

    [Fact]
    public void Create_throws_for_invalid_input()
    {
        // Act
        Action act = () => PhoneNumber.Create("555-1234");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("+14155551234", "1")]
    [InlineData("+442071234567", "44")]
    [InlineData("+33123456789", "33")]
    [InlineData("+212612345678", "212")]
    [InlineData("+80012345678", "800")]
    public void GetCountryCode_returns_correct_code(string phone, string expectedCountryCode)
    {
        // Arrange
        var phoneNumber = PhoneNumber.TryCreate(phone).Unwrap();

        // Act
        var countryCode = phoneNumber.GetCountryCode();

        // Assert
        countryCode.Should().Be(expectedCountryCode);
    }

    [Fact]
    public void Two_PhoneNumber_with_same_value_should_be_equal()
    {
        // Arrange
        var a = PhoneNumber.TryCreate("+14155551234").Unwrap();
        var b = PhoneNumber.TryCreate("+14155551234").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_PhoneNumber_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = PhoneNumber.TryCreate("+14155551234").Unwrap();
        var b = PhoneNumber.TryCreate("+14155559999").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        PhoneNumber value = PhoneNumber.TryCreate("+14155551234").Unwrap();

        // Act
        string stringValue = value;

        // Assert
        stringValue.Should().Be("+14155551234");
    }

    [Theory]
    [InlineData("+14155551234")]
    [InlineData("+442071234567")]
    public void Can_try_parse_valid_string(string input)
    {
        // Act
        PhoneNumber.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("555-1234")]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        PhoneNumber.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("+14155551234")]
    [InlineData("+442071234567")]
    public void Can_parse_valid_string(string input)
    {
        // Act
        var result = PhoneNumber.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("555-1234")]
    public void Cannot_parse_invalid_format_string(string input)
    {
        // Act
        Action act = () => PhoneNumber.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Phone number must be in E.164 format (e.g., +14155551234).");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = PhoneNumber.TryCreate("+14155551234").Unwrap();
        var expected = JsonSerializer.Serialize("+14155551234");

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize("+14155551234");

        // Act
        var actual = JsonSerializer.Deserialize<PhoneNumber>(json)!;

        // Assert
        actual.Value.Should().Be("+14155551234");
    }

    [Fact]
    public void Cannot_deserialize_invalid_from_JSON()
    {
        // Arrange
        var json = JsonSerializer.Serialize("invalid-phone");

        // Act
        Action act = () => JsonSerializer.Deserialize<PhoneNumber>(json);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Phone number must be in E.164 format (e.g., +14155551234).");
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        // Act
        var result = PhoneNumber.TryCreate("invalid", "contactPhone");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/contactPhone");
    }
}