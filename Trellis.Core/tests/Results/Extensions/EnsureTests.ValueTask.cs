namespace Trellis.Core.Tests.Results.Extensions;

using Trellis;
using Trellis.Testing;

public class Ensure_ValueTask_Tests
{
    #region EnsureAsync with ValueTask<bool> predicate and static Error

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_StaticError_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("Initial value"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value.Length > 0),
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value should not be empty" });

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be("Initial value");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_StaticError_SuccessResult_PredicateFalse_ReturnsFailure()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("Initial value"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value.Length > 100),
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value is too short" });

        // Assert
        result.Should().BeFailure("predicate failed")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value is too short" });
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_StaticError_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Initial error" };
        var initialResult = ValueTask.FromResult(Result.Fail<string>(initialError));
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value =>
            {
                predicateInvoked = true;
                return ValueTask.FromResult(true);
            },
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Should not see this error" });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_StaticError_WithComplexPredicate_ReturnsExpectedResult()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok(42));

        // Act
        var result = await initialResult.EnsureAsync(
            async value =>
            {
                await Task.Delay(1); // Simulate async operation
                return value is > 0 and < 100;
            },
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value must be between 0 and 100" });

        // Assert
        result.Should().BeSuccess("value is within range")
            .Which.Should().Be(42);
    }

    #endregion

