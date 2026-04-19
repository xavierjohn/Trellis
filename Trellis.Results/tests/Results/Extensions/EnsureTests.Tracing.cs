namespace Trellis.Results.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Results.Tests.Helpers;
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
        var actual = result.Ensure(x => x > 40, Error.Validation("Value must be > 40"));

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
        var actual = "test".EnsureNotNullOrWhiteSpace(Error.Validation("String is required"));

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
        var actual = result.Ensure(x => x > 40, Error.Validation("Value must be > 40"));

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
        var actual = result.Ensure(() => true, Error.Validation("Must be true"));

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
        var actual = "test".EnsureNotNullOrWhiteSpace(Error.Validation("String is required"));

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
        var actual = result.Ensure(x => x < 40, Error.Validation("Value must be < 40"));

        // Assert
        activityTest.AssertActivityCapturedWithStatus("Ensure", ActivityStatusCode.Error);
        actual.Should().BeFailure();
    }

    [Fact]
    public void Ensure_LogsActivityStatus_OnFailure_InputResultIsFailure()
    {
        // Arrange
        using var activityTest = new ActivityTestHelper();
        var error = Error.NotFound("Not found");
        var result = Result.Fail<int>(error);

        // Act
        var actual = result.Ensure(x => x > 40, Error.Validation("Value must be > 40"));

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
        var actual = result.Ensure(x => x > 10, Error.Validation("Value must be > 10"));

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
        var actual = result.Ensure(x => x > 100, Error.Validation("Predicate failed"));

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
            value => Error.Validation($"Value {value} is invalid"));

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
        var actual = str.EnsureNotNullOrWhiteSpace(Error.Validation("String is required"));

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
        var actual = "   ".EnsureNotNullOrWhiteSpace(Error.Validation("String is required"));

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
            .Ensure(x => x > 0, Error.Validation("Must be positive"))
            .Ensure(x => x < 100, Error.Validation("Must be < 100"))
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