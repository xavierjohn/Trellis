namespace Trellis.Results.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Results.Tests.Helpers;

public class DebugTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Debug_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.Debug();

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Debug_with_message_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.Debug("Test message");

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugDetailed_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.DebugDetailed();

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugDetailed_with_message_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.DebugDetailed("Test message");

        returned.Should().Be(result);
    }

    [Fact]
    public void DebugDetailed_with_validation_error_returns_same_result()
    {
        var validationError = Error.Validation("Test error", "TestField");
        Result<T> result = Result.Fail<T>(validationError);

        var returned = result.DebugDetailed("Validation test");

        returned.Should().Be(result);
    }

    [Fact]
    public void DebugDetailed_with_aggregate_error_returns_same_result()
    {
        var aggregateError = new AggregateError([Error1, Error2]);
        Result<T> result = Result.Fail<T>(aggregateError);

        var returned = result.DebugDetailed("Aggregate test");

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugWithStack_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.DebugWithStack();

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugWithStack_with_message_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.DebugWithStack("Test message");

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugWithStack_without_stack_trace_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.DebugWithStack("Test message", includeStackTrace: false);

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugOnSuccess_executes_action_on_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);
        var actionExecuted = false;

        var returned = result.DebugOnSuccess(_ => actionExecuted = true);

#if DEBUG
        actionExecuted.Should().Be(isSuccess);
#else
        actionExecuted.Should().BeFalse();
#endif
        returned.Should().Be(result);
    }

    [Fact]
    public void DebugOnSuccess_with_null_action_throws_argument_null_exception()
    {
        var result = Result.Ok(T.Value1);

        var act = () => result.DebugOnSuccess((Action<T>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "action");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DebugOnFailure_executes_action_on_failure_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);
        var actionExecuted = false;

        var returned = result.DebugOnFailure(_ => actionExecuted = true);

#if DEBUG
        actionExecuted.Should().Be(!isSuccess);
#else
        actionExecuted.Should().BeFalse();
#endif
        returned.Should().Be(result);
    }

    [Fact]
    public void Debug_can_be_chained_with_other_operations()
    {
        var result = Result.Ok("Hello")
            .Debug("Initial")
            .Bind(s => Result.Ok(s + " World"))
            .Debug("After Bind")
            .Tap(s => s.Should().Be("Hello World"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello World");
    }

    #region Async Tests

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugAsync_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().DebugAsync();

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugAsync_with_message_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().DebugAsync("Test message");

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugDetailedAsync_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().DebugDetailedAsync();

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugDetailedAsync_with_message_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().DebugDetailedAsync("Test message");

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugWithStackAsync_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().DebugWithStackAsync();

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugWithStackAsync_with_message_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().DebugWithStackAsync("Test message");

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugWithStackAsync_without_stack_trace_returns_same_result(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().DebugWithStackAsync("Test message", includeStackTrace: false);

        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugOnSuccessAsync_with_sync_action_executes_on_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);
        var actionExecuted = false;

        var returned = await result.AsTask().DebugOnSuccessAsync(_ => actionExecuted = true);

#if DEBUG
        actionExecuted.Should().Be(isSuccess);
#else
        actionExecuted.Should().BeFalse();
#endif
        returned.Should().Be(result);
    }

    [Fact]
    public async Task DebugOnSuccessAsync_with_null_result_task_throws_argument_null_exception()
    {
        Task<Result<T>> resultTask = null!;

        Func<Task<Result<T>>> act = () => resultTask.DebugOnSuccessAsync(_ => { });

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugOnSuccessAsync_with_async_action_executes_on_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);
        var actionExecuted = false;

        var returned = await result.AsTask().DebugOnSuccessAsync(_ =>
        {
            actionExecuted = true;
            return Task.CompletedTask;
        });

#if DEBUG
        actionExecuted.Should().Be(isSuccess);
#else
        actionExecuted.Should().BeFalse();
#endif
        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugOnFailureAsync_with_sync_action_executes_on_failure_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);
        var actionExecuted = false;

        var returned = await result.AsTask().DebugOnFailureAsync(_ => actionExecuted = true);

#if DEBUG
        actionExecuted.Should().Be(!isSuccess);
#else
        actionExecuted.Should().BeFalse();
#endif
        returned.Should().Be(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DebugOnFailureAsync_with_async_action_executes_on_failure_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);
        var actionExecuted = false;

        var returned = await result.AsTask().DebugOnFailureAsync(_ =>
        {
            actionExecuted = true;
            return Task.CompletedTask;
        });

#if DEBUG
        actionExecuted.Should().Be(!isSuccess);
#else
        actionExecuted.Should().BeFalse();
