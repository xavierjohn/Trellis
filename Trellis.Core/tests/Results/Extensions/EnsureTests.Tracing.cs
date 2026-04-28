namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

public class EnsureTracingTests
{
    #region Activity Name Tests

    [Fact]
    public void Ensure_CreatesActivity_WithCorrectName()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var result = Result.Ok(42);

        // Act
        var actual = result.Ensure(x => x > 40, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value must be > 40" });

        // Assert
        var activity = activityTest.AssertActivityCaptured("Ensure");
        activity.DisplayName.Should().Be("Ensure");
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_CreatesActivity_WithCorrectName()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var actual = "test".EnsureNotNullOrWhiteSpace(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "String is required" });

        // Assert
        var activity = activityTest.AssertActivityCaptured("EnsureNotNullOrWhiteSpace");
        activity.DisplayName.Should().Be("EnsureNotNullOrWhiteSpace");
    }

    #endregion

    #region Activity Status Tests - Success Cases

    [Fact]
    public void Ensure_LogsActivityStatus_OnSuccess_WithPredicatePass()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var result = Result.Ok(42);

        // Act
        var actual = result.Ensure(x => x > 40, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value must be > 40" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Ok);
        actual.Should().BeSuccess();
    }

    [Fact]
    public void Ensure_LogsActivityStatus_OnSuccess_WithBooleanPredicate()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var result = Result.Ok("test");

        // Act
        var actual = result.Ensure(() => true, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be true" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Ok);
        actual.Should().BeSuccess();
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_LogsActivityStatus_OnSuccess()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var actual = "test".EnsureNotNullOrWhiteSpace(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "String is required" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("EnsureNotNullOrWhiteSpace", ActivityStatusCode.Ok);
        actual.Should().BeSuccess();
    }

    #endregion

    #region Activity Status Tests - Failure Cases

    [Fact]
    public void Ensure_LogsActivityStatus_OnFailure_PredicateFails()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var result = Result.Ok(42);

        // Act
        var actual = result.Ensure(x => x < 40, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value must be < 40" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Error);
        actual.Should().BeFailure();
    }

    [Fact]
    public void Ensure_LogsActivityStatus_OnFailure_InputResultIsFailure()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };
        var result = Result.Fail<int>(error);

        // Act
        var actual = result.Ensure(x => x > 40, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value must be > 40" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Error);
        actual.Should().BeFailure();
    }

    [Fact]
    public void Ensure_WithErrorPredicate_LogsActivityStatus_OnFailure()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var result = Result.Ok(5);

        // Act
        var actual = result.Ensure(x => x > 10, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value must be > 10" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Error);
        actual.Should().BeFailure();
    }

    [Fact]
    public void Ensure_WithResultPredicate_LogsActivityStatus_PredicateReturnsFailure()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var result = Result.Ok(42);

        // Act
        var actual = result.Ensure(x => x > 100, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Predicate failed" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Error);
        actual.Should().BeFailure();
    }

    [Fact]
    public void Ensure_WithValueResultPredicate_LogsActivityStatus_PredicateReturnsFailure()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var result = Result.Ok(42);

        // Act
        var actual = result.Ensure(
            value => value > 100,
            value => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Value {value} is invalid" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Error);
        actual.Should().BeFailure();
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_LogsActivityStatus_OnFailure_NullString()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        string? str = null;
        var actual = str.EnsureNotNullOrWhiteSpace(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "String is required" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("EnsureNotNullOrWhiteSpace", ActivityStatusCode.Error);
        actual.Should().BeFailure();
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_LogsActivityStatus_OnFailure_EmptyString()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var actual = "   ".EnsureNotNullOrWhiteSpace(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "String is required" });

        // Assert
        activityTest.AssertActivityCapturedWithStatus("EnsureNotNullOrWhiteSpace", ActivityStatusCode.Error);
        actual.Should().BeFailure();
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void Ensure_CanBeChainedWithOtherOperations()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();

        // Act
        var result = Result.Ok(42)
            .Ensure(x => x > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be positive" })
            .Ensure(x => x < 100, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be < 100" })
            .Bind(x => Result.Ok(x * 2));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(84);

        // Verify that each Ensure operation created its own activity with OK status
        var ensureActivities = activityTest.AssertActivityCaptured("Ensure", 2).ToArray();
        ensureActivities[0].Status.Should().Be(ActivityStatusCode.Ok);
        ensureActivities[1].Status.Should().Be(ActivityStatusCode.Ok);

        // Verify that Bind operation created its activity with OK status
        var bindActivity = activityTest.AssertActivityCaptured("Bind", 1).First();
        bindActivity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    #endregion
}