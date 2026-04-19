using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Xunit;
using Trellis.Testing;

public class EmailAddressTests
{
    [Theory]
    [MemberData(nameof(GetBadEmailAddresses))]
    public void Cannot_create_invalid_email(string? email)
    {
        // Arrange & Act
        var result = EmailAddress.TryCreate(email, "school email");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("school email"), "validation.error") { Detail = "Email address is not valid." })));
    }

    [Theory]
    [MemberData(nameof(GetGoodEmailAddresses))]
    public void Can_create_valid_email(string? email)
    {
        // Arrange
        var result = EmailAddress.TryCreate(email, "school email");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().BeOfType<EmailAddress>();
    }

    [Theory]
    [MemberData(nameof(GetGoodEmailAddresses))]
    public void Can_create_EmailAddress_try_parsing_valid_string(string? strEmail)
    {
        // Arrange & Act
        EmailAddress.TryParse(strEmail, null, out var email)

        // Assert
        .Should().BeTrue();
        email.Should().BeOfType<EmailAddress>();
        email!.ToString(CultureInfo.InvariantCulture).Should().Be(strEmail);
    }

    [Theory]
    [MemberData(nameof(GetBadEmailAddresses))]
    public void Cannot_create_EmailAddress_try_parsing_invalid_string(string? strEmail)
    {
        // Arrange & Act
        EmailAddress.TryParse(strEmail, null, out var email)

        // Assert
        .Should().BeFalse();
        email.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(GetGoodEmailAddresses))]
    public void Can_create_EmailAddress_parsing_valid_string(string? strEmail)
    {
        // Arrange & Act
        var email = EmailAddress.Parse(strEmail, null);

        // Assert
        email.Should().BeOfType<EmailAddress>();
        email.ToString(CultureInfo.InvariantCulture).Should().Be(strEmail);
    }

    [Fact]
    public void Cannot_create_EmailAddress_parsing_null_string()
    {
        // Arrange
        string? str = null;
        // Act
        Action act = () => EmailAddress.Parse(str, null);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Email address is not valid.");
    }

    [Theory]
    [MemberData(nameof(GetBadEmailAddresses))]
    public void Cannot_create_EmailAddress_parsing_invalid_string(string? email)
    {
        // Arrange & Act
        Action act = () => EmailAddress.Parse(email, null);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Email address is not valid.");
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        EmailAddress email = EmailAddress.TryCreate("chris@somewhere.com").Unwrap();
        string primEmail = "chris@somewhere.com";

        var expected = JsonSerializer.Serialize(primEmail);

        // Act
        var actual = JsonSerializer.Serialize(email);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        string primEmail = "chris@somewhere.com";

        var json = JsonSerializer.Serialize(primEmail);

        // Act
        EmailAddress actual = JsonSerializer.Deserialize<EmailAddress>(json)!;

        // Assert
        actual.Value.Should().Be(primEmail);
    }

    [Theory]
    [MemberData(nameof(GetBadEmailAddresses))]
    public void Cannot_create_EmailAddress_from_parsing_invalid_string_in_JSON(string? email)
    {
        // Arrange
        var json = JsonSerializer.Serialize(email);

        // Act
        Action act = () => JsonSerializer.Deserialize<EmailAddress>(json);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Email address is not valid.");
    }

    public static TheoryData<string?> GetGoodEmailAddresses() =>
    [
        "xavier@somewhere.com",
        "0987654321@example.com",
        "_______@email.com"
    ];

    public static TheoryData<string?> GetBadEmailAddresses() =>
    [
        string.Empty,
        "xavier",
        "xavier@",
        "@com",
        "@#@@##@%^%#$@#$@#.com",
        "John Doe <example@email.com>",
        "CAT…123@email.com"
    ];

    [Fact]
    public void Create_returns_EmailAddress_for_valid_value()
    {
        // Act
        var email = EmailAddress.Create("xavier@somewhere.com");

        // Assert
        email.Value.Should().Be("xavier@somewhere.com");
    }

    [Fact]
    public void Create_throws_for_invalid_value()
    {
        // Act
        Action act = () => EmailAddress.Create("invalid");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cannot_create_EmailAddress_from_null()
    {
        // Act
        var result = EmailAddress.TryCreate(null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public void TryCreate_uses_custom_fieldName()
    {
        // Act
        var result = EmailAddress.TryCreate("invalid", "school email");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/school email");
    }

    [Fact]
    public void Two_EmailAddress_with_same_value_should_be_equal()
    {
        // Arrange
        var a = EmailAddress.TryCreate("xavier@somewhere.com").Unwrap();
        var b = EmailAddress.TryCreate("xavier@somewhere.com").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_EmailAddress_with_different_value_should_not_be_equal()
    {
        // Arrange
        var a = EmailAddress.TryCreate("xavier@somewhere.com").Unwrap();
        var b = EmailAddress.TryCreate("other@somewhere.com").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        EmailAddress email = EmailAddress.TryCreate("xavier@somewhere.com").Unwrap();

        // Act
        string stringValue = email;

        // Assert
        stringValue.Should().Be("xavier@somewhere.com");
    }
}
