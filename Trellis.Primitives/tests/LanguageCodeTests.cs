using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public class LanguageCodeTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("es")]
    [InlineData("zh")]
    [InlineData("ar")]
    public void Can_create_valid_LanguageCode(string code)
    {
        // Act
        var result = LanguageCode.TryCreate(code);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(code.ToLowerInvariant());
    }

    [Theory]
    [InlineData("EN", "en")]
    [InlineData("FR", "fr")]
    [InlineData("DE", "de")]
    [InlineData("En", "en")]
    [InlineData("fR", "fr")]
    public void Can_create_LanguageCode_with_different_case_normalized(string input, string expected)
    {
        // Act
        var result = LanguageCode.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(" en ", "en")]
    [InlineData("  fr  ", "fr")]
    public void Can_create_LanguageCode_with_whitespace_trimmed(string input, string expected)
    {
        // Act
        var result = LanguageCode.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cannot_create_empty_LanguageCode(string? code)
    {
        // Act
        var result = LanguageCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Language code is required.");
    }

    [Theory]
    [InlineData("e")]
    [InlineData("eng")]
    [InlineData("engl")]
    [InlineData("1")]
    [InlineData("123")]
    public void Cannot_create_LanguageCode_with_invalid_length(string code)
    {
        // Act
        var result = LanguageCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Language code must be an ISO 639-1 alpha-2 code.");
    }

    [Theory]
    [InlineData("e1")]
    [InlineData("1n")]
    [InlineData("12")]
    [InlineData("e@")]
    [InlineData("e-")]
    public void Cannot_create_LanguageCode_with_non_letters(string code)
    {
        // Act
        var result = LanguageCode.TryCreate(code);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Language code must be an ISO 639-1 alpha-2 code.");
    }

    [Theory]
    // Inspection finding New-2: ISO 639-1 is ASCII-only.
    [InlineData("Ää")]
    [InlineData("Öö")]
    [InlineData("αβ")]
    [InlineData("ru")]   // 'ru' is a real ISO 639-1 code — keep this OUT of the asciilettering test (it's valid ASCII)
    public void Non_ASCII_or_known_lowercase_LanguageCode_handling(string code)
    {
        // 'ru' is valid; the rest are invalid Unicode-letter inputs. This test verifies
        // the ASCII-only contract (rejecting Greek/German letters) and the lowercase
        // normalization remains intact for valid input.
        var result = LanguageCode.TryCreate(code);

        if (code == "ru")
        {
            result.IsSuccess.Should().BeTrue();
            result.Unwrap().Value.Should().Be("ru");
        }
        else
        {
            result.IsFailure.Should().BeTrue();
            var validation = (Error.UnprocessableContent)result.UnwrapError();
            validation.Fields[0].Detail.Should().Be("Language code must be an ISO 639-1 alpha-2 code.");
        }
    }

    [Fact]
    public void TryCreate_uses_custom_fieldName()
    {
        // Act
        var result = LanguageCode.TryCreate("", "Language");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/language");
    }

    [Fact]
    public void Create_returns_LanguageCode_for_valid_value()
    {
        // Act
        var code = LanguageCode.Create("en");

        // Assert
        code.Value.Should().Be("en");
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        // Act
        Action act = () => LanguageCode.Create("eng");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Two_LanguageCode_with_same_value_should_be_equal()
    {
        // Arrange
        var a = LanguageCode.TryCreate("en").Unwrap();
        var b = LanguageCode.TryCreate("EN").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_LanguageCode_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = LanguageCode.TryCreate("en").Unwrap();
        var b = LanguageCode.TryCreate("fr").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        LanguageCode value = LanguageCode.TryCreate("en").Unwrap();

        // Act
        string stringValue = value;

        // Assert
        stringValue.Should().Be("en");
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("FR", "fr")]
    [InlineData("De", "de")]
    public void Can_try_parse_valid_string(string input, string expected)
    {
        // Act
        LanguageCode.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        LanguageCode.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("FR", "fr")]
    public void Can_parse_valid_string(string input, string expected)
    {
        // Act
        var result = LanguageCode.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("e")]
    public void Cannot_parse_invalid_string(string input)
    {
        // Act
        Action act = () => LanguageCode.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Language code must be an ISO 639-1 alpha-2 code.");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = LanguageCode.TryCreate("en").Unwrap();
        var expected = JsonSerializer.Serialize("en");

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize("en");

        // Act
        var value = JsonSerializer.Deserialize<LanguageCode>(json);

        // Assert
        value.Should().NotBeNull();
        value!.Value.Should().Be("en");
    }

}