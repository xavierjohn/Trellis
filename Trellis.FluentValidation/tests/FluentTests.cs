namespace Trellis.FluentValidation.Tests;

using global::FluentValidation;
using Trellis;
using Trellis.Testing;
using Xunit;

public class FluentTests
{
    private const string StrongPassword = "P@ssw0rd";

    [Fact]
    public void Can_create_user()
    {
        // Act
        var rUser = User.TryCreate(
            FirstName.TryCreate("John").Value,
            LastName.TryCreate("Doe").Value,
            EmailAddress.TryCreate("xavier@somewhere.com").Value,
            StrongPassword);

        // Assert
        rUser.Should().BeSuccess()
            .Which.Should().Match<User>(u =>
                u.FirstName.Value == "John" &&
                u.LastName.Value == "Doe" &&
                u.Email.Value == "xavier@somewhere.com" &&
                u.Password == StrongPassword);
    }

    [Fact]
    public void Cannot_create_user_with_invalid_parameters()
    {
        // Arrange
        FirstName firstName = default!;
        LastName lastName = default!;
        EmailAddress email = default!;

        // Act
        var rUser = User.TryCreate(firstName, lastName, email, StrongPassword);

        // Assert
        rUser.Should().BeFailureOfType<ValidationError>()
            .Which.Should()
            .HaveFieldCount(3)
            .And.HaveFieldError("FirstName")
            .And.HaveFieldError("LastName")
            .And.HaveFieldError("Email");
    }

    [Fact]
    public void Cannot_create_user_with_invalid_parameters_variation()
    {
        // Arrange
        FirstName firstName = default!;
        LastName lastName = default!;
        EmailAddress email = EmailAddress.TryCreate("xavier@somewhere.com").Value;

        // Act
        var rUser = User.TryCreate(firstName, lastName, email, "WeakPassword");

        // Assert
        rUser.Should().BeFailureOfType<ValidationError>()
            .Which.Should()
            .HaveFieldCount(3)
            .And.HaveFieldError("FirstName")
            .And.HaveFieldError("LastName")
            .And.HaveFieldErrorWithDetail("Password", "Password must contain at least one number.");
    }

    [Theory]
    [InlineData("98052", true, null)]
    [InlineData("98052-1234", true, null)]
    [InlineData("98052-12345", false, "'zip Code' is not in the correct format.")]
    [InlineData("98052-123", false, "'zip Code' is not in the correct format.")]
    [InlineData("98052-1234-1234", false, "'zip Code' is not in the correct format.")]
    [InlineData(null, false, "'zipCode' must not be empty.")]
    public void Validate_zipcode(string? strZip, bool success, string? errorMessage)
    {
        // Act
        var result = ZipCode.TryCreate(strZip);

        // Assert
        if (success)
        {
            result.Should().BeSuccess()
                .Which.Value.Should().Be(strZip);
        }
        else
        {
            result.Should().BeFailureOfType<ValidationError>()
                .Which.Should()
                .HaveFieldErrorWithDetail("zipCode", errorMessage!);
        }
    }

    [Fact]
    public void Validate_null_value()
    {
        // Arrange
        string? alias = null;
        InlineValidator<string?> validator = new()
        {
            v => v.RuleFor(x => x)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
        };

        // Act
        var result = validator.ValidateToResult(alias);

        // Assert
        result.Should().BeFailureOfType<ValidationError>()
            .Which.Should()
            .HaveFieldErrorWithDetail("alias", "'alias' must not be empty.");
    }

    [Fact]
    public void Validate_null_value_custom_message()
    {
        // Arrange
        string? alias = null;
        InlineValidator<string?> validator = new()
        {
            v => v.RuleFor(x => x)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
        };

        // Act
        var result = validator.ValidateToResult(alias, "Alias", "Hello There");

        // Assert
        result.Should().BeFailureOfType<ValidationError>()
            .Which.Should()
            .HaveFieldErrorWithDetail("Alias", "Hello There");
    }

    [Fact]
    public void ValidateToResult_WithNullValidator_ThrowsArgumentNullException()
    {
        IValidator<string> validator = null!;

        var act = () => validator.ValidateToResult("value");

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "validator");
    }

    #region ValidateToResultAsync Tests

    [Fact]
    public async Task ValidateToResultAsync_WithValidValue_ReturnsSuccess()
    {
        // Arrange
        var validator = new InlineValidator<string>
        {
            v => v.RuleFor(x => x).NotEmpty().MinimumLength(3)
        };

        // Act
        var result = await validator.ValidateToResultAsync("valid", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("valid");
    }

    [Fact]
    public async Task ValidateToResultAsync_WithInvalidValue_ReturnsFailure()
    {
        // Arrange
        var validator = new InlineValidator<string>
        {
            v => v.RuleFor(x => x).NotEmpty().MinimumLength(5)
        };

        // Act
        var result = await validator.ValidateToResultAsync("ab", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task ValidateToResultAsync_WithNullValue_ReturnsFailure()
    {
        // Arrange
        string? value = null;
        var validator = new InlineValidator<string?>
        {
            v => v.RuleFor(x => x).NotEmpty()
        };

        // Act
        var result = await validator.ValidateToResultAsync(value, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailureOfType<ValidationError>()
            .Which.Should()
            .HaveFieldErrorWithDetail("value", "'value' must not be empty.");
    }

    [Fact]
    public async Task ValidateToResultAsync_WithNullValue_CustomMessage_ReturnsFailure()
    {
        // Arrange
        string? myValue = null;
        var validator = new InlineValidator<string?>
        {
            v => v.RuleFor(x => x).NotEmpty()
        };

        // Act
        var result = await validator.ValidateToResultAsync(myValue, "CustomParam", "Custom error message", TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailureOfType<ValidationError>()
            .Which.Should()
            .HaveFieldErrorWithDetail("CustomParam", "Custom error message");
    }

    [Fact]
    public async Task ValidateToResultAsync_WithCancellationToken_RespectsToken()
    {
        // Arrange
        var validator = new InlineValidator<string>
        {
            v => v.RuleFor(x => x).NotEmpty()
        };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await validator.ValidateToResultAsync("test", cancellationToken: cts.Token);

        // Assert
        result.Should().BeSuccess();
    }

    #endregion
}