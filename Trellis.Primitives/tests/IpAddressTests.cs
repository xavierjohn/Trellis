using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Net;
using System.Text.Json;
using Trellis.Testing;

public class IpAddressTests
{
    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    public void Can_create_valid_IPv4_IpAddress(string ipAddress)
    {
        // Act
        var result = IpAddress.TryCreate(ipAddress);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(ipAddress);
    }

    [Theory]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334")]
    [InlineData("2001:db8:85a3::8a2e:370:7334")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:192.168.1.1")]
    public void Can_create_valid_IPv6_IpAddress(string ipAddress)
    {
        // Act
        var result = IpAddress.TryCreate(ipAddress);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().ToIPAddress().Should().NotBeNull();
    }

    [Theory]
    [InlineData("  192.168.1.1  ", "192.168.1.1")]
    [InlineData(" 8.8.8.8 ", "8.8.8.8")]
    public void Can_create_IpAddress_with_whitespace_trimmed(string input, string expected)
    {
        // Act
        var result = IpAddress.TryCreate(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cannot_create_empty_IpAddress(string? ipAddress)
    {
        // Act
        var result = IpAddress.TryCreate(ipAddress);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("IP address is required.");
    }

    [Theory]
    [InlineData("256.1.1.1")]
    [InlineData("192.168.1.1.1")]
    [InlineData("192.168.-1.1")]
    [InlineData("192.168.abc.1")]
    [InlineData("not-an-ip")]
    [InlineData("example.com")]
    [InlineData("192.168.1.1/24")]
    [InlineData("gggg::1")]
    [InlineData("2001:0db8:85a3::8a2e:370g:7334")]
    public void Cannot_create_invalid_IpAddress(string ipAddress)
    {
        // Act
        var result = IpAddress.TryCreate(ipAddress);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("IP address must be a valid IPv4 or IPv6.");
    }

    [Fact]
    public void ToIPAddress_returns_underlying_IPAddress()
    {
        // Arrange
        var ipAddress = IpAddress.TryCreate("192.168.1.1").Unwrap();

        // Act
        var result = ipAddress.ToIPAddress();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<IPAddress>();
        result.ToString().Should().Be("192.168.1.1");
    }

    [Fact]
    public void TryCreate_uses_custom_fieldName()
    {
        // Act
        var result = IpAddress.TryCreate("", "ClientIP");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/clientIP");
    }

    [Fact]
    public void Create_returns_IpAddress_for_valid_value()
    {
        // Act
        var ipAddress = IpAddress.Create("192.168.1.1");

        // Assert
        ipAddress.Value.Should().Be("192.168.1.1");
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        // Act
        Action act = () => IpAddress.Create("invalid-ip");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Two_IpAddress_with_same_value_should_be_equal()
    {
        // Arrange
        var a = IpAddress.TryCreate("192.168.1.1").Unwrap();
        var b = IpAddress.TryCreate("192.168.1.1").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_IpAddress_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = IpAddress.TryCreate("192.168.1.1").Unwrap();
        var b = IpAddress.TryCreate("192.168.1.2").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        IpAddress value = IpAddress.TryCreate("192.168.1.1").Unwrap();

        // Act
        string stringValue = value;

        // Assert
        stringValue.Should().Be("192.168.1.1");
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    public void Can_try_parse_valid_string(string input)
    {
        // Act
        IpAddress.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeTrue();

        // Assert
        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("256.1.1.1")]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData(null)]
    public void Cannot_try_parse_invalid_string(string? input)
    {
        // Act
        IpAddress.TryParse(input, CultureInfo.InvariantCulture, out var result)
            .Should().BeFalse();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("::1")]
    public void Can_parse_valid_string(string input)
    {
        // Act
        var result = IpAddress.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("256.1.1.1")]
    [InlineData("not-an-ip")]
    public void Cannot_parse_invalid_string(string input)
    {
        // Act
        Action act = () => IpAddress.Parse(input, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("IP address must be a valid IPv4 or IPv6.");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var value = IpAddress.TryCreate("192.168.1.1").Unwrap();
        var expected = JsonSerializer.Serialize("192.168.1.1");

        // Act
        var actual = JsonSerializer.Serialize(value);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize("192.168.1.1");

        // Act
        var value = JsonSerializer.Deserialize<IpAddress>(json);

        // Assert
        value.Should().NotBeNull();
        value!.Value.Should().Be("192.168.1.1");
    }

}
