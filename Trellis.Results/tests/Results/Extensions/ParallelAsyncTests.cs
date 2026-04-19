namespace Trellis.Results.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Testing;

/// <summary>
/// Functional tests for ParallelAsync operations.
/// Tests static Result.ParallelAsync methods for eager task creation with factory functions.
/// </summary>
public class ParallelAsyncTests : TestBase
{
    #region 2-Tuple ParallelAsync Tests

    [Fact]
    public void ParallelAsync_2Tuple_NullFactory_ThrowsArgumentNullException()
    {
        Func<Task<Result<int>>> taskFactory1 = null!;
        Func<Task<Result<int>>> taskFactory2 = () => CreateDelayedSuccessTask(2, 20);

        var act = () => Result.ParallelAsync(taskFactory1, taskFactory2);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "taskFactory1");
    }

    [Fact]
    public async Task ParallelAsync_2Tuple_AllSuccess_ReturnsCombinedTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be((1, 2));
    }

    [Fact]
    public async Task ParallelAsync_2Tuple_FirstFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<int>(Error.Validation("First failed"), 10),
            () => CreateDelayedSuccessTask(2, 20)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ValidationError>()
            .Which.Detail.Should().Be("First failed");
    }

    [Fact]
    public async Task ParallelAsync_2Tuple_DifferentTypes_ReturnsTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask("text", 10),
            () => CreateDelayedSuccessTask(42, 20)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be(("text", 42));
    }

    [Fact]
    public void ParallelAsync_2Tuple_OneFactoryThrows_SurfacesTheThrownException()
    {
        // Arrange
        var secondFactoryInvoked = false;

        // Act
        Action act = () => Result.ParallelAsync<int, int>(
            () => throw new InvalidOperationException("factory blew up"),
            () =>
            {
                secondFactoryInvoked = true;
                return CreateDelayedSuccessTask(2, 10);
            });

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("factory blew up");
        secondFactoryInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ParallelAsync_2Tuple_WithResultTry_ConvertsSynchronousExceptionToUnexpectedFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => Task.FromResult(Result.Try<int>(() => throw new InvalidOperationException("sync boom"))),
            () => Task.FromResult(Result.Try(() => 2))
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<UnexpectedError>()
            .Which.Detail.Should().Be("sync boom");
    }

    [Fact]
    public async Task ParallelAsync_2Tuple_WithResultTry_BothSynchronousExceptions_AggregatesFailures()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => Task.FromResult(Result.Try<int>(() => throw new InvalidOperationException("first sync boom"))),
            () => Task.FromResult(Result.Try<int>(() => throw new InvalidOperationException("second sync boom")))
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<AggregateError>();
        var aggregateError = (AggregateError)result.Error;
        aggregateError.Errors.Should().HaveCount(2);
        aggregateError.Errors[0].Should().BeOfType<UnexpectedError>()
            .Which.Detail.Should().Be("first sync boom");
        aggregateError.Errors[1].Should().BeOfType<UnexpectedError>()
            .Which.Detail.Should().Be("second sync boom");
    }

    #endregion

    #region 3-Tuple ParallelAsync Tests (Comprehensive Coverage)

    [Fact]
    public async Task ParallelAsync_3Tuple_AllSuccess_ReturnsCombinedTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be((1, 2, 3));
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_FirstFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<int>(Error.Validation("First failed"), 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ValidationError>()
            .Which.Detail.Should().Be("First failed");
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_SecondFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedFailureTask<int>(Error.NotFound("Second not found"), 20),
            () => CreateDelayedSuccessTask(3, 15)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<NotFoundError>()
            .Which.Detail.Should().Be("Second not found");
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_ThirdFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedFailureTask<int>(Error.Forbidden("Third forbidden"), 15)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ForbiddenError>()
            .Which.Detail.Should().Be("Third forbidden");
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_AllFail_CombinesErrors()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<int>(Error.Validation("First invalid", "field1"), 10),
            () => CreateDelayedFailureTask<int>(Error.Validation("Second invalid", "field2"), 20),
            () => CreateDelayedFailureTask<int>(Error.Validation("Third invalid", "field3"), 15)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ValidationError>();
        var validationError = (ValidationError)result.Error;
        validationError.FieldErrors.Should().HaveCount(3);
        validationError.FieldErrors[0].FieldName.Should().Be("field1");
        validationError.FieldErrors[1].FieldName.Should().Be("field2");
        validationError.FieldErrors[2].FieldName.Should().Be("field3");
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_MixedErrorTypes_ReturnsAggregateError()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<int>(Error.Validation("Validation error"), 10),
            () => CreateDelayedFailureTask<int>(Error.NotFound("Not found"), 20),
            () => CreateDelayedSuccessTask(3, 15)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<AggregateError>();
        var aggregateError = (AggregateError)result.Error;
        aggregateError.Errors.Should().HaveCount(2);
        aggregateError.Errors[0].Should().BeOfType<ValidationError>();
        aggregateError.Errors[1].Should().BeOfType<NotFoundError>();
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_ExecutesInParallel()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 50),
            () => CreateDelayedSuccessTask(2, 50),
            () => CreateDelayedSuccessTask(3, 50)
        ).WhenAllAsync();

        stopwatch.Stop();

        // Assert
        result.Should().BeSuccess();
        // If run sequentially, would take 150ms+. In parallel, should be ~50ms
        // Allow generous margin for CI/slow environments
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(120);
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_DifferentTypes_ReturnsTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask("text", 10),
            () => CreateDelayedSuccessTask(42, 20),
            () => CreateDelayedSuccessTask(true, 15)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be(("text", 42, true));
        result.Value.Item1.Should().Be("text");
        result.Value.Item2.Should().Be(42);
        result.Value.Item3.Should().BeTrue();
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_WithBind_ProcessesCombinedResults()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(10, 10),
            () => CreateDelayedSuccessTask(20, 20),
            () => CreateDelayedSuccessTask(30, 15)
        )
        .WhenAllAsync()
        .BindAsync((a, b, c) => Result.Ok(a + b + c));

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be(60);
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_WithMap_TransformsResult()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15)
        )
        .WhenAllAsync()
        .MapAsync(tuple => $"{tuple.Item1}-{tuple.Item2}-{tuple.Item3}");

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be("1-2-3");
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_FailureShortCircuitsBind()
    {
        // Arrange
        var bindCalled = false;

        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(10, 10),
            () => CreateDelayedFailureTask<int>(Error.Unexpected("Task failed"), 20),
            () => CreateDelayedSuccessTask(30, 15)
        )
        .WhenAllAsync()
        .BindAsync((a, b, c) =>
        {
            bindCalled = true;
            return Result.Ok(a + b + c);
        });

        // Assert
        bindCalled.Should().BeFalse();
        result.Should().BeFailure();
        result.Error.Should().BeOfType<UnexpectedError>();
    }

    #endregion

    #region Other Tuple Sizes (Validation Tests)

    [Fact]
    public async Task ParallelAsync_4Tuple_AllSuccess_ReturnsCombinedTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15),
            () => CreateDelayedSuccessTask(4, 25)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be((1, 2, 3, 4));
    }

    [Fact]
    public async Task ParallelAsync_4Tuple_OneFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedFailureTask<int>(Error.Conflict("Conflict occurred"), 15),
            () => CreateDelayedSuccessTask(4, 25)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task ParallelAsync_5Tuple_AllSuccess_ReturnsCombinedTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15),
            () => CreateDelayedSuccessTask(4, 25),
            () => CreateDelayedSuccessTask(5, 30)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be((1, 2, 3, 4, 5));
    }

    [Fact]
    public async Task ParallelAsync_6Tuple_AllSuccess_ReturnsCombinedTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15),
            () => CreateDelayedSuccessTask(4, 25),
            () => CreateDelayedSuccessTask(5, 30),
            () => CreateDelayedSuccessTask(6, 35)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be((1, 2, 3, 4, 5, 6));
    }

    [Fact]
    public async Task ParallelAsync_7Tuple_AllSuccess_ReturnsCombinedTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15),
            () => CreateDelayedSuccessTask(4, 25),
            () => CreateDelayedSuccessTask(5, 30),
            () => CreateDelayedSuccessTask(6, 35),
            () => CreateDelayedSuccessTask(7, 40)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be((1, 2, 3, 4, 5, 6, 7));
    }

    [Fact]
    public async Task ParallelAsync_8Tuple_AllSuccess_ReturnsCombinedTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15),
            () => CreateDelayedSuccessTask(4, 25),
            () => CreateDelayedSuccessTask(5, 30),
            () => CreateDelayedSuccessTask(6, 35),
            () => CreateDelayedSuccessTask(7, 40),
            () => CreateDelayedSuccessTask(8, 45)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be((1, 2, 3, 4, 5, 6, 7, 8));
    }

    [Fact]
    public async Task ParallelAsync_9Tuple_AllSuccess_ReturnsCombinedTuple()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15),
            () => CreateDelayedSuccessTask(4, 25),
            () => CreateDelayedSuccessTask(5, 30),
            () => CreateDelayedSuccessTask(6, 35),
            () => CreateDelayedSuccessTask(7, 40),
            () => CreateDelayedSuccessTask(8, 45),
            () => CreateDelayedSuccessTask(9, 50)
        ).WhenAllAsync();

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be((1, 2, 3, 4, 5, 6, 7, 8, 9));
    }

    [Fact]
    public async Task ParallelAsync_9Tuple_MultipleFail_CombinesErrors()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedFailureTask<int>(Error.Validation("Error 2"), 20),
            () => CreateDelayedSuccessTask(3, 15),
            () => CreateDelayedSuccessTask(4, 25),
            () => CreateDelayedFailureTask<int>(Error.Validation("Error 5"), 30),
            () => CreateDelayedSuccessTask(6, 35),
            () => CreateDelayedSuccessTask(7, 40),
            () => CreateDelayedFailureTask<int>(Error.Validation("Error 8"), 45),
            () => CreateDelayedSuccessTask(9, 50)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ValidationError>();
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public async Task ParallelAsync_FetchUserData_AllSucceed()
    {
        // Arrange
        var orders = new[] { "Order1", "Order2" };

        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask("John Doe", 30),
            () => CreateDelayedSuccessTask(orders, 40),
            () => CreateDelayedSuccessTask("Dark Mode", 20)
        )
        .WhenAllAsync()
        .MapAsync(data => new
        {
            Profile = data.Item1,
            OrderCount = data.Item2.Length,
            Theme = data.Item3
        });

        // Assert
        result.Should().BeSuccess();
        result.Value.Profile.Should().Be("John Doe");
        result.Value.OrderCount.Should().Be(2);
        result.Value.Theme.Should().Be("Dark Mode");
    }

    [Fact]
    public async Task ParallelAsync_ValidationScenario_CombinesAllErrors()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<string>(Error.Validation("Invalid email format", "email"), 10),
            () => CreateDelayedFailureTask<string>(Error.Validation("Invalid phone number", "phone"), 15),
            () => CreateDelayedFailureTask<int>(Error.Validation("Age must be 18+", "age"), 20)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ValidationError>();
        var validationError = (ValidationError)result.Error;
        validationError.FieldErrors.Should().HaveCount(3);
    }

    [Fact]
    public async Task ParallelAsync_FraudDetection_AllChecksMustPass()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask("Clear", 25),
            () => CreateDelayedSuccessTask("Normal", 30),
            () => CreateDelayedFailureTask<string>(Error.Forbidden("Suspicious location detected"), 20)
        ).WhenAllAsync();

        // Assert - Should fail if any check fails
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ForbiddenError>();
        result.Error.Detail.Should().Contain("Suspicious location");
    }

    #endregion

    #region Helper Methods

    private static async Task<Result<T>> CreateDelayedSuccessTask<T>(T value, int delayMs)
    {
        await Task.Delay(delayMs);
        return Result.Ok(value);
    }

    private static async Task<Result<T>> CreateDelayedFailureTask<T>(Error error, int delayMs)
    {
        await Task.Delay(delayMs);
        return Result.Fail<T>(error);
    }

    #endregion
}