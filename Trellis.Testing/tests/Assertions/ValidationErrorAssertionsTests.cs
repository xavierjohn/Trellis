namespace Trellis.Testing.Tests.Assertions;

public class ValidationErrorAssertionsTests
{
    [Fact]
    public void HaveFieldError_Should_Pass_When_Field_Error_Exists()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is required" }));

        // Act & Assert
        error.Should().HaveFieldError("email");
    }

    [Fact]
    public void HaveFieldError_Should_Fail_When_Field_Error_Missing()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is required" }));

        // Act & Assert
        var act = () => error.Should().HaveFieldError("password");

        act.Should().Throw<Exception>()
            .WithMessage("*to contain field*password*");
    }

    [Fact]
    public void HaveFieldErrorWithDetail_Should_Pass_When_Detail_Matches()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is required" }));

        // Act & Assert
        error.Should().HaveFieldErrorWithDetail("email", "Email is required");
    }

    [Fact]
    public void HaveFieldErrorWithDetail_Should_Fail_When_Detail_Different()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is required" }));

        // Act & Assert
        var act = () => error.Should().HaveFieldErrorWithDetail("email", "Invalid format");

        act.Should().Throw<Exception>()
            .WithMessage("*to have detail*Invalid format*");
    }

    [Fact]
    public void HaveFieldCount_Should_Pass_When_Count_Matches()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"),    "validation.error") { Detail = "Email is required" },
            new FieldViolation(InputPointer.ForProperty("password"), "validation.error") { Detail = "Password is required" },
            new FieldViolation(InputPointer.ForProperty("age"),      "validation.error") { Detail = "Invalid age" }));

        // Act & Assert
        error.Should().HaveFieldCount(3);
    }

    [Fact]
    public void HaveFieldCount_Should_Fail_When_Count_Different()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is required" }));

        // Act & Assert
        var act = () => error.Should().HaveFieldCount(3);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Should_Support_Chained_Assertions()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"),    "validation.error") { Detail = "Email is required" },
            new FieldViolation(InputPointer.ForProperty("password"), "validation.error") { Detail = "Password is required" }));

        // Act & Assert
        error.Should()
            .HaveFieldCount(2)
            .And.HaveFieldError("email")
            .And.HaveFieldError("password")
            .And.HaveFieldErrorWithDetail("email", "Email is required");
    }
}