    #region EnsureAsync with ValueTask<bool> predicate and Error factory (sync)

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_ErrorFactory_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok(100));

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value >= 0),
            value => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Value {value} must be non-negative" });

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be(100);
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_ErrorFactory_SuccessResult_PredicateFalse_ReturnsFailureWithContextualError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok(-5));

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value >= 0),
            value => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Value {value} must be non-negative" });

        // Assert
        result.Should().BeFailure("predicate failed")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value -5 must be non-negative" });
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_ErrorFactory_FailureResult_ErrorFactoryNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.Conflict(null, "conflict") { Detail = "Initial error" };
        var initialResult = ValueTask.FromResult(Result.Fail<int>(initialError));
        var errorFactoryInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value > 0),
            value =>
            {
                errorFactoryInvoked = true;
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Should not see this" };
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        errorFactoryInvoked.Should().BeFalse("error factory should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_ErrorFactory_PredicateFalse_ErrorFactoryReceivesValue()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("test@example"));
        var capturedValue = string.Empty;

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value.Contains('@') && value.Contains('.')),
            value =>
            {
                capturedValue = value;
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Email '{value}' is invalid" };
            });

        // Assert
        result.Should().BeFailure("email is invalid");
        capturedValue.Should().Be("test@example");
        result.Error!.Detail.Should().Contain("test@example");
    }

    #endregion

    #region EnsureAsync with ValueTask<bool> predicate and async Error factory

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_AsyncErrorFactory_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("valid-username"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value.Length >= 3),
            async value =>
            {
                await Task.Delay(1); // Simulate async error creation
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Username '{value}' is too short" };
            });

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be("valid-username");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_AsyncErrorFactory_SuccessResult_PredicateFalse_ReturnsFailureWithAsyncError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("ab"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value.Length >= 3),
            async value =>
            {
                await Task.Delay(1); // Simulate async error creation
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Username '{value}' must be at least 3 characters" };
            });

        // Assert
        result.Should().BeFailure("predicate failed");
        result.Error!.Detail.Should().Contain("ab");
        result.Error!.Detail.Should().Contain("must be at least 3 characters");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_AsyncErrorFactory_FailureResult_AsyncErrorFactoryNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.Unauthorized() { Detail = "Initial error" };
        var initialResult = ValueTask.FromResult(Result.Fail<string>(initialError));
        var asyncErrorFactoryInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value.Length > 0),
            async value =>
            {
                asyncErrorFactoryInvoked = true;
                await Task.Delay(1);
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Should not see this" };
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        asyncErrorFactoryInvoked.Should().BeFalse("async error factory should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Bool_AsyncErrorFactory_ComplexAsyncOperation_WorksCorrectly()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok(DateTime.UtcNow.AddDays(-1)));

        // Act
        var result = await initialResult.EnsureAsync(
            async value =>
            {
                await Task.Delay(1); // Simulate async validation
                return value <= DateTime.UtcNow;
            },
            async value =>
            {
                await Task.Delay(1); // Simulate async error lookup/translation
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Date {value:yyyy-MM-dd} cannot be in the future" };
            });

        // Assert
        result.Should().BeSuccess("date is in the past");
    }

    #endregion

    #region EnsureAsync with ValueTask<Result<TOk>> predicate (no parameter)

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_NoParam_SuccessResult_PredicateSuccess_ReturnsOriginalSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("Initial message"));

        // Act
        var result = await initialResult.EnsureAsync(
            () => ValueTask.FromResult(Result.Ok<string>("Validation passed")));

        // Assert
        result.Should().BeSuccess("initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_NoParam_SuccessResult_PredicateFailure_ReturnsPredicateError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("Initial value"));
        var predicateError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Validation failed" };

        // Act
        var result = await initialResult.EnsureAsync(
            () => ValueTask.FromResult(Result.Fail<string>(predicateError)));

        // Assert
        result.Should().BeFailure("predicate returned failure")
            .Which.Should().Be(predicateError);
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_NoParam_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Initial error" };
        var initialResult = ValueTask.FromResult(Result.Fail<string>(initialError));
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            () =>
            {
                predicateInvoked = true;
                return ValueTask.FromResult(Result.Ok<string>("Should not reach"));
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_NoParam_ComplexAsyncPredicate_WorksCorrectly()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok(42));

        // Act
        var result = await initialResult.EnsureAsync(
            async () =>
            {
                await Task.Delay(1); // Simulate async validation
                var systemCheck = Random.Shared.Next(0, 100);
                return systemCheck >= 0
                    ? Result.Ok(systemCheck)
                    : Result.Fail<int>(new Error.ServiceUnavailable() { Detail = "System unavailable" });
            });

        // Assert
        result.Should().BeSuccess("system check passed")
            .Which.Should().Be(42, "original value is preserved");
    }

    #endregion

    #region EnsureAsync with ValueTask<Result<TOk>> predicate (with parameter)

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_WithParam_SuccessResult_PredicateSuccess_ReturnsOriginalSuccess()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("Initial message"));

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(Result.Ok<string>($"Validated: {value}")));

        // Assert
        result.Should().BeSuccess("initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_WithParam_SuccessResult_PredicateFailure_ReturnsPredicateError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok(15));
        var predicateError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Age must be 18 or older" };

        // Act
        var result = await initialResult.EnsureAsync(
            age => ValueTask.FromResult(
                age >= 18
                    ? Result.Ok(age)
                    : Result.Fail<int>(predicateError)));

        // Assert
        result.Should().BeFailure("age validation failed")
            .Which.Should().Be(predicateError);
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_WithParam_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.Conflict(null, "conflict") { Detail = "Initial conflict" };
        var initialResult = ValueTask.FromResult(Result.Fail<int>(initialError));
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value =>
            {
                predicateInvoked = true;
                return ValueTask.FromResult(Result.Ok<int>(value));
            });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_WithParam_PredicateUsesValue_WorksCorrectly()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("user@example.com"));

        // Act
        var result = await initialResult.EnsureAsync(
            async email =>
            {
                await Task.Delay(1); // Simulate async email validation
                var isValid = email.Contains('@') && email.Contains('.');
                return isValid
                    ? Result.Ok(email)
                    : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Email '{email}' is invalid" });
            });

        // Assert
        result.Should().BeSuccess("email is valid")
            .Which.Should().Be("user@example.com");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_WithParam_PredicateFailsWithContextualError_ReturnsContextualError()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("invalid-email"));

        // Act
        var result = await initialResult.EnsureAsync(
            async email =>
            {
                await Task.Delay(1);
                return email.Contains('@') && email.Contains('.')
                    ? Result.Ok(email)
                    : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Email '{email}' must contain @ and ." });
            });

        // Assert
        result.Should().BeFailure("email validation failed");
        result.Error!.Detail.Should().Contain("invalid-email");
        result.Error!.Detail.Should().Contain("must contain @ and .");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_Result_WithParam_ChainedEnsures_WorkCorrectly()
    {
        // Arrange
        var initialResult = ValueTask.FromResult(Result.Ok("valid-password-123"));

        // Act
        var result = await initialResult
            .EnsureAsync(
                pwd => ValueTask.FromResult(
                    pwd.Length >= 8
                        ? Result.Ok(pwd)
                        : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Password must be at least 8 characters" })))
            .EnsureAsync(
                pwd => ValueTask.FromResult(pwd.Any(char.IsDigit)),
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Password must contain at least one digit" });

        // Assert
        result.Should().BeSuccess("password meets all requirements")
            .Which.Should().Be("valid-password-123");
    }

    #endregion

    #region Edge Cases and Integration Tests

    [Fact]
    public async Task EnsureAsync_ValueTask_WithNullableType_HandlesNullCorrectly()
    {
        // Arrange
        int? nullValue = null;
        var initialResult = ValueTask.FromResult(Result.Ok(nullValue));

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(!value.HasValue),
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value should be null for this test" });

        // Assert
        result.Should().BeSuccess("null value passes the null check");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_WithComplexType_WorksCorrectly()
    {
        // Arrange
        var person = new Person("John", 25);
        var initialResult = ValueTask.FromResult(Result.Ok(person));

        // Act
        var result = await initialResult.EnsureAsync(
            async p =>
            {
                await Task.Delay(1);
                return p.Age >= 18;
            },
            p => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"{p.Name} must be 18 or older" });

        // Assert
        result.Should().BeSuccess("person is adult")
            .Which.Should().Be(person);
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_MultipleChainedEnsures_AllExecuteInOrder()
    {
        // Arrange
        var executionOrder = new List<int>();
        var initialResult = ValueTask.FromResult(Result.Ok(100));

        // Act
        var result = await initialResult
            .EnsureAsync(
                async value =>
                {
                    await Task.Delay(1);
                    executionOrder.Add(1);
                    return value > 0;
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "First check failed" })
            .EnsureAsync(
                async value =>
                {
                    await Task.Delay(1);
                    executionOrder.Add(2);
                    return value < 1000;
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Second check failed" })
            .EnsureAsync(
                async value =>
                {
                    await Task.Delay(1);
                    executionOrder.Add(3);
                    return value % 2 == 0;
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Third check failed" });

        // Assert
        result.Should().BeSuccess("all checks passed");
        executionOrder.Should().Equal([1, 2, 3], "ensures execute in order");
    }

    [Fact]
    public async Task EnsureAsync_ValueTask_MultipleChainedEnsures_StopsAtFirstFailure()
    {
        // Arrange
        var executionOrder = new List<int>();
        var initialResult = ValueTask.FromResult(Result.Ok(100));

        // Act
        var result = await initialResult
            .EnsureAsync(
                async value =>
                {
                    await Task.Delay(1);
                    executionOrder.Add(1);
                    return value > 0;
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "First check failed" })
            .EnsureAsync(
                async value =>
                {
                    await Task.Delay(1);
                    executionOrder.Add(2);
                    return value < 50; // This will fail
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Second check failed" })
            .EnsureAsync(
                async value =>
                {
                    await Task.Delay(1);
                    executionOrder.Add(3);
                    return value % 2 == 0;
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Third check failed" });

        // Assert
        result.Should().BeFailure("second check failed")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Second check failed" });
        executionOrder.Should().Equal([1, 2], "execution stops after first failure");
    }

    #endregion

    private record Person(string Name, int Age);
}