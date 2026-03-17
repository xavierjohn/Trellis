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

    [Theory]
    [MemberData(nameof(SuccessfulWhenAllAsyncScenarios))]
    public async Task WhenAllAsync_SuccessfulScenarios_CreateActivity_WithCorrectName(string _, Func<Task> act)
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        await act();

        // Assert
        var activity = activityTest.AssertActivityCaptured("WhenAllAsync");
        activity.DisplayName.Should().Be("WhenAllAsync");
    }

    #endregion

    #region Activity Status Tests - Success

    [Theory]
    [MemberData(nameof(SuccessfulWhenAllAsyncScenarios))]
    public async Task WhenAllAsync_SuccessfulScenarios_LogOkStatus(string _, Func<Task> act)
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        await act();

        // Assert
        activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Ok);
    }

    #endregion

    #region Activity Status Tests - Failure

    [Theory]
    [MemberData(nameof(FailingWhenAllAsyncScenarios))]
    public async Task WhenAllAsync_FailureScenarios_LogErrorStatus(string _, Func<Task> act)
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        await act();

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

    [Fact]
    public async Task WhenAllAsync_2Tuple_FaultedTask_DoesNotRecordExceptionMessageInStatusDescription()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        const string sensitiveMessage = "password=hunter2";
        var task1 = Task.FromException<Result<int>>(new InvalidOperationException(sensitiveMessage));
        var task2 = Task.FromResult(Result.Success(2));

        // Act
        var act = () => (task1, task2).WhenAllAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        var activity = activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Error);
        activity.StatusDescription.Should().BeNullOrEmpty();
        activity.Tags.Should().Contain(tag => tag.Key == "error.type" && tag.Value == nameof(InvalidOperationException));
        activity.StatusDescription.Should().NotContain(sensitiveMessage);
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

    public static TheoryData<string, Func<Task>> SuccessfulWhenAllAsyncScenarios()
    {
        var data = new TheoryData<string, Func<Task>>();

        data.Add("2-tuple success", async () => await (Task.FromResult(Result.Success(1)), Task.FromResult(Result.Success(2))).WhenAllAsync());
        data.Add("3-tuple success", async () => await (Task.FromResult(Result.Success(1)), Task.FromResult(Result.Success(2)), Task.FromResult(Result.Success(3))).WhenAllAsync());
        data.Add("9-tuple success", async () => await (
            Task.FromResult(Result.Success(1)),
            Task.FromResult(Result.Success(2)),
            Task.FromResult(Result.Success(3)),
            Task.FromResult(Result.Success(4)),
            Task.FromResult(Result.Success(5)),
            Task.FromResult(Result.Success(6)),
            Task.FromResult(Result.Success(7)),
            Task.FromResult(Result.Success(8)),
            Task.FromResult(Result.Success(9))).WhenAllAsync());

        return data;
    }

    public static TheoryData<string, Func<Task>> FailingWhenAllAsyncScenarios()
    {
        var data = new TheoryData<string, Func<Task>>();

        data.Add("2-tuple first failure", async () => await (Task.FromResult(Result.Failure<int>(Error.NotFound("Not found"))), Task.FromResult(Result.Success(2))).WhenAllAsync());
        data.Add("3-tuple one failure", async () => await (Task.FromResult(Result.Success(1)), Task.FromResult(Result.Failure<int>(Error.Validation("Invalid"))), Task.FromResult(Result.Success(3))).WhenAllAsync());
        data.Add("3-tuple all failure", async () => await (
            Task.FromResult(Result.Failure<int>(Error.Validation("Error 1"))),
            Task.FromResult(Result.Failure<int>(Error.Validation("Error 2"))),
            Task.FromResult(Result.Failure<int>(Error.Validation("Error 3")))).WhenAllAsync());
        data.Add("9-tuple multiple failure", async () => await (
            Task.FromResult(Result.Success(1)),
            Task.FromResult(Result.Failure<int>(Error.Validation("Error 2"))),
            Task.FromResult(Result.Success(3)),
            Task.FromResult(Result.Success(4)),
            Task.FromResult(Result.Failure<int>(Error.NotFound("Error 5"))),
            Task.FromResult(Result.Success(6)),
            Task.FromResult(Result.Success(7)),
            Task.FromResult(Result.Failure<int>(Error.Conflict("Error 8"))),
            Task.FromResult(Result.Success(9))).WhenAllAsync());

        return data;
    }
}