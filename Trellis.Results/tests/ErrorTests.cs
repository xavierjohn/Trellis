namespace Trellis.Results.Tests;

using Xunit;
using static Trellis.ValidationError;

public class ErrorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_conflict_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.Conflict("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<ConflictError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: ConflictError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_not_found_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.NotFound("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<NotFoundError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: NotFoundError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Fact]
    public void Create_not_found_error_default()
    {
        // Arrange
        // Act
        var error = Error.NotFound("message");

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("not.found.error");
        error.Should().BeOfType<NotFoundError>();
        error.Instance.Should().BeNull();

        error.ToString().Should().Be($"Type: NotFoundError, Code: not.found.error, Detail: message, Instance: N/A");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_Forbidden_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.Forbidden("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<ForbiddenError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: ForbiddenError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_Unauthorized_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.Unauthorized("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<UnauthorizedError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: UnauthorizedError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_Unexpected_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.Unexpected("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<UnexpectedError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: UnexpectedError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Fact]
    public void Create_Validation_error()
    {
        // Arrange
        // Act
        var error = Error.Validation("field detail.", "field name");

        // Assert
        error.Detail.Should().Be("field detail.");
        error.Code.Should().Be("validation.error");
        error.Should().BeOfType<ValidationError>();
        error.Instance.Should().BeNull();
        var validationError = (ValidationError)error;
        validationError.FieldErrors.Should().HaveCount(1);
        validationError.FieldErrors[0].FieldName.Should().Be("field name");
        validationError.FieldErrors[0].Details.Should().HaveCount(1);
        validationError.FieldErrors[0].Details[0].Should().Be("field detail.");
        validationError.ToString().Should().Be($"Type: ValidationError, Code: validation.error, Detail: field detail., Instance: N/A{Environment.NewLine}field name: field detail.");
    }

    [Fact]
    public void Create_Combine_Validation_error()
    {
        // Arrange
        var error1 = Error.Validation("Too short.", "password");
        FieldError fieldDetails = new("password", ["Not complex.", "Make it complex."]);
        var error2 = Error.Validation([fieldDetails]);

        // Act
        Error combinedError = error1.Combine(error2);

        // Assert
        combinedError.Detail.Should().Be("Too short.");
        combinedError.Code.Should().Be("validation.error");
        combinedError.Should().BeOfType<ValidationError>();
        combinedError.Instance.Should().BeNull();
        var validationError = (ValidationError)combinedError;
        validationError.FieldErrors.Should().HaveCount(1);
        validationError.FieldErrors[0].FieldName.Should().Be("password");
        validationError.FieldErrors[0].Details.Should().HaveCount(3);
        validationError.FieldErrors[0].Details.Should().BeEquivalentTo(["Too short.", "Not complex.", "Make it complex."]);

        var errorSting = validationError.ToString();
        errorSting.Should().Be($"Type: ValidationError, Code: validation.error, Detail: Too short., Instance: N/A{Environment.NewLine}password: Too short., Not complex., Make it complex.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_precondition_failed_error(string? instance)
    {
        // Arrange & Act
        var error = Error.PreconditionFailed("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<PreconditionFailedError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: PreconditionFailedError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Fact]
    public void Create_precondition_failed_error_default()
    {
        // Arrange & Act
        var error = Error.PreconditionFailed("Resource has been modified.");

        // Assert
        error.Detail.Should().Be("Resource has been modified.");
        error.Code.Should().Be("precondition.failed.error");
        error.Should().BeOfType<PreconditionFailedError>();
        error.Instance.Should().BeNull();
    }
}