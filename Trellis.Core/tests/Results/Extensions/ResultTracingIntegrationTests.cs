namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Integration tests for Activity tracing across complex ROP pipelines.
/// Tests the complete observability story when multiple operations are chained together.
/// </summary>
public class ResultTracingIntegrationTests
{
    #region Map Tracing Tests

    [Fact]
    public void Map_Success_LogsOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok(21)
            .Map(x => x * 2);

        // Assert
        result.Should().BeSuccess().Which.Should().Be(42);
        activityTest.AssertActivityCapturedWithStatus("Map", ActivityStatusCode.Ok);
    }

    [Fact]
    public void Map_Failure_LogsErrorStatus_AndShortCircuits()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var functionCalled = false;

        // Act
        var result = Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid value" })
            .Map(x =>
            {
                functionCalled = true;
                return x * 2;
            });

        // Assert
        functionCalled.Should().BeFalse();
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
        activityTest.AssertActivityCapturedWithStatus("Map", ActivityStatusCode.Error);
    }

    [Fact]
    public async Task MapAsync_Success_LogsOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = await Result.Ok(21)
            .MapAsync(x => Task.FromResult(x * 2));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(42);
        activityTest.AssertActivityCapturedWithStatus("Map", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task MapAsync_Failure_LogsErrorStatus_AndShortCircuits()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var functionCalled = false;

        // Act
        var result = await Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid value" })
            .MapAsync(x =>
            {
                functionCalled = true;
                return Task.FromResult(x * 2);
            });

        // Assert
        functionCalled.Should().BeFalse();
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
        activityTest.AssertActivityCapturedWithStatus("Map", ActivityStatusCode.Error);
    }

    #endregion

    #region Success Path Integration Tests

    [Fact]
    public void SuccessPath_MultipleEnsure_Bind_Tap_TracesAllOperations()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Multiple validations followed by transformations
        var result = Result.Ok(50)
            .Ensure(x => x > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be positive" })
            .Ensure(x => x < 100, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be < 100" })
            .Bind(x => Result.Ok(x * 2))
            .Tap(x => Console.WriteLine($"Doubled: {x}"))
            .Bind(x => Result.Ok(x + 10));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(110);

        // Verify activity counts
        var ensureActivities = activityTest.AssertActivityCaptured("Ensure", 2);
        ensureActivities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Ok);

        var bindActivities = activityTest.AssertActivityCaptured("Bind", 2);
        bindActivities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Ok);

        activityTest.AssertActivityCapturedWithStatus("Tap", ActivityStatusCode.Ok);
    }

    #endregion

    #region Failure Path Integration Tests

    [Fact]
    public void FailurePath_Bind_Fails_PropagatesErrorThroughPipeline()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Bind fails in the middle of pipeline
        var result = Result.Ok(42)
            .Ensure(x => x > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be positive" })
            .Bind(x => Result.Fail<int>(new Error.InternalServerError("test") { Detail = "Database error" }))
            .Tap(x => Console.WriteLine(x))
            .Bind(x => Result.Ok(x + 10));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Kind.Should().Be("internal-server-error");

        // Ensure should succeed
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Ok);

        // Bind operations should reflect the failure
        var bindActivities = activityTest.AssertActivityCaptured("Bind", 2);
        bindActivities.First().Status.Should().Be(ActivityStatusCode.Error); // First Bind fails
        bindActivities.Last().Status.Should().Be(ActivityStatusCode.Error);  // Second Bind short-circuits
    }

    #endregion

    #region Recovery Path Integration Tests

    [Fact]
    public void RecoveryPath_MultipleRecoveries_FirstSucceeds()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Multiple recoveries, first one succeeds
        var result = Result.Ok(5)
            .Ensure(x => x > 10, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be > 10" })
            .RecoverOnFailure(() => Result.Ok(50))
            .RecoverOnFailure(() => Result.Ok(999)); // Should not execute

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(50);

        // First RecoverOnFailure should execute and succeed
        var recoverActivities = activityTest.AssertActivityCaptured("RecoverOnFailure", 1);
        recoverActivities.First().Status.Should().Be(ActivityStatusCode.Ok);
    }

    #endregion

    #region Complex Real-World Scenarios

    [Fact]
    public void ComplexScenario_ValidationPipeline_WithRecovery()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Simulate a real-world validation and processing pipeline
        var result = ProcessOrder(orderId: 123)
            .Ensure(order => order > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Order ID must be positive" })
            .Bind(ValidateInventory)
            .Bind(ApplyDiscount)
            .Ensure(price => price >= 10, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Price must be at least $10" })
            .RecoverOnFailure(() => Result.Ok(10)) // Floor price at $10
            .Tap(price => Console.WriteLine($"Final price: ${price}"));

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify the complete trace
        var ensureActivities = activityTest.AssertActivityCaptured("Ensure", 2);
        var bindActivities = activityTest.AssertActivityCaptured("Bind", 2);

        // All operations should have created activities
        activityTest.AssertActivityCaptured("Tap", 1);
    }

    [Fact]
    public void ComplexScenario_FailureRecoveryChain()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Complex failure and recovery scenario
        var result = Result.Ok("user@example.com")
            .Bind(FetchUserFromDatabase)      // Fails
            .RecoverOnFailure(CreateGuestUser)
            .Ensure(user => !string.IsNullOrEmpty(user), new Error.InternalServerError("test") { Detail = "User cannot be null" })
            .Bind(user => Result.Ok(user.ToUpperInvariant()))
            .Tap(user => Console.WriteLine($"Processing: {user}"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("GUEST_USER");

        // Verify recovery was triggered
        activityTest.AssertActivityCaptured("Bind", 2);
        activityTest.AssertActivityCapturedWithStatus("RecoverOnFailure", ActivityStatusCode.Ok);
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Ok);
        activityTest.AssertActivityCapturedWithStatus("Tap", ActivityStatusCode.Ok);
    }

    #endregion

    #region Async Pipeline Integration Tests

    [Fact]
    public async Task AsyncPipeline_Ensure_BindAsync_TapAsync_MaintainsTraceContext()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Async pipeline
        var result = await Result.Ok(42)
            .Ensure(x => x > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be positive" })
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2)))
            .TapAsync(x => Task.CompletedTask);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(84);

        // Verify activities were captured across async boundaries
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Ok);
        activityTest.AssertActivityCapturedWithStatus("Bind", ActivityStatusCode.Ok);
        activityTest.AssertActivityCapturedWithStatus("Tap", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task AsyncPipeline_ComplexChain_PreservesActivities()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Mix of sync and async operations
        var result = await Result.Ok(10)
            .Ensure(x => x > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Positive" })
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2)))
            .BindAsync(async x =>
            {
                var r = Result.Ok(x);
                return await Task.FromResult(r.Ensure(y => y < 100, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "< 100" }));
            })
            .TapAsync(async x => await Task.Delay(1))
            .BindAsync(x => Task.FromResult(Result.Ok(x + 5)));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(25);

        // Verify all operations were traced
        activityTest.AssertActivityCaptured("Ensure", 2);
        activityTest.AssertActivityCaptured("Bind", 3);
        activityTest.AssertActivityCaptured("Tap", 1);
    }

    #endregion

    #region Track Transition Tests

    [Fact]
    public void TrackTransition_SuccessToError_LogsCorrectActivityStatuses()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Success track changes to error track at Ensure
        var result = Result.Ok(42)
            .Bind(x => Result.Ok(x * 2))          // Success track: 84
            .Tap(x => Console.WriteLine($"Before: {x}"))  // Success track
            .Ensure(x => x < 50, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be < 50" }) // TRANSITION: Success ? Error
            .Bind(x => Result.Ok(x + 10))         // Error track (short-circuited)
            .Tap(x => Console.WriteLine($"After: {x}")); // Error track (short-circuited)

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("unprocessable-content");

        // Get all activities by name
        var activities = activityTest.CapturedActivities;
        var bindActivities = activities.Where(a => a.DisplayName == "Bind").ToArray();
        var tapActivities = activities.Where(a => a.DisplayName == "Tap").ToArray();
        var ensureActivity = activities.First(a => a.DisplayName == "Ensure");

        // Verify activities before the transition are OK
        bindActivities[0].Status.Should().Be(ActivityStatusCode.Ok);
        tapActivities[0].Status.Should().Be(ActivityStatusCode.Ok);

        // Verify the transition point logs Error
        ensureActivity.Status.Should().Be(ActivityStatusCode.Error);

        // Verify activities after the transition are Error (short-circuited)
        bindActivities[1].Status.Should().Be(ActivityStatusCode.Error);
        tapActivities[1].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void TrackTransition_ErrorToSuccess_LogsCorrectActivityStatuses()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Error track changes to success track via Compensate
        var result = Result.Ok(5)
            .Ensure(x => x > 10, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be > 10" }) // TRANSITION: Success ? Error
            .Bind(x => Result.Ok(x * 2))         // Error track (short-circuited)
            .RecoverOnFailure(() => Result.Ok(100))    // TRANSITION: Error ? Success
            .Bind(x => Result.Ok(x + 50))        // Success track: 150
            .Tap(x => Console.WriteLine($"Final: {x}")); // Success track

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(150);

        // Get all activities
        var activities = activityTest.CapturedActivities;
        var bindActivities = activities.Where(a => a.DisplayName == "Bind").ToArray();

        // Verify the first transition (Success ? Error)
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Error);
        bindActivities[0].Status.Should().Be(ActivityStatusCode.Error); // Short-circuited

        // Verify the recovery (Error ? Success)
        activityTest.AssertActivityCapturedWithStatus("RecoverOnFailure", ActivityStatusCode.Ok);

        // Verify activities after compensation are OK
        bindActivities[1].Status.Should().Be(ActivityStatusCode.Ok);
        activityTest.AssertActivityCapturedWithStatus("Tap", ActivityStatusCode.Ok);
    }

    [Fact]
    public void TrackTransition_MultipleTransitions_LogsAllStatusChanges()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Multiple track transitions in one pipeline
        var result = Result.Ok(100)
            .Ensure(x => x > 50, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be > 50" })  // OK: Success track
            .Bind(x => Result.Fail<int>(new Error.InternalServerError("test") { Detail = "DB Error" })) // TRANSITION: Success ? Error
            .Tap(x => Console.WriteLine($"After error: {x}"))         // Error track
            .RecoverOnFailure(() => Result.Ok(200))                     // TRANSITION: Error ? Success
            .Ensure(x => x < 150, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be < 150" })  // TRANSITION: Success ? Error (200 > 150)
            .RecoverOnFailure(() => Result.Ok(50))                      // TRANSITION: Error ? Success
            .Bind(x => Result.Ok(x * 2));                         // Success track: 100

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(100);

        // Get all activities
        var activities = activityTest.CapturedActivities;
        var ensureActivities = activities.Where(a => a.DisplayName == "Ensure").ToArray();
        var bindActivities = activities.Where(a => a.DisplayName == "Bind").ToArray();
        var recoverActivities = activities.Where(a => a.DisplayName == "RecoverOnFailure").ToArray();
        var tapActivity = activities.First(a => a.DisplayName == "Tap");

        // Verify first transition: Success ? Error (at Bind)
        ensureActivities[0].Status.Should().Be(ActivityStatusCode.Ok);
        bindActivities[0].Status.Should().Be(ActivityStatusCode.Error);
        tapActivity.Status.Should().Be(ActivityStatusCode.Error);

        // Verify first recovery: Error ? Success
        recoverActivities[0].Status.Should().Be(ActivityStatusCode.Ok);

        // Verify second transition: Success ? Error (at Ensure)
        ensureActivities[1].Status.Should().Be(ActivityStatusCode.Error);

        // Verify second recovery: Error ? Success
        recoverActivities[1].Status.Should().Be(ActivityStatusCode.Ok);

        // Verify final operation on success track
        bindActivities[1].Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task TrackTransition_AsyncPipeline_MaintainsStatusAcrossTransitions()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Track transitions in async pipeline
        var result = await Result.Ok(42)
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2))) // Success: 84
            .BindAsync(async x =>
            {
                await Task.Delay(1);
                return Result.Fail<int>(new Error.InternalServerError("test") { Detail = "Async error" });
            }) // TRANSITION: Success ? Error
            .TapAsync(x => Task.CompletedTask)                      // Error track
            .RecoverOnFailureAsync(() => Task.FromResult(Result.Ok(200))) // TRANSITION: Error ? Success
            .BindAsync(x => Task.FromResult(Result.Ok(x / 2))); // Success track: 100

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(100);

        // Get all activities
        var activities = activityTest.CapturedActivities;
        var bindActivities = activities.Where(a => a.DisplayName == "Bind").ToArray();
        var tapActivity = activities.First(a => a.DisplayName == "Tap");
        var recoverActivity = activities.First(a => a.DisplayName == "RecoverOnFailure");

        // Verify transitions
        bindActivities[0].Status.Should().Be(ActivityStatusCode.Ok);  // First Bind: Success
        bindActivities[1].Status.Should().Be(ActivityStatusCode.Error); // Second Bind: Error
        tapActivity.Status.Should().Be(ActivityStatusCode.Error);
        recoverActivity.Status.Should().Be(ActivityStatusCode.Ok);
        bindActivities[2].Status.Should().Be(ActivityStatusCode.Ok);  // Third Bind: Success after compensation
    }

    [Fact]
    public void TrackTransition_FailedRecovery_MaintainsErrorTrack()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Recovery fails, stays on error track
        var result = Result.Ok(5)
            .Ensure(x => x > 10, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be > 10" })     // TRANSITION: Success ? Error
            .RecoverOnFailure(() => Result.Fail<int>(new Error.InternalServerError("test") { Detail = "Recovery failed" })) // Failed recovery
            .Bind(x => Result.Ok(x * 2))                           // Still on error track
            .Tap(x => Console.WriteLine($"Value: {x}"));                // Still on error track

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Kind.Should().Be("internal-server-error");
        result.Error!.Detail.Should().Be("Recovery failed");

        // Verify transition and failed recovery
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Error);
        activityTest.AssertActivityCapturedWithStatus("RecoverOnFailure", ActivityStatusCode.Error);

        // Verify subsequent operations stay on error track
        activityTest.AssertActivityCapturedWithStatus("Bind", ActivityStatusCode.Error);
        activityTest.AssertActivityCapturedWithStatus("Tap", ActivityStatusCode.Error);
    }

    #endregion

    #region Helper Methods

    private static Result<int> ProcessOrder(int orderId) => Result.Ok(orderId);

    private static Result<int> ValidateInventory(int orderId) => Result.Ok(100); // Price

    private static Result<int> ApplyDiscount(int price) => Result.Ok(price - 95); // Discount brings it to $5

    private static Result<string> FetchUserFromDatabase(string email) =>
        Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "User not found" });

    private static Result<string> CreateGuestUser() => Result.Ok("guest_user");

    #endregion
}