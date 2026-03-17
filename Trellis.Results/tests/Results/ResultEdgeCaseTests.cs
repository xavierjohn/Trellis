namespace Trellis.Results.Tests;

using Xunit;

/// <summary>
/// Tests for Result edge cases including null handling, exception scenarios, and boundary conditions.
/// </summary>
public class ResultEdgeCaseTests
{
    #region TryGetValue and TryGetError Edge Cases

    [Fact]
    public void TryGetValue_OnSuccessWithNull_ShouldReturnTrueWithNullValue()
    {
        // Arrange
        var result = Result.Success(default(string));

        // Act
        bool success = result.TryGetValue(out var value);

        // Assert
        success.Should().BeTrue();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetValue_OnFailure_ShouldReturnFalseWithDefaultValue()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NotFound("Not found"));

        // Act
        bool success = result.TryGetValue(out var value);

        // Assert
        success.Should().BeFalse();
        value.Should().Be(default(int));
    }

    [Fact]
    public void TryGetError_OnSuccess_ShouldReturnFalseWithNullError()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        bool hasError = result.TryGetError(out var error);

        // Assert
        hasError.Should().BeFalse();
        error.Should().BeNull();
    }

    [Fact]
    public void TryGetError_OnFailure_ShouldReturnTrueWithError()
    {
        // Arrange
        var expectedError = Error.Validation("Invalid input");
        var result = Result.Failure<int>(expectedError);

        // Act
        bool hasError = result.TryGetError(out var error);

        // Assert
        hasError.Should().BeTrue();
        error.Should().Be(expectedError);
    }

    #endregion

    #region Deconstruction Edge Cases

    [Fact]
    public void Deconstruct_OnSuccessWithNull_ShouldDeconstructCorrectly()
    {
        // Arrange
        var result = Result.Success(default(string));

        // Act
        var (isSuccess, value, error) = result;

        // Assert
        isSuccess.Should().BeTrue();
        value.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void Deconstruct_OnFailure_ShouldDeconstructCorrectly()
    {
        // Arrange
        var expectedError = Error.Conflict("Conflict");
        var result = Result.Failure<int>(expectedError);

        // Act
        var (isSuccess, value, error) = result;

        // Assert
        isSuccess.Should().BeFalse();
        value.Should().Be(default(int));
        error.Should().Be(expectedError);
    }

    [Fact]
    public void Deconstruct_CanBeUsedInSwitchExpression()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        string message = result switch
        {
            { IsSuccess: true } => $"Success: {result.Value}",
            { IsFailure: true } => $"Failure: {result.Error.Code}",
            _ => "Unknown"
        };

        // Assert
        message.Should().Be("Success: 42");
    }

    #endregion

    #region Equality Edge Cases

    [Fact]
    public void Equals_TwoSuccessResultsWithNullValues_ShouldBeEqual()
    {
        // Arrange
        var result1 = Result.Success(default(string));
        var result2 = Result.Success(default(string));

        // Act & Assert
        result1.Should().Be(result2);
        (result1 == result2).Should().BeTrue();
        (result1 != result2).Should().BeFalse();
    }

    [Fact]
    public void Equals_SuccessResultWithNullAndNonNull_ShouldNotBeEqual()
    {
        // Arrange
        var result1 = Result.Success(default(string));
        var result2 = Result.Success("value");

        // Act & Assert
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Equals_TwoFailuresWithSameErrorCode_ShouldBeEqual()
    {
        // Arrange
        var result1 = Result.Failure<int>(Error.NotFound("User not found"));
        var result2 = Result.Failure<int>(Error.NotFound("Resource not found"));

        // Act & Assert
        // Errors are equal based on Code, not Detail
        result1.Should().Be(result2);
    }

    [Fact]
    public void Equals_SuccessAndFailure_ShouldNotBeEqual()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Failure<int>(Error.NotFound("Not found"));

        // Act & Assert
        result1.Should().NotBe(result2);
        result1.Equals(result2).Should().BeFalse();
    }

    [Fact]
    public void Equals_CompareWithNull_ShouldReturnFalse()
    {
        // Arrange
        var result = Result.Success(42);

        // Act & Assert
        result.Equals((object?)null).Should().BeFalse();
    }

    [Fact]
    public void Equals_CompareWithDifferentType_ShouldReturnFalse()
    {
        // Arrange
        var result = Result.Success(42);
        object otherObject = "string";

        // Act & Assert
        result.Equals(otherObject).Should().BeFalse();
    }

    #endregion

    #region HashCode Edge Cases

    [Fact]
    public void GetHashCode_SuccessResultsWithNullValues_ShouldHaveSameHashCode()
    {
        // Arrange
        var result1 = Result.Success(default(string));
        var result2 = Result.Success(default(string));

        // Act
        int hash1 = result1.GetHashCode();
        int hash2 = result2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void GetHashCode_FailureResults_ShouldBeConsistent()
    {
        // Arrange
        var error = Error.Validation("Invalid");
        var result1 = Result.Failure<int>(error);
        var result2 = Result.Failure<int>(error);

        // Act
        int hash1 = result1.GetHashCode();
        int hash2 = result2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    #endregion

    #region ToString Edge Cases

    [Fact]
    public void ToString_SuccessWithNull_ShouldFormatCorrectly()
    {
        // Arrange
        var result = Result.Success(default(string));

        // Act
        string str = result.ToString();

        // Assert
        str.Should().Be("Success(<null>)");
    }

    [Fact]
    public void ToString_SuccessWithValue_ShouldFormatCorrectly()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        string str = result.ToString();

        // Assert
        str.Should().Be("Success(42)");
    }

    [Fact]
    public void ToString_Failure_ShouldFormatCorrectly()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NotFound("User not found"));

        // Act
        string str = result.ToString();

        // Assert
        str.Should().Contain("Failure");
        str.Should().Contain("not.found.error");
        str.Should().Contain("User not found");
    }

    #endregion

    #region Implicit Conversion Edge Cases

    [Fact]
    public void ImplicitConversion_FromNullValue_ShouldCreateSuccessResult()
    {
        // Arrange & Act
        Result<string?> result = (string?)null;

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void ImplicitConversion_FromError_ShouldCreateFailureResult()
    {
        // Arrange
        Error error = Error.Unexpected("Something went wrong");

        // Act
        Result<int> result = error;

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    #endregion

    #region Exception Handling Edge Cases

    [Fact]
    public void Value_OnFailure_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NotFound("Not found"));

        // Act
        Action act = () => { var _ = result.Value; };

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*failed result*");
    }

    [Fact]
    public void Error_OnSuccess_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        Action act = () => { var _ = result.Error; };

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*successful result*");
    }

    #endregion

    #region Try/TryAsync Exception Handling Edge Cases

    [Fact]
    public void Try_WithNullFunction_ShouldReturnFailureResult()
    {
        // Act - Try() catches all exceptions including NullReferenceException and returns Failure
        var result = Result.Try<int>(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UnexpectedError>();
    }

    [Fact]
    public void Try_WhenFunctionThrows_ShouldReturnFailureWithUnexpectedError()
    {
        // Act
        var result = Result.Try<int>(() => throw new InvalidOperationException("Test exception"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UnexpectedError>();
        result.Error.Detail.Should().Be("Test exception");
    }

    [Fact]
    public void Try_WithCustomExceptionMapper_ShouldUseCustomMapping()
    {
        // Arrange
        Error CustomMapper(Exception ex) => Error.Validation($"Custom: {ex.Message}");

        // Act
        var result = Result.Try<int>(() => throw new InvalidOperationException("Test"), CustomMapper);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        result.Error.Detail.Should().Be("Custom: Test");
    }

    [Fact]
    public async Task TryAsync_WhenFunctionThrows_ShouldReturnFailureWithUnexpectedError()
    {
        // Act
        var result = await Result.TryAsync(async () =>
        {
            await Task.Delay(1);
            throw new InvalidOperationException("Async test exception");
#pragma warning disable CS0162 // Unreachable code detected
            return 42;
#pragma warning restore CS0162
        });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UnexpectedError>();
        result.Error.Detail.Should().Be("Async test exception");
    }

    [Fact]
    public async Task TryAsync_WithCancelledToken_ShouldPropagateException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var ct = cts.Token;

        // Act & Assert — OperationCanceledException must not be swallowed
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Result.TryAsync(async () =>
            {
                await Task.Delay(1000, ct);
                return 42;
            }));
    }

    #endregion

    #region SuccessIf/FailureIf Edge Cases

    [Fact]
    public void SuccessIf_WithFalseCondition_ShouldReturnFailure()
    {
        // Act
        var result = Result.SuccessIf(false, 42, Error.Validation("Condition not met"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Detail.Should().Be("Condition not met");
    }

    [Fact]
    public void SuccessIf_WithNullValue_ShouldReturnSuccessWithNull()
    {
        // Act
        var result = Result.SuccessIf(true, (string?)null, Error.Validation("Error"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void FailureIf_WithTrueCondition_ShouldReturnFailure()
    {
        // Act
        var result = Result.FailureIf(true, 42, Error.Validation("Failure condition met"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Detail.Should().Be("Failure condition met");
    }

    #endregion

    #region Struct Value Type Edge Cases

    [Fact]
    public void Success_WithDefaultStructValue_ShouldWorkCorrectly()
    {
        // Act
        var result = Result.Success(default(DateTime));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(default(DateTime));
    }

    [Fact]
    public void Success_WithNullableStructWithValue_ShouldWorkCorrectly()
    {
        // Act
        var result = Result.Success<DateTime?>(DateTime.Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public void Success_WithNullableStructWithoutValue_ShouldWorkCorrectly()
    {
        // Act
        var result = Result.Success(default(DateTime?));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    #endregion

    #region Unit Result Edge Cases

    [Fact]
    public void Success_Unit_ShouldCreateSuccessResultWithUnitValue()
    {
        // Act
        var result = Result.Success();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(default(Unit));
    }

    [Fact]
    public void Failure_Unit_ShouldCreateFailureResultWithError()
    {
        // Arrange
        var error = Error.BadRequest("Bad request");

        // Act
        var result = Result.Failure(error);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void FromException_ShouldCreateFailureUnitResult()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");

        // Act
        var result = Result.FromException(exception);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UnexpectedError>();
        result.Error.Detail.Should().Be("Test exception");
    }

    [Fact]
    public void FromException_Generic_ShouldCreateFailureResultWithType()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");

        // Act
        var result = Result.FromException<int>(exception);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UnexpectedError>();
        result.Error.Detail.Should().Be("Test exception");
    }

    #endregion

    #region ToResult Extension Edge Cases

    [Fact]
    public void ToResult_OnValue_ShouldCreateSuccessResult()
    {
        // Arrange
        int value = 42;

        // Act
        var result = value.ToResult();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ToResult_OnNullReferenceType_ShouldCreateSuccessResultWithNull()
    {
        // Arrange
        string? value = null;

        // Act
        var result = value.ToResult();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    #endregion
}