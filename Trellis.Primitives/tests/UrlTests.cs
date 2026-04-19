using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public class UrlTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://www.example.com/path", "https://www.example.com/path")]
    [InlineData("https://api.example.com/v1/users", "https://api.example.com/v1/users")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    [InlineData("https://example.com/search?q=test", "https://example.com/search?q=test")]
    [InlineData("https://example.com:443/path", "https://example.com/path")]
    public void Can_create_valid_Url(string url, string expected)
    {
        // Act
        var result = Url.TryCreate(url);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("file:///path/to/file")]
    [InlineData("mailto:test@example.com")]
    [InlineData("javascript:alert('xss')")]
    [InlineData("//example.com")]
    [InlineData("example.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cannot_create_invalid_Url(string? url)
    {
        // Act
        var result = Url.TryCreate(url);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public void Cannot_create_empty_Url()
    {
        // Act
        var result = Url.TryCreate("");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("URL is required.");
    }

    [Fact]
    public void Cannot_create_relative_Url()
    {
        // Act
        var result = Url.TryCreate("/path/to/resource");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        // Note: On some platforms, Uri.TryCreate treats "/path" as absolute with "file" scheme
        // So we check for either error message depending on platform behavior
        validation.Fields[0].Detail.Should().Match(e =>
            e == "URL must be a valid absolute HTTP or HTTPS URL." ||
            e == "URL must use HTTP or HTTPS scheme.");
    }

    [Fact]
    public void Cannot_create_non_http_scheme_Url()
    {
        // Act
        var result = Url.TryCreate("ftp://example.com/file");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("URL must use HTTP or HTTPS scheme.");
    }

    [Theory]
    [InlineData("https://example.com", "https")]
    [InlineData("http://example.com", "http")]
    public void Scheme_returns_correct_value(string url, string expectedScheme)
    {
        // Arrange
        var urlObj = Url.TryCreate(url).Unwrap();

        // Act & Assert
        urlObj.Scheme.Should().Be(expectedScheme);
    }

    [Theory]
    [InlineData("https://example.com", "example.com")]
    [InlineData("https://www.example.com", "www.example.com")]
    [InlineData("https://api.example.com/v1", "api.example.com")]
    public void Host_returns_correct_value(string url, string expectedHost)
    {
        // Arrange
        var urlObj = Url.TryCreate(url).Unwrap();

        // Act & Assert
        urlObj.Host.Should().Be(expectedHost);
    }

    [Theory]
    [InlineData("https://example.com", 443)]
    [InlineData("http://example.com", 80)]
    [InlineData("https://example.com:8443", 8443)]
    [InlineData("http://localhost:8080", 8080)]
    public void Port_returns_correct_value(string url, int expectedPort)
    {
        // Arrange
        var urlObj = Url.TryCreate(url).Unwrap();

        // Act & Assert
        urlObj.Port.Should().Be(expectedPort);
    }

    [Theory]
    [InlineData("https://example.com", "/")]
    [InlineData("https://example.com/path", "/path")]
    [InlineData("https://example.com/api/v1/users", "/api/v1/users")]
    public void Path_returns_correct_value(string url, string expectedPath)
    {
        // Arrange
        var urlObj = Url.TryCreate(url).Unwrap();

        // Act & Assert
        urlObj.Path.Should().Be(expectedPath);
    }

    [Theory]
    [InlineData("https://example.com", "")]
    [InlineData("https://example.com/search?q=test", "?q=test")]
    [InlineData("https://example.com/path?a=1&b=2", "?a=1&b=2")]
    public void Query_returns_correct_value(string url, string expectedQuery)
    {
        // Arrange
        var urlObj = Url.TryCreate(url).Unwrap();

        // Act & Assert
        urlObj.Query.Should().Be(expectedQuery);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", false)]
    public void IsSecure_returns_correct_value(string url, bool expectedIsSecure)
    {
        // Arrange
        var urlObj = Url.TryCreate(url).Unwrap();

        // Act & Assert
        urlObj.IsSecure.Should().Be(expectedIsSecure);
    }

    [Fact]
    public void ToUri_returns_valid_Uri()
    {
        // Arrange
        var url = Url.TryCreate("https://example.com/path").Unwrap();

        // Act
        var uri = url.ToUri();

        // Assert
        uri.Should().BeOfType<Uri>();
        uri.AbsoluteUri.Should().Be("https://example.com/path");
    }

    [Fact]
    public void Two_Url_with_same_value_should_be_equal()
    {
        // Arrange
        var a = Url.TryCreate("https://example.com").Unwrap();
        var b = Url.TryCreate("https://example.com").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Url_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = Url.TryCreate("https://example.com").Unwrap();
        var b = Url.TryCreate("https://other.com").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        Url value = Url.TryCreate("https://example.com").Unwrap();

        // Act
        string stringValue = value;

        // Assert
        stringValue.Should().Be("https://example.com/");
    }

    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void Can_try_parse_valid_string(string input, string expected)
    {
        // Act
        Url.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        Url.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void Can_parse_valid_string(string input, string expected)
    {
        // Act
        var result = Url.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("not-a-url")]
    public void Cannot_parse_invalid_format_string(string input)
    {
        // Act
        Action act = () => Url.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("URL must be a valid absolute HTTP or HTTPS URL.");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = Url.TryCreate("https://example.com").Unwrap();
        var expected = JsonSerializer.Serialize("https://example.com/");

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize("https://example.com");

        // Act
        var actual = JsonSerializer.Deserialize<Url>(json)!;

        // Assert
        actual.Value.Should().Be("https://example.com/");
    }

    [Fact]
    public void Cannot_deserialize_invalid_from_JSON()
    {
        // Arrange
        var json = JsonSerializer.Serialize("not-a-url");

        // Act
        Action act = () => JsonSerializer.Deserialize<Url>(json);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("URL must be a valid absolute HTTP or HTTPS URL.");
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        // Act
        var result = Url.TryCreate("invalid", "webhookUrl");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/webhookUrl");
    }

    [Fact]
    public void Create_returns_Url_for_valid_value()
    {
        // Act
        var url = Url.Create("https://example.com");

        // Assert
        url.Value.Should().Be("https://example.com/");
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        // Act
        Action act = () => Url.Create("not-a-url");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }
}
