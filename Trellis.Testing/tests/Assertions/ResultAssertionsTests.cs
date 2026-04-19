namespace Trellis.Testing.Tests.Assertions;

public class ResultAssertionsTests
{
    [Fact]
    public void BeSuccess_Should_Pass_When_Result_Is_Success()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void BeSuccess_Should_Fail_When_Result_Is_Failure()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act & Assert
        var act = () => result.Should().BeSuccess();

        act.Should().Throw<Exception>()
            .WithMessage("*to be success*failed with error*");
    }

    [Fact]
    public void BeSuccess_Should_Return_Value_In_Which()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        result.Should()
            .BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public void BeFailure_Should_Pass_When_Result_Is_Failure()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act & Assert
        result.Should().BeFailure();
    }

    [Fact]
    public void BeFailure_Should_Fail_When_Result_Is_Success()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        var act = () => result.Should().BeFailure();

        act.Should().Throw<Exception>()
            .WithMessage("*to be failure*succeeded with value*");
    }

    [Fact]
    public void BeFailure_Should_Return_Error_In_Which()
    {
        // Arrange
        var error = Error.NotFound("Not found");
        var result = Result.Fail<int>(error);

        // Act & Assert
        result.Should()
            .BeFailure()
            .Which.Should().BeOfType<NotFoundError>();
    }

    [Fact]
    public void BeFailureOfType_Should_Pass_When_Error_Matches_Type()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act & Assert
        result.Should().BeFailureOfType<NotFoundError>();
    }

    [Fact]
    public void BeFailureOfType_Should_Fail_When_Error_Type_Different()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Validation("Invalid"));

        // Act & Assert
        var act = () => result.Should().BeFailureOfType<NotFoundError>();

        act.Should().Throw<Exception>()
            .WithMessage("*to be of type*NotFoundError*found*ValidationError*");
    }

    [Fact]
    public void BeFailureOfType_Should_Return_Typed_Error_In_Which()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act & Assert
        result.Should()
            .BeFailureOfType<NotFoundError>()
            .Which.Should().BeOfType<NotFoundError>();
    }

    [Fact]
    public void HaveValue_Should_Pass_When_Value_Matches()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        result.Should().HaveValue(42);
    }

    [Fact]
    public void HaveValue_Should_Fail_When_Value_Different()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        var act = () => result.Should().HaveValue(99);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void HaveValueMatching_Should_Pass_When_Predicate_Satisfied()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        result.Should().HaveValueMatching(x => x > 40);
    }

    [Fact]
    public void HaveValueMatching_Should_Fail_When_Predicate_Not_Satisfied()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        var act = () => result.Should().HaveValueMatching(x => x > 50);

        act.Should().Throw<Exception>()
            .WithMessage("*value to match predicate*");
    }

    #region HaveValueEquivalentTo Tests

    [Fact]
    public void HaveValueEquivalentTo_Should_Pass_When_Equivalent()
    {
        // Arrange
        var result = Result.Ok(new { Name = "John", Age = 30 });

        // Act & Assert
        result.Should().HaveValueEquivalentTo(new { Name = "John", Age = 30 });
    }

    [Fact]
    public void HaveValueEquivalentTo_Should_Fail_When_Not_Equivalent()
    {
        // Arrange
        var result = Result.Ok(new { Name = "John", Age = 30 });

        // Act
        var act = () => result.Should().HaveValueEquivalentTo(new { Name = "Jane", Age = 25 });

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion

    #region HaveErrorCode Tests

    [Fact]
    public void HaveErrorCode_Should_Pass_When_Code_Matches()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act & Assert
        result.Should().HaveErrorCode("not.found.error");
    }

    [Fact]
    public void HaveErrorCode_Should_Fail_When_Code_Different()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act
        var act = () => result.Should().HaveErrorCode("wrong.code");

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion

    #region HaveErrorDetail Tests

    [Fact]
    public void HaveErrorDetail_Should_Pass_When_Detail_Matches()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Resource not found"));

        // Act & Assert
        result.Should().HaveErrorDetail("Resource not found");
    }

    [Fact]
    public void HaveErrorDetail_Should_Fail_When_Detail_Different()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Resource not found"));

        // Act
        var act = () => result.Should().HaveErrorDetail("Wrong detail");

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion

    #region HaveErrorDetailContaining Tests

    [Fact]
    public void HaveErrorDetailContaining_Should_Pass_When_Contains()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("User with ID 123 not found"));

        // Act & Assert
        result.Should().HaveErrorDetailContaining("123");
    }

    [Fact]
    public void HaveErrorDetailContaining_Should_Fail_When_Not_Contains()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("User not found"));

        // Act
        var act = () => result.Should().HaveErrorDetailContaining("456");

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion
}