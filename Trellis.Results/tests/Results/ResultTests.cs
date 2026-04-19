namespace Trellis.Results.Tests;

using Trellis.Testing;

public class ResultTests
{
    [Fact]
    public void Success_argument_is_not_null_Success_result_expected()
    {
        var result = Result.Ok("Hello");

        result.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public void Can_work_with_nullable_structs()
    {
        var result = Result.Ok((DateTime?)null);

        result.Should().BeSuccess()
            .Which.Should().Be(null);
    }

    [Fact]
    public void Success_Unit_Result()
    {
        // Arrange
        var result = Result.Ok();

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(default(Unit));
    }

    [Fact]
    public void Failed_Unit_Result()
    {
        // Arrange
        var result = Result.Fail(Error.Forbidden("Testing"));

        // Assert
        result.Should().BeFailureOfType<ForbiddenError>()
            .Which.Should().HaveDetail("Testing");
    }

    [Fact]
    public void Wrap_value_into_Success_Result_struct()
    {
        // Arrange
        var value = DateTime.Now;

        // Act
        var result = value.ToResult();

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(value);
    }

    [Fact]
    public void Wrap_value_into_Success_Result_class()
    {
        // Arrange
        var value = "Hello";

        // Act
        var result = value.ToResult();

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(value);
    }
}