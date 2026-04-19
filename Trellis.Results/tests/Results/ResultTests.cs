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
    public void Success_argument_via_callback()
    {
        var result = Result.Success(() => "Hello");

        result.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public void Success_argument_is_null_Success_result_expected()
    {
        var result = Result.Ok(default(string));

        result.Should().BeSuccess();
    }

    [Fact]
    public void Failure_returns_failed_result()
    {
        var result = Result.Fail<string>(Error.Validation("Bad first name"));

        result.Should().BeFailure()
            .Which.Should().HaveDetail("Bad first name");
    }

    [Fact]
    public void Failure_returns_failed_result_via_callback()
    {
        var result = Result.Failure<string>(() => Error.Validation("Bad first name"));

        result.Should().BeFailure()
            .Which.Should().HaveDetail("Bad first name");
    }

    [Fact]
    public void CreateFailure_value_is_null_Success_result_expected()
    {
        var result = Result.FailureIf(false, "Hello", Error.Validation("Bad first name"));

        result.Should().BeSuccess();
    }

    [Fact]
    public void CreateFailure_predicate_is_false_Success_result_expected()
    {
        var result = Result.FailureIf(() => false, string.Empty, Error.Unexpected(string.Empty));

        result.Should().BeSuccess();
    }

    [Fact]
    public void CreateFailure_predicate_is_true_Failure_result_expected()
    {
        var result = Result.FailureIf(() => true, "Hello", Error.Unexpected("You can't touch this."));

        result.Should().BeFailure()
            .Which.Should().HaveDetail("You can't touch this.");
    }

    [Fact]
    public async Task CreateFailure_async_predicate_is_false_Success_result_expected()
    {
        var result = await Result.FailureIfAsync(() => Task.FromResult(false), "Hello", Error.Unexpected(string.Empty));

        result.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public async Task CreateFailure_async_predicate_is_true_Failure_result_expected()
    {
        var result = await Result.FailureIfAsync(() => Task.FromResult(true), "Hello", Error.Unexpected("You can't touch this."));

        result.Should().BeFailure()
            .Which.Should().HaveDetail("You can't touch this.");
    }

    [Fact]
    public void CreateFailure_generic_argument_is_false_Success_result_expected()
    {
        byte val = 7;
        var result = Result.FailureIf(false, val, Error.Unexpected(string.Empty));

        result.Should().BeSuccess()
            .Which.Should().Be(val);
    }

    [Fact]
    public void CreateFailure_generic_argument_is_true_Failure_result_expected()
    {
        var val = .56;
        var result = Result.FailureIf(true, val, Error.Unexpected("simple result error"));

        result.Should().BeFailure()
            .Which.Should().HaveDetail("simple result error");
    }

    [Fact]
    public void Can_work_with_nullable_structs()
    {
        var result = Result.Ok((DateTime?)null);

        result.Should().BeSuccess()
            .Which.Should().Be(null);
    }

    [Fact]
    public void Can_implicitly_convert_to_Result()
    {
        // Arrange
        var hello = "Hello";

        // Act
        Result<string> result = hello;

        // Assert
        result.Should().HaveValue(hello);
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