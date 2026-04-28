using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public class SlugTests
{
    [Theory]
    [InlineData("hello-world")]
    [InlineData("my-blog-post")]
    [InlineData("product-123")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("a-b-c-d-e-f")]
    [InlineData("hello-world-2024")]
    [InlineData("test123")]
    public void Can_create_valid_Slug(string slug)
    {
        // Act
        var result = Slug.TryCreate(slug);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(slug);
    }

    [Theory]
    [InlineData("  hello-world  ", "hello-world")]
    [InlineData(" my-slug ", "my-slug")]
    public void Can_create_Slug_with_whitespace_trimmed(string input, string expected)
    {
        // Act
        var result = Slug.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cannot_create_empty_Slug(string? slug)
    {
        // Act
        var result = Slug.TryCreate(slug);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Slug is required.");
    }

    [Theory]
    [InlineData("-hello")]
    [InlineData("world-")]
    [InlineData("-hello-world")]
    [InlineData("hello-world-")]
    [InlineData("-hello-world-")]
    public void Cannot_create_Slug_with_leading_or_trailing_hyphen(string slug)
    {
        // Act
        var result = Slug.TryCreate(slug);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Slug must contain lower-case letters, numbers, and hyphens, without leading/trailing hyphens.");
    }

    [Theory]
    [InlineData("hello--world")]
    [InlineData("my---slug")]
    [InlineData("test--123")]
    public void Cannot_create_Slug_with_consecutive_hyphens(string slug)
    {
        // Act
        var result = Slug.TryCreate(slug);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Slug must contain lower-case letters, numbers, and hyphens, without leading/trailing hyphens.");
    }

    [Theory]
    [InlineData("Hello-World")]
    [InlineData("MY-SLUG")]
    [InlineData("Test-123")]
    [InlineData("heLLo")]
    public void Cannot_create_Slug_with_uppercase_letters(string slug)
    {
        // Act
        var result = Slug.TryCreate(slug);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Slug must contain lower-case letters, numbers, and hyphens, without leading/trailing hyphens.");
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("my_slug")]
    [InlineData("test@123")]
    [InlineData("hello.world")]
    [InlineData("test/path")]
    [InlineData("my slug!")]
    public void Cannot_create_Slug_with_invalid_characters(string slug)
    {
        // Act
        var result = Slug.TryCreate(slug);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Slug must contain lower-case letters, numbers, and hyphens, without leading/trailing hyphens.");
    }

    [Fact]
    public void TryCreate_uses_custom_fieldName()
    {
        // Act
        var result = Slug.TryCreate("", "ArticleSlug");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/articleSlug");
    }

    [Fact]
    public void Create_returns_Slug_for_valid_value()
    {
        // Act
        var slug = Slug.Create("hello-world");

        // Assert
        slug.Value.Should().Be("hello-world");
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        // Act
        Action act = () => Slug.Create("Hello-World");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Two_Slug_with_same_value_should_be_equal()
    {
        // Arrange
        var a = Slug.TryCreate("hello-world").Unwrap();
        var b = Slug.TryCreate("hello-world").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Slug_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = Slug.TryCreate("hello-world").Unwrap();
        var b = Slug.TryCreate("goodbye-world").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        Slug value = Slug.TryCreate("hello-world").Unwrap();

        // Act
        string stringValue = value;

        // Assert
        stringValue.Should().Be("hello-world");
    }

    [Theory]
    [InlineData("hello-world")]
    [InlineData("my-blog-post")]
    [InlineData("test123")]
    public void Can_try_parse_valid_string(string input)
    {
        // Act
        Slug.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("Hello-World")]
    [InlineData("-invalid")]
    [InlineData("hello--world")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        Slug.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("hello-world")]
    [InlineData("my-blog-post")]
    public void Can_parse_valid_string(string input)
    {
        // Act
        var result = Slug.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("Hello-World")]
    [InlineData("-invalid")]
    public void Cannot_parse_invalid_string(string input)
    {
        // Act
        Action act = () => Slug.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Slug must contain lower-case letters, numbers, and hyphens, without leading/trailing hyphens.");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = Slug.TryCreate("hello-world").Unwrap();
        var expected = JsonSerializer.Serialize("hello-world");

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize("hello-world");

        // Act
        var value = JsonSerializer.Deserialize<Slug>(json);

        // Assert
        value.Should().NotBeNull();
        value!.Value.Should().Be("hello-world");
    }

}