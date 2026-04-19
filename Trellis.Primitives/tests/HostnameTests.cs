using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public class HostnameTests
{
    [Theory]
    [InlineData("example.com")]
    [InlineData("api.github.com")]
    [InlineData("sub.domain.example.org")]
    [InlineData("my-server.example.com")]
    [InlineData("localhost")]
    [InlineData("server1")]
    [InlineData("a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p")]
    [InlineData("123.456.789.012")]
    [InlineData("1-2-3.example.com")]
    public void Can_create_valid_Hostname(string hostname)
    {
        // Act
        var result = Hostname.TryCreate(hostname);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(hostname);
    }

    [Theory]
    [InlineData("  example.com  ", "example.com")]
    [InlineData(" api.github.com ", "api.github.com")]
    public void Can_create_Hostname_with_whitespace_trimmed(string input, string expected)
    {
        // Act
        var result = Hostname.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cannot_create_empty_Hostname(string? hostname)
    {
        // Act
        var result = Hostname.TryCreate(hostname);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Hostname is required.");
    }

    [Theory]
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    [InlineData("example.-com")]
    [InlineData("example.com-")]
    [InlineData("exam ple.com")]
    [InlineData("example..com")]
    [InlineData("exam_ple.com")]
    [InlineData("example.com/path")]
    [InlineData("http://example.com")]
    [InlineData("example.com:8080")]
    [InlineData("@example.com")]
    [InlineData("example.com?query=1")]
    public void Cannot_create_invalid_Hostname(string hostname)
    {
        // Act
        var result = Hostname.TryCreate(hostname);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Hostname must be RFC 1123 compliant.");
    }

    [Fact]
    public void Cannot_create_Hostname_exceeding_255_characters()
    {
        // Arrange - Create a hostname longer than 255 characters
        var longHostname = string.Join(".", Enumerable.Repeat("subdomain", 40));

        // Act
        var result = Hostname.TryCreate(longHostname);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Hostname must be RFC 1123 compliant.");
    }

    [Fact]
    public void Cannot_create_Hostname_with_label_exceeding_63_characters()
    {
        // Arrange - Create a label longer than 63 characters
        var longLabel = new string('a', 64);
        var hostname = $"{longLabel}.example.com";

        // Act
        var result = Hostname.TryCreate(hostname);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Hostname must be RFC 1123 compliant.");
    }

    [Fact]
    public void Can_create_Hostname_with_label_exactly_63_characters()
    {
        // Arrange - Create a label exactly 63 characters
        var maxLabel = new string('a', 63);
        var hostname = $"{maxLabel}.example.com";

        // Act
        var result = Hostname.TryCreate(hostname);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_uses_custom_fieldName()
    {
        // Act
        var result = Hostname.TryCreate("", "ServerName");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].FieldName.Should().Be("serverName");
    }

    [Fact]
    public void Create_returns_Hostname_for_valid_value()
    {
        // Act
        var hostname = Hostname.Create("example.com");

        // Assert
        hostname.Value.Should().Be("example.com");
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        // Act
        Action act = () => Hostname.Create("http://example.com");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Two_Hostname_with_same_value_should_be_equal()
    {
        // Arrange
        var a = Hostname.TryCreate("example.com").Unwrap();
        var b = Hostname.TryCreate("example.com").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Hostname_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = Hostname.TryCreate("example.com").Unwrap();
        var b = Hostname.TryCreate("other.com").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        Hostname value = Hostname.TryCreate("example.com").Unwrap();

        // Act
        string stringValue = value;

        // Assert
        stringValue.Should().Be("example.com");
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("api.github.com")]
    [InlineData("localhost")]
    public void Can_try_parse_valid_string(string input)
    {
        // Act
        Hostname.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result!.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("-example.com")]
    [InlineData("http://example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        Hostname.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("api.github.com")]
    public void Can_parse_valid_string(string input)
    {
        // Act
        var result = Hostname.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("-example.com")]
    [InlineData("http://example.com")]
    public void Cannot_parse_invalid_string(string input)
    {
        // Act
        Action act = () => Hostname.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Hostname must be RFC 1123 compliant.");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = Hostname.TryCreate("example.com").Unwrap();
        var expected = JsonSerializer.Serialize("example.com");

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize("example.com");

        // Act
        var value = JsonSerializer.Deserialize<Hostname>(json);

        // Assert
        value.Should().NotBeNull();
        value!.Value.Should().Be("example.com");
    }

}
