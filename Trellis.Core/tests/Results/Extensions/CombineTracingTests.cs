namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;

/// <summary>
/// Tests for Activity tracing in Combine operations (both base and tuple-based from T4 templates).
/// Verifies that Combine methods create proper OpenTelemetry activities with correct status codes.
/// 
/// Note: The Result constructor automatically sets Activity.Current status based on success/failure.
/// When Combine returns Result.Fail(), the activity status is set to Error.
/// When Combine returns Result.Ok(), the activity status is set to Ok.
/// This ensures proper visual representation in observability tools like .NET Aspire.
/// </summary>
public class CombineTracingTests : TestBase
{
    #region 2-Tuple Combine Tracing Tests

    [Fact]
    public void Combine_2Tuple_AllSuccess_CreatesActivityWithOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Ok("Second"));

        // Assert
        result.Should().BeSuccess();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Ok);
    }

    [Fact]
    public void Combine_2Tuple_FirstFails_CreatesActivityWithErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad first" })
            .Combine(Result.Ok("Second"));

        // Assert
        result.Should().BeFailure();

        var activity = activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Error);

        // Verify error is also tracked in activity tags
        var errorTag = activity.TagObjects.FirstOrDefault(t => t.Key == "result.error.code");
        errorTag.Should().NotBeNull();
        errorTag.Value.Should().Be("unprocessable-content");
    }

    [Fact]
    public void Combine_2Tuple_SecondFails_CreatesActivityWithErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad second" }));

        // Assert
        result.Should().BeFailure();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Error);
    }

    [Fact]
    public void Combine_2Tuple_BothFail_CreatesActivityWithErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad first" })
            .Combine(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad second" }));

        // Assert
        result.Should().BeFailure();

        var activity = activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Error);

        // Verify error is tracked in activity tags
        var errorTag = activity.TagObjects.FirstOrDefault(t => t.Key == "result.error.code");
        errorTag.Should().NotBeNull();
    }

    [Fact]
    public void Combine_WithUnit_Success_CreatesActivityWithOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("Value")
            .Combine(Result.Ok());

        // Assert
        result.Should().BeSuccess();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Ok);
    }

    [Fact]
    public void Combine_WithUnit_UnitFails_CreatesActivityWithErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("Value")
            .Combine(Result.Fail(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Unit validation failed" }));

        // Assert
        result.Should().BeFailure();

        var activity = activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Error);
        var errorTag = activity.TagObjects.FirstOrDefault(t => t.Key == "result.error.code");
        errorTag.Should().NotBeNull();
    }

    #endregion

    #region 3-Tuple Combine Tracing Tests

    [Fact]
    public void Combine_3Tuple_AllSuccess_CreatesMultipleActivitiesWithOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Ok("Second"))
            .Combine(Result.Ok("Third"));

        // Assert
        result.Should().BeSuccess();

        // Should have 2 Combine activities (one for 2-tuple, one for 3-tuple)
        var activities = activityTest.AssertActivityCaptured("Combine", 2);
        activities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Ok);
    }

    [Fact]
    public void Combine_3Tuple_MiddleFails_CreatesActivitiesWithErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad second" }))
            .Combine(Result.Ok("Third"));

        // Assert
        result.Should().BeFailure();

        // Should have 2 Combine activities - both should have Error status
        // because the failure propagates through the chain
        var activities = activityTest.AssertActivityCaptured("Combine", 2);
        activities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Error);
    }

    [Fact]
    public void Combine_3Tuple_LastFails_CreatesActivitiesWithMixedStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Ok("Second"))
            .Combine(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad third" }));

        // Assert
        result.Should().BeFailure();

        // Should have 2 Combine activities
        var activities = activityTest.AssertActivityCaptured("Combine", 2).ToArray();

        // First Combine (2-tuple) should succeed
        activities[0].Status.Should().Be(ActivityStatusCode.Ok);

        // Second Combine (3-tuple) should fail
        activities[1].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void Combine_3Tuple_WithUnit_CreatesActivitiesWithOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Ok("Second"))
            .Combine(Result.Ok()); // Unit validation

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().Be(("First", "Second"));

        // Should have 2 Combine activities, both with Ok status
        var activities = activityTest.AssertActivityCaptured("Combine", 2);
        activities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Ok);
    }

    #endregion

    #region Chained Combine Operations Tracing

    [Fact]
    public void Combine_ChainedOperations_CreatesMultipleActivitiesWithOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Ok("Second"))
            .Combine(Result.Ok("Third"))
            .Combine(Result.Ok("Fourth"));

        // Assert
        result.Should().BeSuccess();

        // Should have 3 Combine activities (2→3, 3→4), all with Ok status
        var activities = activityTest.AssertActivityCaptured("Combine", 3);
        activities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Ok);
    }

    [Fact]
    public void Combine_WithBindPipeline_TracesAllOperations()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Ok("Second"))
            .Combine(Result.Ok("Third"))
            .Bind((a, b, c) => Result.Ok($"{a} {b} {c}"));

        // Assert
        result.Should().BeSuccess();

        // Verify Combine operations were traced with Ok status
        var combineActivities = activityTest.AssertActivityCaptured("Combine", 2);
        combineActivities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Ok);

        // Verify Bind operation was traced with Ok status
        activityTest.AssertActivityCapturedWithStatus("Bind", ActivityStatusCode.Ok);
    }

    [Fact]
    public void Combine_WithTapOnFailurePipeline_TracesAllOperations()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var tapExecuted = false;

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad second" }))
            .Combine(Result.Ok("Third"))
            .TapOnFailure(() => tapExecuted = true);

        // Assert
        result.Should().BeFailure();
        tapExecuted.Should().BeTrue();

        // Verify Combine operations were traced with Error status
        var combineActivities = activityTest.AssertActivityCaptured("Combine", 2);
        combineActivities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Error);

        // Verify TapOnFailure was traced with Error status
        activityTest.AssertActivityCapturedWithStatus("TapOnFailure", ActivityStatusCode.Error);
    }

    #endregion

    #region Async Combine Tracing Tests

    [Fact]
    public async Task CombineAsync_TaskResult_Success_CreatesActivity()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = await Task.FromResult(Result.Ok("First"))
            .CombineAsync(Result.Ok("Second"));

        // Assert
        result.Should().BeSuccess();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task CombineAsync_TaskResult_Failure_CreatesActivity()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad first" }))
            .CombineAsync(Result.Ok("Second"));

        // Assert
        result.Should().BeFailure();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Error);
    }

    [Fact]
    public async Task CombineAsync_InPipeline_TracesOperations()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = await Task.FromResult(Result.Ok("First"))
            .CombineAsync(Result.Ok("Second"))
            .BindAsync((a, b) => Task.FromResult(Result.Ok($"{a} {b}")));

        // Assert
        result.Should().BeSuccess();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Ok);
        activityTest.AssertActivityCapturedWithStatus("Bind", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task CombineAsync_ValueTaskResult_Success_CreatesActivity()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = await ValueTask.FromResult(Result.Ok("First"))
            .CombineAsync(ValueTask.FromResult(Result.Ok("Second")));

        // Assert
        result.Should().BeSuccess();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task CombineAsync_ValueTaskResult_Failure_CreatesActivity()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = await ValueTask.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad first" }))
            .CombineAsync(ValueTask.FromResult(Result.Ok("Second")));

        // Assert
        result.Should().BeFailure();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Error);
    }

    [Fact]
    public async Task CombineAsync_ValueTaskWithUnit_Success_CreatesActivity()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = await ValueTask.FromResult(Result.Ok("First"))
            .CombineAsync(Result.Ok());

        // Assert
        result.Should().BeSuccess().Which.Should().Be("First");
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Ok);
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void Combine_ValidationPipeline_TracesCompleteFlowWithOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Simulate real-world validation scenario
        var result = Result.Ok("user@example.com")
            .Combine(Result.Ok("John"))
            .Combine(Result.Ok("Doe"))
            .Bind((email, firstName, lastName) =>
                Result.Ok($"{firstName} {lastName} <{email}>"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("John Doe <user@example.com>");

        // Verify complete trace: 2 Combines + 1 Bind, all with Ok status
        var combineActivities = activityTest.AssertActivityCaptured("Combine", 2);
        combineActivities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Ok);

        activityTest.AssertActivityCapturedWithStatus("Bind", ActivityStatusCode.Ok);
    }

    [Fact]
    public void Combine_FailedValidations_TracesErrorPathWithErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var errorLogged = false;

        // Act - Multiple validations fail
        var result = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "unprocessable-content") { Detail = "Invalid email" })))
            .Combine(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("firstName"), "unprocessable-content") { Detail = "Invalid first name" }))))
            .Combine(Result.Ok("Doe"))
            .TapOnFailure(error => errorLogged = true);

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
        errorLogged.Should().BeTrue();

        // Verify Combine operations were traced with Error status
        var combineActivities = activityTest.AssertActivityCaptured("Combine", 2);
        combineActivities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Error);

        // Verify error tags are also present
        combineActivities.Should().OnlyContain(a => a.TagObjects.Any(t => t.Key == "result.error.code"));

        // Verify TapOnFailure was traced with Error status
        activityTest.AssertActivityCapturedWithStatus("TapOnFailure", ActivityStatusCode.Error);
    }

    [Fact]
    public void Combine_MixedErrorTypes_TracesAggregationWithErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Validation error" })
            .Combine(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }))
            .Combine(Result.Ok("Third"));

        // Assert
        result.Should().BeFailureOfType<Error.Aggregate>();

        // Verify Combine operations were traced with Error status
        var activities = activityTest.AssertActivityCaptured("Combine", 2);
        activities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Error);

        // Verify error tags
        activities.Should().OnlyContain(a => a.TagObjects.Any(t => t.Key == "result.error.code"));
    }

    [Fact]
    public void Combine_LongChain_TracesAllStepsWithOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Simulate combining many validations
        var result = Result.Ok("1")
            .Combine(Result.Ok("2"))
            .Combine(Result.Ok("3"))
            .Combine(Result.Ok("4"))
            .Combine(Result.Ok("5"))
            .Combine(Result.Ok("6"));

        // Assert
        result.Should().BeSuccess();

        // Should have 5 Combine activities (2→3→4→5→6), all with Ok status
        var activities = activityTest.AssertActivityCaptured("Combine", 5);
        activities.Should().OnlyContain(a => a.Status == ActivityStatusCode.Ok);
    }

    #endregion

    #region Activity Name Verification

    [Fact]
    public void Combine_CreatesActivity_WithCorrectName()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok("First")
            .Combine(Result.Ok("Second"));

        // Assert
        var activity = activityTest.AssertActivityCaptured("Combine");
        activity.DisplayName.Should().Be("Combine");
    }

    #endregion
}
