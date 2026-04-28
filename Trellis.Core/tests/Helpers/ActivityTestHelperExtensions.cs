namespace Trellis.Core.Tests.Helpers;

using System.Diagnostics;
using FluentAssertions;

/// <summary>
/// Extension methods for ActivityTestHelper to make test assertions more expressive and readable.
/// </summary>
public static class ActivityTestHelperExtensions
{
    /// <summary>
    /// Asserts that the expected number of activities have been captured.
    /// Waits for the activities with a timeout and fails the test if the count is not reached.
    /// </summary>
    /// <param name="helper">The ActivityTestHelper instance.</param>
    /// <param name="expectedCount">The expected number of activities.</param>
    /// <param name="timeout">Optional timeout period. Defaults to 2 seconds.</param>
    /// <returns>The same ActivityTestHelper instance for method chaining.</returns>
    public static ActivityTestHelper AssertActivityCaptured(
        this ActivityTestHelper helper,
        int expectedCount,
        TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(2);
        helper.WaitForActivityCount(expectedCount, actualTimeout)
            .Should().BeTrue($"expected {expectedCount} activities to be captured within {actualTimeout.TotalSeconds}s");
        return helper;
    }

    /// <summary>
    /// Asserts that an activity with the specified display name has been captured.
    /// Waits for the activity with a timeout and fails the test if not found.
    /// First waits for at least one activity to be captured before searching.
    /// </summary>
    /// <param name="helper">The ActivityTestHelper instance.</param>
    /// <param name="displayName">The expected activity display name.</param>
    /// <param name="timeout">Optional timeout period. Defaults to 2 seconds.</param>
    /// <returns>The captured activity for further assertions.</returns>
    public static Activity AssertActivityCaptured(
        this ActivityTestHelper helper,
        string displayName,
        TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(2);

        // First wait for at least one activity to ensure async completion
        helper.WaitForActivityCount(1, actualTimeout);

        // Then wait for the specific activity
        var activity = helper.WaitForActivity(displayName, actualTimeout);
        activity.Should().NotBeNull($"expected activity '{displayName}' to be captured within {actualTimeout.TotalSeconds}s");
        return activity!;
    }

    /// <summary>
    /// Asserts that an activity with the specified display name has been captured and has the expected status.
    /// </summary>
    /// <param name="helper">The ActivityTestHelper instance.</param>
    /// <param name="displayName">The expected activity display name.</param>
    /// <param name="expectedStatus">The expected activity status.</param>
    /// <param name="timeout">Optional timeout period. Defaults to 2 seconds.</param>
    /// <returns>The captured activity for further assertions.</returns>
    public static Activity AssertActivityCapturedWithStatus(
        this ActivityTestHelper helper,
        string displayName,
        ActivityStatusCode expectedStatus,
        TimeSpan? timeout = null)
    {
        var activity = helper.AssertActivityCaptured(displayName, timeout);
        activity.Status.Should().Be(expectedStatus,
            $"activity '{displayName}' should have status {expectedStatus}");
        return activity;
    }

    /// <summary>
    /// Asserts that multiple activities with the specified display name have been captured.
    /// Waits for the activities with a timeout and fails the test if the count is not reached.
    /// </summary>
    /// <param name="helper">The ActivityTestHelper instance.</param>
    /// <param name="displayName">The expected activity display name.</param>
    /// <param name="expectedCount">The minimum number of activities expected.</param>
    /// <param name="timeout">Optional timeout period. Defaults to 2 seconds.</param>
    /// <returns>An enumerable of captured activities for further assertions.</returns>
    public static IEnumerable<Activity> AssertActivityCaptured(
        this ActivityTestHelper helper,
        string displayName,
        int expectedCount,
        TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(2);
        var activities = helper.WaitForActivities(displayName, expectedCount, actualTimeout);
        activities.Should().NotBeNull($"expected at least {expectedCount} activities with name '{displayName}' to be captured within {actualTimeout.TotalSeconds}s");
        return activities!;
    }
}