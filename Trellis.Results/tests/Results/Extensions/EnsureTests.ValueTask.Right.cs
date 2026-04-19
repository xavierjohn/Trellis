namespace Trellis.Results.Tests.Results.Extensions;

using Trellis;
using Trellis.Testing;

public class Ensure_ValueTask_Right_Tests
{
    #region EnsureAsync with Func<ValueTask<bool>> predicate and static Error

    [Fact]
    public async Task EnsureAsync_Right_Bool_NoParam_StaticError_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = Result.Ok("Initial value");

        // Act
        var result = await initialResult.EnsureAsync(
            () => ValueTask.FromResult(true),
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Should not fail" });

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be("Initial value");
    }

    [Fact]
    public async Task EnsureAsync_Right_Bool_NoParam_StaticError_SuccessResult_PredicateFalse_ReturnsFailure()
    {
        // Arrange
        var initialResult = Result.Ok("Initial value");

        // Act
        var result = await initialResult.EnsureAsync(
            () => ValueTask.FromResult(false),
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Predicate check failed" });

        // Assert
        result.Should().BeFailure("predicate failed")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Predicate check failed" });
    }

    [Fact]
    public async Task EnsureAsync_Right_Bool_NoParam_StaticError_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Initial error" };
        var initialResult = Result.Fail<string>(initialError);
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            () =>
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
    public async Task EnsureAsync_Right_Bool_NoParam_StaticError_WithComplexAsyncPredicate_WorksCorrectly()
    {
        // Arrange
        var initialResult = Result.Ok(42);

        // Act
        var result = await initialResult.EnsureAsync(
            () => new ValueTask<bool>(true), // Explicitly use ValueTask
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "System check failed" });

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be(42);
    }

    #endregion

    #region EnsureAsync with Func<TOk, ValueTask<bool>> predicate and static Error

