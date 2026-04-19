namespace Trellis.Testing.Tests.Assertions;

public class ErrorAssertionsTests
{
    [Fact]
    public void HaveCode_Should_Pass_When_Code_Matches()
    {
        // Arrange
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };

        // Act & Assert
        error.Should().HaveCode("not-found");
    }

    [Fact]
    public void HaveCode_Should_Fail_When_Code_Does_Not_Match()
    {
        // Arrange
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };

        // Act
        var act = () => error.Should().HaveCode("wrong.code");

        // Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void HaveDetail_Should_Pass_When_Detail_Matches()
    {
        // Arrange
        var error = new Error.BadRequest("bad.request") { Detail = "Invalid input" };

        // Act & Assert
        error.Should().HaveDetail("Invalid input");
    }

    [Fact]
    public void HaveDetail_Should_Fail_When_Detail_Does_Not_Match()
    {
        // Arrange
        var error = new Error.BadRequest("bad.request") { Detail = "Invalid input" };

        // Act
        var act = () => error.Should().HaveDetail("Wrong detail");

        // Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void HaveDetailContaining_Should_Pass_When_Contains_Substring()
    {
        // Arrange
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "User with ID 123 not found" };

        // Act & Assert
        error.Should().HaveDetailContaining("123");
    }

    [Fact]
    public void HaveDetailContaining_Should_Fail_When_Does_Not_Contain()
    {
        // Arrange
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "User not found" };

        // Act
        var act = () => error.Should().HaveDetailContaining("456");

        // Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Should_Allow_Chaining_Assertions()
    {
        // Arrange
        var error = new Error.Conflict(null, "conflict") { Detail = "Resource already exists" };

        // Act & Assert
        error.Should()
            .HaveCode("conflict")
            .And.HaveDetail("Resource already exists")
            .And.HaveDetailContaining("exists");
    }

    [Fact]
    public void HaveCode_Should_Support_Because_Reason()
    {
        // Arrange
        var error = new Error.Unauthorized() { Detail = "Not authenticated" };

        // Act & Assert
        error.Should().HaveCode("unauthorized", "because authentication is required");
    }

    [Fact]
    public void HaveDetail_Should_Support_Because_Reason()
    {
        // Arrange
        var error = new Error.Forbidden("authorization.forbidden") { Detail = "Access denied" };

        // Act & Assert
        error.Should().HaveDetail("Access denied", "because user lacks permission");
    }

    [Fact]
    public void HaveDetailContaining_Should_Support_Because_Reason()
    {
        // Arrange
        var error = new Error.Conflict(null, "domain.violation") { Detail = "Balance insufficient for withdrawal" };

        // Act & Assert
        error.Should().HaveDetailContaining("insufficient", "because this is a business rule");
    }

    #region HaveInstance Tests

    #endregion

    #region BeOfType Tests

    [Fact]
    public void BeOfType_Should_Pass_When_Type_Matches()
    {
        // Arrange
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };

        // Act & Assert
        error.Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public void BeOfType_Should_Fail_When_Type_Does_Not_Match()
    {
        // Arrange
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };

        // Act
        var act = () => error.Should().BeOfType<Error.UnprocessableContent>();

        // Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void BeOfType_Should_Return_Typed_Error_For_Chaining()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Invalid email" }));

        // Act & Assert
        error.Should()
            .BeOfType<Error.UnprocessableContent>()
            .Which.Should()
            .HaveFieldError("email");
    }

    #endregion

    #region Be Tests

    [Fact]
    public void Be_Should_Pass_When_Errors_Are_Equal()
    {
        // Arrange
        var error1 = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };
        var error2 = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };

        // Act & Assert
        error1.Should().Be(error2);
    }

    [Fact]
    public void Be_Should_Fail_When_Errors_Are_Different()
    {
        // Arrange
        var error1 = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };
        var error2 = new Error.BadRequest("bad.request") { Detail = "Bad request" };

        // Act
        var act = () => error1.Should().Be(error2);

        // Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Be_Should_Support_Because_Reason()
    {
        // Arrange
        var error1 = new Error.Conflict(null, "conflict") { Detail = "Conflict" };
        var error2 = new Error.Conflict(null, "conflict") { Detail = "Conflict" };

        // Act & Assert
        error1.Should().Be(error2, "because they represent the same conflict");
    }

    #endregion
}