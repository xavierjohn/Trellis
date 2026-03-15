namespace Trellis.Results.Tests.Results.Extensions;

using Trellis;
using Trellis.Testing;

public class Ensure_ValueTask_Left_Tests
{
    #region EnsureAsync with Func<bool> predicate and static Error

    [Fact]
    public async Task EnsureAsync_Left_Bool_NoParam_StaticError_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("Initial value"));

        // Act
        var result = await initialResult.EnsureAsync(
            () => true,
            Error.Validation("Should not fail"));

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be("Initial value");
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_NoParam_StaticError_SuccessResult_PredicateFalse_ReturnsFailure()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("Initial value"));

        // Act
        var result = await initialResult.EnsureAsync(
            () => false,
            Error.Validation("Predicate check failed"));

        // Assert
        result.Should().BeFailure("predicate failed")
            .Which.Should().Be(Error.Validation("Predicate check failed"));
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_NoParam_StaticError_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = Error.NotFound("Initial error");
        var initialResult = ValueTask.FromResult(Result.Failure<string>(initialError));
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            () =>
            {
                predicateInvoked = true;
                return true;
            },
            Error.Validation("Should not see this error"));

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    #endregion

    #region EnsureAsync with Func<TOk, bool> predicate and static Error

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_StaticError_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("test"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => value.Length > 0,
            Error.Validation("Value should not be empty"));

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be("test");
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_StaticError_SuccessResult_PredicateFalse_ReturnsFailure()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("abc"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => value.Length > 10,
            Error.Validation("String is too short"));

        // Assert
        result.Should().BeFailure("predicate failed")
            .Which.Should().Be(Error.Validation("String is too short"));
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_StaticError_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = Error.Conflict("Initial conflict");
        var initialResult = ValueTask.FromResult(Result.Failure<string>(initialError));
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value =>
            {
                predicateInvoked = true;
                return value.Length > 0;
            },
            Error.Validation("Should not see this error"));

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_StaticError_PredicateUsesValue_WorksCorrectly()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success(25));

        // Act
        var result = await initialResult.EnsureAsync(
            age => age >= 18,
            Error.Validation("Must be 18 or older"));

        // Assert
        result.Should().BeSuccess("age is valid")
            .Which.Should().Be(25);
    }

    #endregion

    #region EnsureAsync with Func<TOk, bool> predicate and sync Error factory

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_ErrorFactory_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success(100));

        // Act
        var result = await initialResult.EnsureAsync(
            value => value >= 0,
            value => Error.Validation($"Value {value} must be non-negative"));

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be(100);
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_ErrorFactory_SuccessResult_PredicateFalse_ReturnsContextualError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success(-5));

        // Act
        var result = await initialResult.EnsureAsync(
            value => value >= 0,
            value => Error.Validation($"Value {value} must be non-negative"));

        // Assert
        result.Should().BeFailure("predicate failed")
            .Which.Should().Be(Error.Validation("Value -5 must be non-negative"));
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_ErrorFactory_FailureResult_ErrorFactoryNotInvoked()
    {
        // Arrange
        var initialError = Error.Unauthorized("Initial error");
        var initialResult = ValueTask.FromResult(Result.Failure<int>(initialError));
        var errorFactoryInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value => value > 0,
            value =>
            {
                errorFactoryInvoked = true;
                return Error.Validation("Should not see this");
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        errorFactoryInvoked.Should().BeFalse("error factory should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_ErrorFactory_ErrorFactoryReceivesValue()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("test@example"));
        var capturedValue = string.Empty;

        // Act
        var result = await initialResult.EnsureAsync(
            value => value.Contains('@') && value.Contains('.'),
            value =>
            {
                capturedValue = value;
                return Error.Validation($"Email '{value}' is invalid");
            });

        // Assert
        result.Should().BeFailure("email validation failed");
        capturedValue.Should().Be("test@example");
        result.Error.Detail.Should().Contain("test@example");
    }

    #endregion

    #region EnsureAsync with Func<TOk, bool> predicate and async Error factory

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_AsyncErrorFactory_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("valid-username"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => value.Length >= 3,
            async value =>
            {
                await Task.Delay(1); // Simulate async error creation
                return Error.Validation($"Username '{value}' is too short");
            });

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be("valid-username");
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_AsyncErrorFactory_SuccessResult_PredicateFalse_ReturnsAsyncError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("ab"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => value.Length >= 3,
            async value =>
            {
                await Task.Delay(1); // Simulate async error creation
                return Error.Validation($"Username '{value}' must be at least 3 characters");
            });

        // Assert
        result.Should().BeFailure("predicate failed");
        result.Error.Detail.Should().Contain("ab");
        result.Error.Detail.Should().Contain("must be at least 3 characters");
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_AsyncErrorFactory_FailureResult_AsyncErrorFactoryNotInvoked()
    {
        // Arrange
        var initialError = Error.Forbidden("Initial error");
        var initialResult = ValueTask.FromResult(Result.Failure<string>(initialError));
        var asyncErrorFactoryInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value => value.Length > 0,
            async value =>
            {
                asyncErrorFactoryInvoked = true;
                await Task.Delay(1);
                return Error.Validation("Should not see this");
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        asyncErrorFactoryInvoked.Should().BeFalse("async error factory should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_Left_Bool_WithParam_AsyncErrorFactory_PredicateNotCalled_WhenResultIsFailure()
    {
        // Arrange
        var initialError = Error.BadRequest("Initial bad request");
        var initialResult = ValueTask.FromResult(Result.Failure<string>(initialError));
        var predicateInvoked = false;
        var asyncErrorFactoryInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value =>
            {
                predicateInvoked = true;
                return value.Length > 0;
            },
            async value =>
            {
                asyncErrorFactoryInvoked = true;
                await Task.Delay(1);
                return Error.Validation("Should not see this");
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
        asyncErrorFactoryInvoked.Should().BeFalse("async error factory should not be invoked for failed results");
    }

    #endregion

    #region EnsureAsync with Func<Result<TOk>> predicate (no parameter)

    [Fact]
    public async Task EnsureAsync_Left_Result_NoParam_SuccessResult_PredicateSuccess_ReturnsOriginalSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("Initial message"));

        // Act
        var result = await initialResult.EnsureAsync(
            () => Result.Success<string>("Validation passed"));

        // Assert
        result.Should().BeSuccess("initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task EnsureAsync_Left_Result_NoParam_SuccessResult_PredicateFailure_ReturnsPredicateError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("Initial value"));
        var predicateError = Error.Validation("System validation failed");

        // Act
        var result = await initialResult.EnsureAsync(
            () => Result.Failure<string>(predicateError));

        // Assert
        result.Should().BeFailure("predicate returned failure")
            .Which.Should().Be(predicateError);
    }

    [Fact]
    public async Task EnsureAsync_Left_Result_NoParam_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = Error.RateLimit("Initial error");
        var initialResult = ValueTask.FromResult(Result.Failure<string>(initialError));
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            () =>
            {
                predicateInvoked = true;
                return Result.Success<string>("Should not reach");
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    #endregion

    #region EnsureAsync with Func<TOk, Result<TOk>> predicate (with parameter)

    [Fact]
    public async Task EnsureAsync_Left_Result_WithParam_SuccessResult_PredicateSuccess_ReturnsOriginalSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("Initial message"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => Result.Success<string>($"Validated: {value}"));

        // Assert
        result.Should().BeSuccess("initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task EnsureAsync_Left_Result_WithParam_SuccessResult_PredicateFailure_ReturnsPredicateError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success(15));
        var predicateError = Error.Validation("Age must be 18 or older");

        // Act
        var result = await initialResult.EnsureAsync(
            age => age >= 18
                ? Result.Success(age)
                : Result.Failure<int>(predicateError));

        // Assert
        result.Should().BeFailure("age validation failed")
            .Which.Should().Be(predicateError);
    }

    [Fact]
    public async Task EnsureAsync_Left_Result_WithParam_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = Error.Domain("Initial domain error");
        var initialResult = ValueTask.FromResult(Result.Failure<int>(initialError));
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value =>
            {
                predicateInvoked = true;
                return Result.Success<int>(value);
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_Left_Result_WithParam_PredicateUsesValue_WorksCorrectly()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("user@example.com"));

        // Act
        var result = await initialResult.EnsureAsync(
            email =>
            {
                var isValid = email.Contains('@') && email.Contains('.');
                return isValid
                    ? Result.Success(email)
                    : Result.Failure<string>(Error.Validation($"Email '{email}' is invalid"));
            });

        // Assert
        result.Should().BeSuccess("email is valid")
            .Which.Should().Be("user@example.com");
    }

    [Fact]
    public async Task EnsureAsync_Left_Result_WithParam_PredicateFailsWithContextualError_ReturnsContextualError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("invalid-email"));

        // Act
        var result = await initialResult.EnsureAsync(
            email => email.Contains('@') && email.Contains('.')
                ? Result.Success(email)
                : Result.Failure<string>(Error.Validation($"Email '{email}' must contain @ and .")));

        // Assert
        result.Should().BeFailure("email validation failed");
        result.Error.Detail.Should().Contain("invalid-email");
        result.Error.Detail.Should().Contain("must contain @ and .");
    }

    #endregion

    #region Edge Cases and Integration Tests

    [Fact]
    public async Task EnsureAsync_Left_WithNullableType_HandlesNullCorrectly()
    {
        // Arrange
        int? nullValue = null;
        var initialResult = ValueTask.FromResult(Result.Success(nullValue));

        // Act
        var result = await initialResult.EnsureAsync(
            value => !value.HasValue,
            Error.Validation("Value should be null for this test"));

        // Assert
        result.Should().BeSuccess("null value passes the null check");
    }

    [Fact]
    public async Task EnsureAsync_Left_WithComplexType_WorksCorrectly()
    {
        // Arrange
        var person = new Person("John", 25);
        var initialResult = ValueTask.FromResult(Result.Success(person));

        // Act
        var result = await initialResult.EnsureAsync(
            p => p.Age >= 18,
            p => Error.Validation($"{p.Name} must be 18 or older"));

        // Assert
        result.Should().BeSuccess("person is adult")
            .Which.Should().Be(person);
    }

    [Fact]
    public async Task EnsureAsync_Left_MultipleChainedEnsures_AllExecuteInOrder()
    {
        // Arrange
        var executionOrder = new List<int>();
        var initialResult = ValueTask.FromResult(Result.Success(100));

        // Act
        var result = await initialResult
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(1);
                    return value > 0;
                },
                Error.Validation("First check failed"))
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(2);
                    return value < 1000;
                },
                Error.Validation("Second check failed"))
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(3);
                    return value % 2 == 0;
                },
                Error.Validation("Third check failed"));

        // Assert
        result.Should().BeSuccess("all checks passed");
        executionOrder.Should().Equal([1, 2, 3], "ensures execute in order");
    }

    [Fact]
    public async Task EnsureAsync_Left_MultipleChainedEnsures_StopsAtFirstFailure()
    {
        // Arrange
        var executionOrder = new List<int>();
        var initialResult = ValueTask.FromResult(Result.Success(100));

        // Act
        var result = await initialResult
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(1);
                    return value > 0;
                },
                Error.Validation("First check failed"))
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(2);
                    return value < 50; // This will fail
                },
                Error.Validation("Second check failed"))
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(3);
                    return value % 2 == 0;
                },
                Error.Validation("Third check failed"));

        // Assert
        result.Should().BeFailure("second check failed")
            .Which.Should().Be(Error.Validation("Second check failed"));
        executionOrder.Should().Equal([1, 2], "execution stops after first failure");
    }

    [Fact]
    public async Task EnsureAsync_Left_MixingResultAndBooleanPredicates_WorksCorrectly()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("valid-input"));

        // Act
        var result = await initialResult
            .EnsureAsync(
                value => value.Length >= 5,
                Error.Validation("Too short"))
            .EnsureAsync(
                value => value.Contains('-') // Use char instead of string
                    ? Result.Success(value)
                    : Result.Failure<string>(Error.Validation("Must contain 'valid'")));

        // Assert
        result.Should().BeSuccess("all validations passed")
            .Which.Should().Be("valid-input");
    }

    [Fact]
    public async Task EnsureAsync_Left_ChainedWithAsyncErrorFactory_WorksCorrectly()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success("test-value"));

        // Act
        var result = await initialResult
            .EnsureAsync(
                value => value.Length >= 5,
                Error.Validation("Too short"))
            .EnsureAsync(
                value => value.Contains('-'), // Use char instead of string
                async value =>
                {
                    await Task.Delay(1);
                    return Error.Validation($"Value '{value}' must contain hyphen");
                });

        // Assert
        result.Should().BeSuccess("all validations passed")
            .Which.Should().Be("test-value");
    }

    [Fact]
    public async Task EnsureAsync_Left_WithDifferentErrorTypes_MaintainsErrorType()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Success(10));

        // Act
        var result = await initialResult.EnsureAsync(
            value => value >= 18,
            Error.Domain("Business rule: Must be 18 or older"));

        // Assert
        result.Should().BeFailure("validation failed")
            .Which.Should().BeOfType<DomainError>();
        result.Error.Code.Should().Be("domain.error");
    }

    #endregion

    private record Person(string Name, int Age);
}