    [Fact]
    public async Task EnsureAsync_Right_Bool_WithParam_StaticError_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = Result.Ok("test");

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value.Length > 0),
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value should not be empty" });

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be("test");
    }

    [Fact]
    public async Task EnsureAsync_Right_Bool_WithParam_StaticError_SuccessResult_PredicateFalse_ReturnsFailure()
    {
        // Arrange
        var initialResult = Result.Ok("abc");

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value.Length > 10),
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "String is too short" });

        // Assert
        result.Should().BeFailure("predicate failed")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "String is too short" });
    }

    [Fact]
    public async Task EnsureAsync_Right_Bool_WithParam_StaticError_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.Conflict(null, "conflict") { Detail = "Initial conflict" };
        var initialResult = Result.Fail<string>(initialError);
        var predicateInvoked = false;

        // Act
        var result = await initialResult.EnsureAsync(
            value =>
            {
                predicateInvoked = true;
                return ValueTask.FromResult(value.Length > 0);
            },
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Should not see this error" });

        // Assert
        result.Should().BeFailure("initial result is failure")
            .Which.Should().Be(initialError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    [Fact]
    public async Task EnsureAsync_Right_Bool_WithParam_StaticError_PredicateUsesValue_WorksCorrectly()
    {
        // Arrange
        var initialResult = Result.Ok(25);

        // Act
        var result = await initialResult.EnsureAsync(
            age => new ValueTask<bool>(age >= 18), // Explicitly use ValueTask
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be 18 or older" });

        // Assert
        result.Should().BeSuccess("age is valid")
            .Which.Should().Be(25);
    }

    #endregion

    #region EnsureAsync with Func<TOk, ValueTask<bool>> predicate and sync Error factory

    [Fact]
    public async Task EnsureAsync_Right_Bool_WithParam_ErrorFactory_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = Result.Ok(100);

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value >= 0),
            value => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Value {value} must be non-negative" });

        // Assert
        result.Should().BeSuccess("predicate passed")
            .Which.Should().Be(100);
    }

    [Fact]
    public async Task EnsureAsync_Right_Bool_WithParam_ErrorFactory_SuccessResult_PredicateFalse_ReturnsContextualError()
    {
        // Arrange
        var initialResult = Result.Ok(-5);

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(value >= 0),
            value => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Value {value} must be non-negative" });

        // Assert
        result.Should().BeFailure("predicate failed")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value -5 must be non-negative" });
    }

    [Fact]
    public async Task EnsureAsync_Right_Bool_WithParam_ErrorFactory_FailureResult_ErrorFactoryNotInvoked()
    {
        // Arrange
        var initialError = new Error.Unauthorized() { Detail = "Initial error" };
        var initialResult = Result.Fail<int>(initialError);
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
    public async Task EnsureAsync_Right_Bool_WithParam_ErrorFactory_ErrorFactoryReceivesValue()
    {
        // Arrange
        var initialResult = Result.Ok("test@example");
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
        result.Should().BeFailure("email validation failed");
        capturedValue.Should().Be("test@example");
        result.Error!.Detail.Should().Contain("test@example");
    }

    #endregion

    #region EnsureAsync with Func<TOk, ValueTask<bool>> predicate and async Error factory

    [Fact]
    public async Task EnsureAsync_Right_Bool_WithParam_AsyncErrorFactory_SuccessResult_PredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var initialResult = Result.Ok("valid-username");

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
    public async Task EnsureAsync_Right_Bool_WithParam_AsyncErrorFactory_SuccessResult_PredicateFalse_ReturnsAsyncError()
    {
        // Arrange
        var initialResult = Result.Ok("ab");

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
    public async Task EnsureAsync_Right_Bool_WithParam_AsyncErrorFactory_FailureResult_AsyncErrorFactoryNotInvoked()
    {
        // Arrange
        var initialError = new Error.Forbidden("authorization.forbidden") { Detail = "Initial error" };
        var initialResult = Result.Fail<string>(initialError);
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
    public async Task EnsureAsync_Right_Bool_WithParam_AsyncErrorFactory_ComplexAsyncOperation_WorksCorrectly()
    {
        // Arrange
        var initialResult = Result.Ok(DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await initialResult.EnsureAsync(
            value => new ValueTask<bool>(value <= DateTime.UtcNow), // Explicitly use ValueTask
            async value =>
            {
                await Task.Delay(1); // Simulate async error lookup
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Date {value:yyyy-MM-dd} cannot be in the future" };
            });

        // Assert
        result.Should().BeSuccess("date is in the past");
    }

    #endregion

    #region EnsureAsync with Func<ValueTask<Result<TOk>>> predicate (no parameter)

    [Fact]
    public async Task EnsureAsync_Right_Result_NoParam_SuccessResult_PredicateSuccess_ReturnsOriginalSuccess()
    {
        // Arrange
        var initialResult = Result.Ok("Initial message");

        // Act
        var result = await initialResult.EnsureAsync(
            () => ValueTask.FromResult(Result.Ok<string>("Validation passed")));

        // Assert
        result.Should().BeSuccess("initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task EnsureAsync_Right_Result_NoParam_SuccessResult_PredicateFailure_ReturnsPredicateError()
    {
        // Arrange
        var initialResult = Result.Ok("Initial value");
        var predicateError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "System validation failed" };

        // Act
        var result = await initialResult.EnsureAsync(
            () => ValueTask.FromResult(Result.Fail<string>(predicateError)));

        // Assert
        result.Should().BeFailure("predicate returned failure")
            .Which.Should().Be(predicateError);
    }

    [Fact]
    public async Task EnsureAsync_Right_Result_NoParam_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.TooManyRequests() { Detail = "Initial error" };
        var initialResult = Result.Fail<string>(initialError);
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
    public async Task EnsureAsync_Right_Result_NoParam_ComplexAsyncPredicate_WorksCorrectly()
    {
        // Arrange
        var initialResult = Result.Ok(42);

        // Act
        var result = await initialResult.EnsureAsync(
            () => new ValueTask<Result<int>>(
                Random.Shared.Next(0, 100) >= 0
                    ? Result.Ok(Random.Shared.Next(0, 100))
                    : Result.Fail<int>(new Error.ServiceUnavailable() { Detail = "System unavailable" })));

        // Assert
        result.Should().BeSuccess("system check passed")
            .Which.Should().Be(42, "original value is preserved");
    }

    #endregion

    #region EnsureAsync with Func<TOk, ValueTask<Result<TOk>>> predicate (with parameter)

    [Fact]
    public async Task EnsureAsync_Right_Result_WithParam_SuccessResult_PredicateSuccess_ReturnsOriginalSuccess()
    {
        // Arrange
        var initialResult = Result.Ok("Initial message");

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(Result.Ok<string>($"Validated: {value}")));

        // Assert
        result.Should().BeSuccess("initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task EnsureAsync_Right_Result_WithParam_SuccessResult_PredicateFailure_ReturnsPredicateError()
    {
        // Arrange
        var initialResult = Result.Ok(15);
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
    public async Task EnsureAsync_Right_Result_WithParam_FailureResult_PredicateNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var initialError = new Error.Conflict(null, "domain.violation") { Detail = "Initial domain error" };
        var initialResult = Result.Fail<int>(initialError);
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
    public async Task EnsureAsync_Right_Result_WithParam_PredicateUsesValue_WorksCorrectly()
    {
        // Arrange
        var initialResult = Result.Ok("user@example.com");

        // Act
        var result = await initialResult.EnsureAsync(
            email => new ValueTask<Result<string>>(
                email.Contains('@') && email.Contains('.')
                    ? Result.Ok(email)
                    : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Email '{email}' is invalid" })));

        // Assert
        result.Should().BeSuccess("email is valid")
            .Which.Should().Be("user@example.com");
    }

    [Fact]
    public async Task EnsureAsync_Right_Result_WithParam_PredicateFailsWithContextualError_ReturnsContextualError()
    {
        // Arrange
        var initialResult = Result.Ok("invalid-email");

        // Act
        var result = await initialResult.EnsureAsync(
            email => new ValueTask<Result<string>>(
                email.Contains('@') && email.Contains('.')
                    ? Result.Ok(email)
                    : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Email '{email}' must contain @ and ." })));

        // Assert
        result.Should().BeFailure("email validation failed");
        result.Error!.Detail.Should().Contain("invalid-email");
        result.Error!.Detail.Should().Contain("must contain @ and .");
    }

    [Fact]
    public async Task EnsureAsync_Right_Result_WithParam_ChainedEnsures_WorkCorrectly()
    {
        // Arrange
        var initialResult = Result.Ok("valid-password-123");

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
    public async Task EnsureAsync_Right_WithNullableType_HandlesNullCorrectly()
    {
        // Arrange
        int? nullValue = null;
        var initialResult = Result.Ok(nullValue);

        // Act
        var result = await initialResult.EnsureAsync(
            value => ValueTask.FromResult(!value.HasValue),
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value should be null for this test" });

        // Assert
        result.Should().BeSuccess("null value passes the null check");
    }

    [Fact]
    public async Task EnsureAsync_Right_WithComplexType_WorksCorrectly()
    {
        // Arrange
        var person = new Person("John", 25);
        var initialResult = Result.Ok(person);

        // Act
        var result = await initialResult.EnsureAsync(
            p => new ValueTask<bool>(p.Age >= 18), // Explicitly use ValueTask
            p => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"{p.Name} must be 18 or older" });

        // Assert
        result.Should().BeSuccess("person is adult")
            .Which.Should().Be(person);
    }

    [Fact]
    public async Task EnsureAsync_Right_MultipleChainedEnsures_AllExecuteInOrder()
    {
        // Arrange
        var executionOrder = new List<int>();
        var initialResult = Result.Ok(100);

        // Act
        var result = await initialResult
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(1);
                    return new ValueTask<bool>(value > 0); // Explicitly use ValueTask
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "First check failed" })
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(2);
                    return new ValueTask<bool>(value < 1000); // Explicitly use ValueTask
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Second check failed" })
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(3);
                    return new ValueTask<bool>(value % 2 == 0); // Explicitly use ValueTask
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Third check failed" });

        // Assert
        result.Should().BeSuccess("all checks passed");
        executionOrder.Should().Equal([1, 2, 3], "ensures execute in order");
    }

    [Fact]
    public async Task EnsureAsync_Right_MultipleChainedEnsures_StopsAtFirstFailure()
    {
        // Arrange
        var executionOrder = new List<int>();
        var initialResult = Result.Ok(100);

        // Act
        var result = await initialResult
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(1);
                    return new ValueTask<bool>(value > 0); // Explicitly use ValueTask
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "First check failed" })
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(2);
                    return new ValueTask<bool>(value < 50); // This will fail - Explicitly use ValueTask
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Second check failed" })
            .EnsureAsync(
                value =>
                {
                    executionOrder.Add(3);
                    return new ValueTask<bool>(value % 2 == 0); // Explicitly use ValueTask
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Third check failed" });

        // Assert
        result.Should().BeFailure("second check failed")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Second check failed" });
        executionOrder.Should().Equal([1, 2], "execution stops after first failure");
    }

    [Fact]
    public async Task EnsureAsync_Right_WithAsyncException_ThrowsException()
    {
        // Arrange
        var initialResult = Result.Ok("test");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await initialResult.EnsureAsync(
                value =>
                {
                    throw new InvalidOperationException("Simulated async failure");
#pragma warning disable CS0162 // Unreachable code detected
                    return new ValueTask<bool>(true);
#pragma warning restore CS0162 // Unreachable code detected
                },
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Should not reach" }));
    }

    [Fact]
    public async Task EnsureAsync_Right_MixingResultAndBooleanPredicates_WorksCorrectly()
    {
        // Arrange
        var initialResult = Result.Ok("valid-input");

        // Act
        var result = await initialResult
            .EnsureAsync(
                value => new ValueTask<bool>(value.Length >= 5), // Explicitly use ValueTask
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Too short" })
            .EnsureAsync(
                value => new ValueTask<Result<string>>(
                    value.Contains("valid")
                        ? Result.Ok(value)
                        : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must contain 'valid'" }))); // Explicitly use ValueTask

        // Assert
        result.Should().BeSuccess("all validations passed")
            .Which.Should().Be("valid-input");
    }

    #endregion

    private record Person(string Name, int Age);
}