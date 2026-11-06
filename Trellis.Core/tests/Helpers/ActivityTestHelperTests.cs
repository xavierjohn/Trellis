namespace Trellis.Core.Tests.Helpers;

/// <summary>
/// Tests for <see cref="ActivityTestHelper"/>: the activity oracle other tracing tests rely on must itself be stable.
/// </summary>
public class ActivityTestHelperTests
{
    [Fact]
    public void WaitForActivities_returns_a_snapshot_that_later_activities_do_not_change()
    {
        using var helper = new ActivityTestHelper();
        _ = Result.Ok(1).Combine(Result.Ok(2));
        _ = Result.Ok(3).Combine(Result.Ok(4));

        var snapshot = helper.WaitForActivities("Combine", 2);
        _ = Result.Ok(5).Combine(Result.Ok(6));

        snapshot.Should().NotBeNull();
        snapshot!.Should().HaveCount(2, "a result handed to the test must not grow as the listener captures more activities");
        helper.CapturedActivities.Count(a => a.DisplayName == "Combine").Should().Be(3);
    }

    [Fact]
    public void WaitForActivities_returns_only_activities_with_the_requested_display_name()
    {
        using var helper = new ActivityTestHelper();
        _ = Result.Ok(1).Combine(Result.Ok(2));
        _ = Result.Ok(3).Map(x => x + 1);

        var combines = helper.WaitForActivities("Combine", 1);

        combines.Should().NotBeNull();
        combines!.Should().ContainSingle().Which.DisplayName.Should().Be("Combine");
    }

    [Fact]
    public void WaitForActivities_returns_null_when_the_count_is_not_reached_before_the_timeout()
    {
        using var helper = new ActivityTestHelper();
        _ = Result.Ok(1).Combine(Result.Ok(2));

        var activities = helper.WaitForActivities("Combine", 2, TimeSpan.FromMilliseconds(50));

        activities.Should().BeNull();
    }
}
