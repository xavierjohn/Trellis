using Trellis.Testing;
namespace Trellis.Mediator.Tests;

using System.Diagnostics;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="TracingBehavior{TMessage, TResponse}"/>.
/// </summary>
[Collection(SerializedMediatorActivitySource.Name)]
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
            Result.Ok("Hello, Alice!"));

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
            Result.Fail<string>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), ValidationCodes.Unspecified) { Detail = "Bad input." }))));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _activities.Should().ContainSingle();
        var activity = _activities[0];
        activity.Status.Should().Be(ActivityStatusCode.Error);
        // ga-12: Detail is redacted from StatusDescription by default; only the stable Code
        // and type tags are emitted.
        activity.StatusDescription.Should().BeNullOrEmpty(
            "Error.Detail can carry user input or PII and must not leak into trace status descriptions by default");
        activity.GetTagItem("error.type").Should().Be("Error.InvalidInput");
        activity.GetTagItem("error.code").Should().Be(ValidationCodes.Unspecified,
            "Error.InvalidInput carries no explicit code, so the wire renders the sentinel and the span must spell it the same way");
        activity.GetTagItem("error.code").Should().Be(
            new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), ValidationCodes.Unspecified))).Code,
            "the tag is Error.Code, so it cannot drift from what the boundary publishes");
    }

    [Fact]
    public async Task Handle_FailedResult_TagsTheExplicitCode_WhenTheErrorCarriesOne()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Fail<string>(new Error.Forbidden("orders.write")));

        await behavior.Handle(new TestCommand("Alice"), next, CancellationToken.None);

        // The kind stays available on error.type, so narrowing error.code to the producer's own
        // decision loses nothing: the two tags now answer two different questions.
        _activities[0].GetTagItem("error.code").Should().Be("orders.write");
    }

    [Fact]
    public async Task Handle_FailedTransportResult_TagsTheFaultsOwnCode()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Fail<string>(new Error.TransportFault(new CodedFault("precondition-failed", "IfMatch"))));

        await behavior.Handle(new TestCommand("Alice"), next, CancellationToken.None);

        // A coded transport fault is the one case where the boundary emits the fault's own code
        // rather than the sentinel. The span has to agree, or the code in the response body cannot
        // be pasted into a trace query — which is the whole point of this dimension.
        _activities[0].GetTagItem("error.code").Should().Be("IfMatch");
    }

    private sealed record CodedFault(string Kind, string Code) : ICodedTransportFault;

    [Fact]
    public async Task Handle_FailedResult_IncludesDetailInDescription_WhenOptedIn()
    {
        var options = new TrellisMediatorTelemetryOptions { IncludeErrorDetail = true };
        var behavior = new TracingBehavior<TestCommand, Result<string>>(options);
        var command = new TestCommand("Alice");
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(
            Result.Fail<string>(new Error.NotFound(new ResourceRef("Order", "42")) { Detail = "order 42 for tenant acme" }));

        await behavior.Handle(command, next, CancellationToken.None);

        var activity = _activities.Should().ContainSingle().Subject;
        activity.StatusDescription.Should().Be("order 42 for tenant acme",
            "operators may explicitly opt in to including Error.Detail in trace output");
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
            Result.Ok("Hello!"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Hello!");
    }

    #endregion

    #region Activity name is message type name

    [Fact]
    public async Task Handle_ActivityName_IsMessageTypeName()
    {
        var behavior = new TracingBehavior<AdminCommand, Result<string>>();
        var command = new AdminCommand("data");
        var next = NextDelegate.ReturningAsync<AdminCommand, Result<string>>(
            Result.Ok("Done"));

        await behavior.Handle(command, next, CancellationToken.None);

        _activities.Should().ContainSingle();
        _activities[0].DisplayName.Should().Be("AdminCommand");
    }

    #endregion

    #region Handler cancellation — Activity status

    [Fact]
    public async Task Handle_RequestCancellation_WhenRequestTokenIsCanceled_RecordsExceptionEventWithoutErrorStatus()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        using var cts = new CancellationTokenSource();
        global::Mediator.MessageHandlerDelegate<TestCommand, Result<string>> next = (_, cancellationToken) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cancellationToken);
        };

        var act = async () => await behavior.Handle(command, next, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var activity = _activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Unset,
            "consumer-initiated cancellations should not be reported as OpenTelemetry errors");
        activity.StatusDescription.Should().BeNullOrEmpty(
            "Activity.SetStatus(Unset, ...) does not preserve description per BCL semantics; the otel.status_description tag is the stable queryable cancellation marker");
        activity.GetTagItem("otel.status_description").Should().Be("canceled",
            "consumer-initiated cancellations must emit a stable queryable marker so backends can aggregate canceled spans without special-casing the exception event");
        activity.GetTagItem("error.type").Should().BeNull();

        var exceptionEvent = activity.Events.Should().ContainSingle(e => e.Name == "exception").Subject;
        var exceptionTags = exceptionEvent.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
        exceptionTags.TryGetValue("exception.type", out var exceptionType).Should().BeTrue();
        exceptionType.Should().Be(typeof(OperationCanceledException).FullName);
    }

    [Fact]
    public async Task Handle_InternalCancellation_WhenRequestTokenIsAlsoCanceled_SetsActivityStatusError()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        using var requestCts = new CancellationTokenSource();
        using var internalCts = new CancellationTokenSource();
        requestCts.Cancel();
        internalCts.Cancel();
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(
            new OperationCanceledException(internalCts.Token));

        var act = async () => await behavior.Handle(command, next, requestCts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var activity = _activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error,
            "OperationCanceledException from a non-request token should preserve exception telemetry even when the request token is also canceled");
        activity.GetTagItem("error.type").Should().Be(nameof(OperationCanceledException));

        var exceptionEvent = activity.Events.Should().ContainSingle(e => e.Name == "exception").Subject;
        var exceptionTags = exceptionEvent.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
        exceptionTags.TryGetValue("exception.type", out var exceptionType).Should().BeTrue();
        exceptionType.Should().Be(typeof(OperationCanceledException).FullName);
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
        _activities[0].GetTagItem("error.type").Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task Handle_HandlerThrows_ExceptionEvent_ContainsTypeMessageAndStacktraceTags()
    {
        var exception = new InvalidOperationException("Something broke");
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(exception);

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var activity = _activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        var exceptionEvent = activity.Events.Should().ContainSingle(e => e.Name == "exception").Subject;
        var exceptionTags = exceptionEvent.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);

        exceptionTags.TryGetValue("exception.type", out var exceptionType).Should().BeTrue();
        exceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        exceptionTags.TryGetValue("exception.message", out var exceptionMessage).Should().BeTrue();
        exceptionMessage.Should().Be(exception.Message);
        exceptionTags.TryGetValue("exception.stacktrace", out var exceptionStacktrace).Should().BeTrue();
        exceptionStacktrace.Should().BeOfType<string>()
            .Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Handle_HandlerThrows_DoesNotLeakExceptionMessageInActivityStatus()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var command = new TestCommand("Alice");
        var next = NextDelegate.Throwing<TestCommand, Result<string>>(
            new InvalidOperationException("Connection string: Server=prod-db;Password=s3cret"));

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _activities.Should().ContainSingle();
        _activities[0].Status.Should().Be(ActivityStatusCode.Error);
        _activities[0].StatusDescription.Should().BeNullOrEmpty(
            "exception messages may contain secrets and must not be copied into telemetry status descriptions");
        _activities[0].GetTagItem("error.type").Should().Be("InvalidOperationException");
    }

    #endregion
}