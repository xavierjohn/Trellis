namespace Trellis.Results.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Results.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Tests for Activity tracing in WhenAllAsync operations.
/// Verifies that WhenAllAsync creates proper distributed tracing spans for observability.
/// </summary>
public class WhenAllAsyncTracingTests : TestBase
{
    #region Activity Creation Tests

    [Fact]
    public async Task WhenAllAsync_2Tuple_CreatesActivity_WithCorrectName()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));

        // Act
        await (task1, task2).WhenAllAsync();

        // Assert
        var activity = activityTest.AssertActivityCaptured("WhenAllAsync");
        activity.DisplayName.Should().Be("WhenAllAsync");
    }

    [Fact]
    public async Task WhenAllAsync_3Tuple_CreatesActivity_WithCorrectName()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));

        // Act
        await (task1, task2, task3).WhenAllAsync();

        // Assert
        var activity = activityTest.AssertActivityCaptured("WhenAllAsync");
        activity.DisplayName.Should().Be("WhenAllAsync");
    }

    [Fact]
    public async Task WhenAllAsync_9Tuple_CreatesActivity_WithCorrectName()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));
        var task4 = Task.FromResult(Result.Success(4));
        var task5 = Task.FromResult(Result.Success(5));
        var task6 = Task.FromResult(Result.Success(6));
        var task7 = Task.FromResult(Result.Success(7));
        var task8 = Task.FromResult(Result.Success(8));
        var task9 = Task.FromResult(Result.Success(9));

        // Act
        await (task1, task2, task3, task4, task5, task6, task7, task8, task9).WhenAllAsync();

        // Assert
        var activity = activityTest.AssertActivityCaptured("WhenAllAsync");
        activity.DisplayName.Should().Be("WhenAllAsync");
    }

    #endregion

    #region Activity Status Tests - Success

    [Fact]
    public async Task WhenAllAsync_2Tuple_AllSuccess_LogsOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));

        // Act
        await (task1, task2).WhenAllAsync();

        // Assert
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task WhenAllAsync_3Tuple_AllSuccess_LogsOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));

        // Act
        await (task1, task2, task3).WhenAllAsync();

        // Assert
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task WhenAllAsync_9Tuple_AllSuccess_LogsOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));
        var task4 = Task.FromResult(Result.Success(4));
        var task5 = Task.FromResult(Result.Success(5));
        var task6 = Task.FromResult(Result.Success(6));
        var task7 = Task.FromResult(Result.Success(7));
        var task8 = Task.FromResult(Result.Success(8));
        var task9 = Task.FromResult(Result.Success(9));

        // Act
        await (task1, task2, task3, task4, task5, task6, task7, task8, task9).WhenAllAsync();

        // Assert
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Ok);
    }

    #endregion

    #region Activity Status Tests - Failure

    [Fact]
    public async Task WhenAllAsync_2Tuple_FirstFails_LogsErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Failure<int>(Error.NotFound("Not found")));
        var task2 = Task.FromResult(Result.Success(2));

        // Act
        await (task1, task2).WhenAllAsync();

        // Assert
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Error);
    }

    [Fact]
    public async Task WhenAllAsync_3Tuple_OneFails_LogsErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Failure<int>(Error.Validation("Invalid")));
        var task3 = Task.FromResult(Result.Success(3));

        // Act
        await (task1, task2, task3).WhenAllAsync();

        // Assert
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Error);
    }

    [Fact]
    public async Task WhenAllAsync_3Tuple_AllFail_LogsErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Failure<int>(Error.Validation("Error 1")));
        var task2 = Task.FromResult(Result.Failure<int>(Error.Validation("Error 2")));
        var task3 = Task.FromResult(Result.Failure<int>(Error.Validation("Error 3")));

        // Act
        await (task1, task2, task3).WhenAllAsync();

        // Assert
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Error);
    }

    [Fact]
    public async Task WhenAllAsync_9Tuple_MultipleFail_LogsErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Failure<int>(Error.Validation("Error 2")));
        var task3 = Task.FromResult(Result.Success(3));
        var task4 = Task.FromResult(Result.Success(4));
        var task5 = Task.FromResult(Result.Failure<int>(Error.NotFound("Error 5")));
        var task6 = Task.FromResult(Result.Success(6));
        var task7 = Task.FromResult(Result.Success(7));
        var task8 = Task.FromResult(Result.Failure<int>(Error.Conflict("Error 8")));
        var task9 = Task.FromResult(Result.Success(9));

        // Act
        await (task1, task2, task3, task4, task5, task6, task7, task8, task9).WhenAllAsync();

        // Assert
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Error);
    }

    [Fact]
    public async Task WhenAllAsync_2Tuple_FaultedTask_LogsErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromException<Result<int>>(new InvalidOperationException("boom"));
        var task2 = Task.FromResult(Result.Success(2));

        // Act
        var act = () => (task1, task2).WhenAllAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Error);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public async Task WhenAllAsync_3Tuple_ChainedWithBind_CreatesMultipleActivities()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var task1 = Task.FromResult(Result.Success(10));
        var task2 = Task.FromResult(Result.Success(20));
        var task3 = Task.FromResult(Result.Success(30));

        // Act
        await (task1, task2, task3)
            .WhenAllAsync()
            .BindAsync((a, b, c) => Result.Success(a + b + c));

        // Assert
        // Should have both WhenAllAsync and Bind activities
        activityTest.AssertActivityCaptured("WhenAllAsync", 1);
        activityTest.AssertActivityCaptured("Bind", 1);
    }

    #endregion
}