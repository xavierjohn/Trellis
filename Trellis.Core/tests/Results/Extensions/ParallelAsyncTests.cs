namespace Trellis.Core.Tests.Results.Extensions;

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
        result.Should().BeSuccess().Which.Should().Be((1, 2));
    }

    [Fact]
    public async Task ParallelAsync_2Tuple_FirstFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "First failed" }, 10),
            () => CreateDelayedSuccessTask(2, 20)
        ).WhenAllAsync();

        // Assert
        result.Should().HaveErrorDetail("First failed").And.BeFailureOfType<Error.InvalidInput>();
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
        result.Should().BeSuccess().Which.Should().Be(("text", 42));
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
        var error = result.Should().HaveErrorCode("unhandled-exception").And.BeFailureOfType<Error.Unexpected>().Which;
        error.FaultId.Should().NotBeNullOrWhiteSpace();
        result.Should().HaveErrorDetail("An unexpected error occurred while processing the request.");
        error.Detail.Should().NotContain("sync boom");
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
        var aggregateError = result.Should().BeFailureOfType<Error.Aggregate>().Which;
        aggregateError.Errors.Items.Should().HaveCount(2);
        var firstError = aggregateError.Errors.Items[0].Should().BeOfType<Error.Unexpected>().Subject;
        firstError.Code.Should().Be("unhandled-exception");
        firstError.FaultId.Should().NotBeNullOrWhiteSpace();
        firstError.Detail.Should().Be("An unexpected error occurred while processing the request.");
        firstError.Detail.Should().NotContain("first sync boom");

        var secondError = aggregateError.Errors.Items[1].Should().BeOfType<Error.Unexpected>().Subject;
        secondError.Code.Should().Be("unhandled-exception");
        secondError.FaultId.Should().NotBeNullOrWhiteSpace();
        secondError.Detail.Should().Be("An unexpected error occurred while processing the request.");
        secondError.Detail.Should().NotContain("second sync boom");
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
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3));
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_FirstFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "First failed" }, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedSuccessTask(3, 15)
        ).WhenAllAsync();

        // Assert
        result.Should().HaveErrorDetail("First failed").And.BeFailureOfType<Error.InvalidInput>();
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_SecondFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedFailureTask<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Second not found" }, 20),
            () => CreateDelayedSuccessTask(3, 15)
        ).WhenAllAsync();

        // Assert
        result.Should().HaveErrorDetail("Second not found").And.BeFailureOfType<Error.NotFound>();
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_ThirdFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedFailureTask<int>(new Error.Forbidden("authorization.forbidden") { Detail = "Third forbidden" }, 15)
        ).WhenAllAsync();

        // Assert
        result.Should().HaveErrorDetail("Third forbidden").And.BeFailureOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_AllFail_CombinesErrors()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field1"), ValidationCodes.Unspecified) { Detail = "First invalid" })), 10),
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field2"), ValidationCodes.Unspecified) { Detail = "Second invalid" })), 20),
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field3"), ValidationCodes.Unspecified) { Detail = "Third invalid" })), 15)
        ).WhenAllAsync();

        // Assert
        var validationError = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        validationError.Should().HaveFieldViolationCount(3);
        validationError.Fields.Items[0].Field.Path.Should().Be("/field1");
        validationError.Fields.Items[1].Field.Path.Should().Be("/field2");
        validationError.Fields.Items[2].Field.Path.Should().Be("/field3");
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_MixedErrorTypes_ReturnsAggregateError()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Validation error" }, 10),
            () => CreateDelayedFailureTask<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }, 20),
            () => CreateDelayedSuccessTask(3, 15)
        ).WhenAllAsync();

        // Assert
        var aggregateError = result.Should().BeFailureOfType<Error.Aggregate>().Which;
        aggregateError.Errors.Items.Should().HaveCount(2);
        aggregateError.Errors.Items[0].Should().BeOfType<Error.InvalidInput>();
        aggregateError.Errors.Items[1].Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_ExecutesInParallel()
    {
        // Proves overlap instead of timing it. Each operation blocks until all three have started, so a
        // sequential implementation can never complete (the first would wait forever) while a parallel one
        // completes regardless of how loaded the machine is. The previous wall-clock assertion failed on a
        // busy agent even though the code was correct.
        using var barrier = new TestGate();

        async Task<Result<int>> Operation(int value)
        {
            barrier.Signal();

            // The timeout converts a sequential regression into a clear failure rather than a hung run.
            await barrier.WaitForArrivalsAsync(3, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            return Result.Ok(value);
        }

        var result = await Result.ParallelAsync(
            () => Operation(1),
            () => Operation(2),
            () => Operation(3)
        ).WhenAllAsync();

        result.Should().BeSuccess();
        barrier.ArrivalCount.Should().Be(3, "every operation must have been started before any of them completed");
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
        result.Should().BeSuccess().Which.Should().Be(("text", 42, true));
        result.Unwrap().Item1.Should().Be("text");
        result.Unwrap().Item2.Should().Be(42);
        result.Unwrap().Item3.Should().BeTrue();
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
        result.Should().BeSuccess().Which.Should().Be(60);
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
        result.Should().BeSuccess().Which.Should().Be("1-2-3");
    }

    [Fact]
    public async Task ParallelAsync_3Tuple_FailureShortCircuitsBind()
    {
        // Arrange
        var bindCalled = false;

        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(10, 10),
            () => CreateDelayedFailureTask<int>(new Error.Unexpected("test") { Detail = "Task failed" }, 20),
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
        result.Should().BeFailureOfType<Error.Unexpected>();
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
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4));
    }

    [Fact]
    public async Task ParallelAsync_4Tuple_OneFails_ReturnsFailure()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedSuccessTask(2, 20),
            () => CreateDelayedFailureTask<int>(new Error.Conflict(Resource: null, Code: "conflict") { Detail = "Conflict occurred" }, 15),
            () => CreateDelayedSuccessTask(4, 25)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailureOfType<Error.Conflict>();
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
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5));
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
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5, 6));
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
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5, 6, 7));
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
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5, 6, 7, 8));
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
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5, 6, 7, 8, 9));
    }

    [Fact]
    public async Task ParallelAsync_9Tuple_MultipleFail_CombinesErrors()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask(1, 10),
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Error 2" }, 20),
            () => CreateDelayedSuccessTask(3, 15),
            () => CreateDelayedSuccessTask(4, 25),
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Error 5" }, 30),
            () => CreateDelayedSuccessTask(6, 35),
            () => CreateDelayedSuccessTask(7, 40),
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Error 8" }, 45),
            () => CreateDelayedSuccessTask(9, 50)
        ).WhenAllAsync();

        // Assert
        result.Should().BeFailureOfType<Error.InvalidInput>();
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
        result.Should().BeSuccess().Which.Profile.Should().Be("John Doe");
        result.Unwrap().OrderCount.Should().Be(2);
        result.Unwrap().Theme.Should().Be("Dark Mode");
    }

    [Fact]
    public async Task ParallelAsync_ValidationScenario_CombinesAllErrors()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedFailureTask<string>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.Unspecified) { Detail = "Invalid email format" })), 10),
            () => CreateDelayedFailureTask<string>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("phone"), ValidationCodes.Unspecified) { Detail = "Invalid phone number" })), 15),
            () => CreateDelayedFailureTask<int>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("age"), ValidationCodes.Unspecified) { Detail = "Age must be 18+" })), 20)
        ).WhenAllAsync();

        // Assert
        var validationError = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        validationError.Should().HaveFieldViolationCount(3);
    }

    [Fact]
    public async Task ParallelAsync_FraudDetection_AllChecksMustPass()
    {
        // Act
        var result = await Result.ParallelAsync(
            () => CreateDelayedSuccessTask("Clear", 25),
            () => CreateDelayedSuccessTask("Normal", 30),
            () => CreateDelayedFailureTask<string>(new Error.Forbidden("authorization.forbidden") { Detail = "Suspicious location detected" }, 20)
        ).WhenAllAsync();

        // Assert - Should fail if any check fails
        result.Should().HaveErrorDetailContaining("Suspicious location").And.BeFailureOfType<Error.Forbidden>();
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