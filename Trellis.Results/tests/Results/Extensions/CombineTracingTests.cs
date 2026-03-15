namespace Trellis.Results.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Results.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Tests for Activity tracing in Combine operations (both base and tuple-based from T4 templates).
/// Verifies that Combine methods create proper OpenTelemetry activities with correct status codes.
/// 
/// Note: The Result constructor automatically sets Activity.Current status based on success/failure.
/// When Combine returns Result.Failure(), the activity status is set to Error.
/// When Combine returns Result.Success(), the activity status is set to Ok.
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
        var result = Result.Success("First")
            .Combine(Result.Success("Second"));

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
        var result = Result.Failure<string>(Error.Validation("Bad first"))
            .Combine(Result.Success("Second"));

        // Assert
        result.Should().BeFailure();

        var activity = activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Error);

        // Verify error is also tracked in activity tags
        var errorTag = activity.TagObjects.FirstOrDefault(t => t.Key == "result.error.code");
        errorTag.Should().NotBeNull();
        errorTag.Value.Should().Be("validation.error");
    }

    [Fact]
    public void Combine_2Tuple_SecondFails_CreatesActivityWithErrorStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Success("First")
            .Combine(Result.Failure<string>(Error.Validation("Bad second")));

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
        var result = Result.Failure<string>(Error.Validation("Bad first"))
            .Combine(Result.Failure<string>(Error.Validation("Bad second")));

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
        var result = Result.Success("Value")
            .Combine(Result.Success());

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
        var result = Result.Success("Value")
            .Combine(Result.Failure(Error.Validation("Unit validation failed")));

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
        var result = Result.Success("First")
            .Combine(Result.Success("Second"))
            .Combine(Result.Success("Third"));

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
        var result = Result.Success("First")
            .Combine(Result.Failure<string>(Error.Validation("Bad second")))
            .Combine(Result.Success("Third"));

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
        var result = Result.Success("First")
            .Combine(Result.Success("Second"))
            .Combine(Result.Failure<string>(Error.Validation("Bad third")));

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
        var result = Result.Success("First")
            .Combine(Result.Success("Second"))
            .Combine(Result.Success()); // Unit validation

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be(("First", "Second"));

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
        var result = Result.Success("First")
            .Combine(Result.Success("Second"))
            .Combine(Result.Success("Third"))
            .Combine(Result.Success("Fourth"));

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
        var result = Result.Success("First")
            .Combine(Result.Success("Second"))
            .Combine(Result.Success("Third"))
            .Bind((a, b, c) => Result.Success($"{a} {b} {c}"));

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
        var result = Result.Success("First")
            .Combine(Result.Failure<string>(Error.Validation("Bad second")))
            .Combine(Result.Success("Third"))
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
        var result = await Task.FromResult(Result.Success("First"))
            .CombineAsync(Result.Success("Second"));

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
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad first")))
            .CombineAsync(Result.Success("Second"));

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
        var result = await Task.FromResult(Result.Success("First"))
            .CombineAsync(Result.Success("Second"))
            .BindAsync((a, b) => Task.FromResult(Result.Success($"{a} {b}")));

        // Assert
        result.Should().BeSuccess();
        activityTest.AssertActivityCapturedWithStatus("Combine", ActivityStatusCode.Ok);
        activityTest.AssertActivityCapturedWithStatus("Bind", ActivityStatusCode.Ok);
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void Combine_ValidationPipeline_TracesCompleteFlowWithOkStatus()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act - Simulate real-world validation scenario
        var result = Result.Success("user@example.com")
            .Combine(Result.Success("John"))
            .Combine(Result.Success("Doe"))
            .Bind((email, firstName, lastName) =>
                Result.Success($"{firstName} {lastName} <{email}>"));

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
        var result = Result.Failure<string>(Error.Validation("Invalid email", "email"))
            .Combine(Result.Failure<string>(Error.Validation("Invalid first name", "firstName")))
            .Combine(Result.Success("Doe"))
            .TapOnFailure(error => errorLogged = true);

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
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
        var result = Result.Failure<string>(Error.Validation("Validation error"))
            .Combine(Result.Failure<string>(Error.NotFound("Not found")))
            .Combine(Result.Success("Third"));

        // Assert
        result.Should().BeFailureOfType<AggregateError>();

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
        var result = Result.Success("1")
            .Combine(Result.Success("2"))
            .Combine(Result.Success("3"))
            .Combine(Result.Success("4"))
            .Combine(Result.Success("5"))
            .Combine(Result.Success("6"));

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
        var result = Result.Success("First")
            .Combine(Result.Success("Second"));

        // Assert
        var activity = activityTest.AssertActivityCaptured("Combine");
        activity.DisplayName.Should().Be("Combine");
    }

    #endregion
}