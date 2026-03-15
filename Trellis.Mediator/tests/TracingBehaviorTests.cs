namespace Trellis.Mediator.Tests;

using System.Diagnostics;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="TracingBehavior{TMessage, TResponse}"/>.
/// </summary>
public class TracingBehaviorTests : IDisposable
{
    private readonly ActivitySource _activitySource = TracingBehavior<TestCommand, Result<string>>.ActivitySource;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public TracingBehaviorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = ShouldListenToTrellisMediator,
            Sample = SampleAllData,
            ActivityStopped = _activities.Add
        };
        ActivitySource.AddActivityListener(_listener);
    }

    private static bool ShouldListenToTrellisMediator(ActivitySource source)
        => source.Name == "Trellis.Mediator";

    private static ActivitySamplingResult SampleAllData(ref ActivityCreationOptions<ActivityContext> options)
        => ActivitySamplingResult.AllDataAndRecorded;

    public void Dispose()
    {
        _listener.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Successful handler — Activity status Ok

    [Fact]
    public async Task Handle_SuccessfulResult_SetsActivityStatusOk()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Success("Hello, Alice!"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _activities.Should().ContainSingle();
        var activity = _activities[0];
        activity.Status.Should().Be(ActivityStatusCode.Ok);
        activity.DisplayName.Should().Be("TestCommand");
    }

    #endregion

    #region Failed Result — Activity status Error with tags

    [Fact]
    public async Task Handle_FailedResult_SetsActivityStatusErrorWithTags()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Failure<string>(Error.Validation("Bad input.", "field")));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _activities.Should().ContainSingle();
        var activity = _activities[0];
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("Bad input.");
        activity.GetTagItem("error.type").Should().Be("ValidationError");
        activity.GetTagItem("error.code").Should().Be("validation.error");
    }

    #endregion

    #region No listener — no-op

    [Fact]
    public async Task Handle_NoActivityListener_StillReturnsResult()
    {
        // Dispose listener so no activity is created
        _listener.Dispose();
        _activities.Clear();

        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Success("Hello!"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello!");
    }

    #endregion

    #region Activity name is message type name

    [Fact]
    public async Task Handle_ActivityName_IsMessageTypeName()
    {
        var behavior = new TracingBehavior<AdminCommand, Result<string>>();
        var command = new AdminCommand("data");
        var next = NextDelegate.ReturningAsync<AdminCommand, Result<string>>(
            Result.Success("Done"));

        await behavior.Handle(command, next, CancellationToken.None);

        _activities.Should().ContainSingle();
        _activities[0].DisplayName.Should().Be("AdminCommand");
    }

    #endregion

    #region Handler throws — Activity status Error

    [Fact]
    public async Task Handle_HandlerThrows_SetsActivityStatusError()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(
            new InvalidOperationException("Something broke"));

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _activities.Should().ContainSingle();
        _activities[0].Status.Should().Be(ActivityStatusCode.Error,
            "unhandled exceptions should set activity status to Error");
    }

    #endregion
}