using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public class CountryCodeTests
{
    [Theory]
    [InlineData("US")]
    [InlineData("GB")]
    [InlineData("FR")]
    [InlineData("DE")]
    [InlineData("JP")]
    [InlineData("CA")]
    [InlineData("IN")]
    public void Can_create_valid_CountryCode(string code)
    {
        // Act
        var result = CountryCode.TryCreate(code);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(code.ToUpperInvariant());
    }

    [Theory]
    [InlineData("us", "US")]
    [InlineData("gb", "GB")]
    [InlineData("fr", "FR")]
    [InlineData("Us", "US")]
    [InlineData("gB", "GB")]
    public void Can_create_CountryCode_with_different_case_normalized(string input, string expected)
    {
        // Act
        var result = CountryCode.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(" US ", "US")]
    [InlineData("  GB  ", "GB")]
    public void Can_create_CountryCode_with_whitespace_trimmed(string input, string expected)
    {
        // Act
        var result = CountryCode.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cannot_create_empty_CountryCode(string? code)
    {
        // Act
        var result = CountryCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Country code is required.");
    }

    [Theory]
    [InlineData("U")]
    [InlineData("USA")]
    [InlineData("ABCD")]
    [InlineData("1")]
    [InlineData("123")]
    public void Cannot_create_CountryCode_with_invalid_length(string code)
    {
        // Act
        var result = CountryCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Country code must be an ISO 3166-1 alpha-2 code.");
    }

    [Theory]
    [InlineData("U1")]
    [InlineData("1S")]
    [InlineData("12")]
    [InlineData("A@")]
    [InlineData("U-")]
    public void Cannot_create_CountryCode_with_non_letters(string code)
    {
        // Act
        var result = CountryCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Country code must be an ISO 3166-1 alpha-2 code.");
    }

    [Theory]
    // Inspection finding New-2: char.IsLetter accepts Unicode letters (German umlauts,
    // Greek, Cyrillic, etc). ISO 3166-1 alpha-2 is ASCII-only — must reject these.
    [InlineData("Ää")]   // German letters: char.IsLetter=true, char.IsAsciiLetter=false
    [InlineData("ÖÜ")]
    [InlineData("Αα")]   // Greek alpha
    [InlineData("Ωω")]   // Greek omega
    [InlineData("ЯЯ")]   // Cyrillic
    public void Cannot_create_CountryCode_with_non_ASCII_letters(string code)
    {
        var result = CountryCode.TryCreate(code);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Country code must be an ISO 3166-1 alpha-2 code.");
    }

    [Fact]
    public void TryCreate_uses_custom_fieldName()
    {
        // Act
        var result = CountryCode.TryCreate("", "Country");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/country");
    }

    [Fact]
    public void Create_returns_CountryCode_for_valid_value()
    {
        // Act
        var code = CountryCode.Create("US");

        // Assert
        code.Value.Should().Be("US");
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        // Act
        Action act = () => CountryCode.Create("USA");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Two_CountryCode_with_same_value_should_be_equal()
    {
        // Arrange
        var a = CountryCode.TryCreate("US").Unwrap();
        var b = CountryCode.TryCreate("us").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_CountryCode_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = CountryCode.TryCreate("US").Unwrap();
        var b = CountryCode.TryCreate("GB").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        CountryCode value = CountryCode.TryCreate("US").Unwrap();

        // Act
        string stringValue = value;

        // Assert
        stringValue.Should().Be("US");
    }

    [Theory]
    [InlineData("US", "US")]
    [InlineData("gb", "GB")]
    [InlineData("Fr", "FR")]
    public void Can_try_parse_valid_string(string input, string expected)
    {
        // Act
        CountryCode.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("USA")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        CountryCode.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("US", "US")]
    [InlineData("gb", "GB")]
    public void Can_parse_valid_string(string input, string expected)
    {
        // Act
        var result = CountryCode.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("USA")]
    [InlineData("U")]
    public void Cannot_parse_invalid_string(string input)
    {
        // Act
        Action act = () => CountryCode.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Country code must be an ISO 3166-1 alpha-2 code.");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = CountryCode.TryCreate("US").Unwrap();
        var expected = JsonSerializer.Serialize("US");

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize("US");

        // Act
        var value = JsonSerializer.Deserialize<CountryCode>(json);

        // Assert
        value.Should().NotBeNull();
        value!.Value.Should().Be("US");
    }

}