#endif
        returned.Should().Be(result);
    }

    [Fact]
    public async Task DebugAsync_can_be_chained_with_other_async_operations()
    {
        var result = await Task.FromResult(Result.Ok("Hello"))
            .DebugAsync("Initial")
            .BindAsync(s => Task.FromResult(Result.Ok(s + " World")))
            .DebugAsync("After Bind")
            .TapAsync(s => s.Should().Be("Hello World"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello World");
    }

    #endregion

    #region Activity Tests (DEBUG only)

#if DEBUG
    [Fact]
    public void Debug_creates_activity_with_correct_name_and_tags_for_success()
    {
        using var activityTest = new ActivityTestHelper();

        var result = Result.Ok("Test value");
        result.Debug("Test message");

        var activity = activityTest.AssertActivityCaptured("Debug: Test message");
        activity.DisplayName.Should().Be("Debug: Test message");
        activity.Tags.Should().Contain(t => t.Key == "debug.result.status" && t.Value == "Success");
        activity.Tags.Should().Contain(t => t.Key == "debug.result.value");
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void Debug_creates_activity_with_correct_name_and_tags_for_failure()
    {
        using var activityTest = new ActivityTestHelper();

        var error = Error.NotFound("User not found");
        var result = Result.Fail<string>(error);
        result.Debug("Error test");

        var activity = activityTest.AssertActivityCaptured("Debug: Error test");
        activity.DisplayName.Should().Be("Debug: Error test");
        activity.Tags.Should().Contain(t => t.Key == "debug.result.status" && t.Value == "Failure");
        activity.Tags.Should().Contain(t => t.Key == "debug.error.code" && t.Value == "not.found.error");
        activity.Tags.Should().Contain(t => t.Key == "debug.error.detail" && t.Value == "User not found");
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void DebugDetailed_includes_type_information_for_success()
    {
        using var activityTest = new ActivityTestHelper();

        var result = Result.Ok(42);
        result.DebugDetailed("Detailed test");

        var activity = activityTest.AssertActivityCaptured("Debug: Detailed test (Detailed)");
        activity.Tags.Should().Contain(t => t.Key == "debug.result.type" && t.Value == "Int32");
        activity.Tags.Should().Contain(t => t.Key == "debug.result.value" && t.Value == "42");
    }

    [Fact]
    public void DebugDetailed_includes_validation_error_field_details()
    {
        using var activityTest = new ActivityTestHelper();

        var validationError = Error.Validation("Email is required", "email")
            .And("password", "Password too short");
        var result = Result.Fail<string>(validationError);
        result.DebugDetailed("Validation error test");

        var activity = activityTest.AssertActivityCaptured("Debug: Validation error test (Detailed)");
        activity.Tags.Should().Contain(t => t.Key == "debug.error.type" && t.Value == "ValidationError");
        activity.Tags.Should().Contain(t => t.Key == "debug.error.validation.field[0].name" && t.Value == "email");
        activity.Tags.Should().Contain(t => t.Key == "debug.error.validation.field[1].name" && t.Value == "password");
    }

    [Fact]
    public void DebugDetailed_includes_aggregate_error_details()
    {
        using var activityTest = new ActivityTestHelper();

        var error1 = Error.NotFound("User not found");
        var error2 = Error.Unauthorized("Not authorized");
        var aggregateError = new AggregateError([error1, error2]);
        var result = Result.Fail<string>(aggregateError);
        result.DebugDetailed("Aggregate error test");

        var activity = activityTest.AssertActivityCaptured("Debug: Aggregate error test (Detailed)");
        activity.Tags.Should().Contain(t => t.Key == "debug.error.type" && t.Value == "AggregateError");
        activity.Tags.Should().Contain(t => t.Key == "debug.error.aggregate[0].code" && t.Value == "not.found.error");
        activity.Tags.Should().Contain(t => t.Key == "debug.error.aggregate[1].code" && t.Value == "unauthorized.error");
    }

    [Fact]
    public void DebugWithStack_includes_stack_trace_information()
    {
        using var activityTest = new ActivityTestHelper();

        var result = Result.Ok("Test");
        result.DebugWithStack("Stack test");

        var activity = activityTest.AssertActivityCaptured("Debug: Stack test (with stack)");
        activity.Tags.Should().Contain(t => t.Key.StartsWith("debug.stack[", StringComparison.Ordinal));
        activity.Tags.Should().Contain(t => t.Key == "debug.stack[0].method");
    }

    [Fact]
    public void DebugWithStack_excludes_stack_trace_when_disabled()
    {
        using var activityTest = new ActivityTestHelper();

        var result = Result.Ok("Test");
        result.DebugWithStack("No stack test", includeStackTrace: false);

        var activity = activityTest.AssertActivityCaptured("Debug: No stack test (with stack)");
        activity.Tags.Should().NotContain(t => t.Key.StartsWith("debug.stack[", StringComparison.Ordinal));
    }

    [Fact]
    public void DebugOnSuccess_creates_activity_with_correct_status()
    {
        using var activityTest = new ActivityTestHelper();

        var result = Result.Ok("Test value");
        result.DebugOnSuccess(_ => { });

        var activity = activityTest.AssertActivityCapturedWithStatus("Debug: OnSuccess", ActivityStatusCode.Ok);
    }

    [Fact]
    public void DebugOnFailure_creates_activity_with_error_status_and_tags()
    {
        using var activityTest = new ActivityTestHelper();

        var error = Error.BadRequest("Invalid request");
        var result = Result.Fail<string>(error);
        result.DebugOnFailure(_ => { });

        var activity = activityTest.AssertActivityCapturedWithStatus("Debug: OnFailure", ActivityStatusCode.Error);
        activity.Tags.Should().Contain(t => t.Key == "debug.error.code" && t.Value == "bad.request.error");
    }

    [Fact]
    public void Debug_without_message_creates_activity_with_default_name()
    {
        using var activityTest = new ActivityTestHelper();

        var result = Result.Ok("Test");
        result.Debug();

        var activity = activityTest.AssertActivityCaptured("Debug");
        activity.DisplayName.Should().Be("Debug");
    }
#endif

    #endregion
}