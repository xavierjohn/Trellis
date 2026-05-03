namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>.
/// </summary>
public class DomainEventDispatchBehaviorTests
{
    private static readonly TestAggregateId Id1 = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public async Task Handle_SuccessfulAggregateResult_DispatchesEachEvent_AndAcceptsChanges()
    {
        var aggregate = new TestAggregate(Id1);
        var eventA = new TestEventA("first", DateTimeOffset.UtcNow);
        var eventB = new TestEventB(42, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(eventA);
        aggregate.RaiseEvent(eventB);

        var publisher = new RecordingPublisher();
        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var response = await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().HaveCount(2);
        publisher.Published[0].Should().BeSameAs(eventA);
        publisher.Published[1].Should().BeSameAs(eventB);
        aggregate.UncommittedEvents().Should().BeEmpty();
        aggregate.IsChanged.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_FailedResult_DoesNotDispatch_AndDoesNotAccept()
    {
        var aggregate = new TestAggregate(Id1);
        aggregate.RaiseEvent(new TestEventA("payload", DateTimeOffset.UtcNow));

        var publisher = new RecordingPublisher();
        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var failure = Result.Fail<TestAggregate>(new Error.NotFound(new ResourceRef("Aggregate", "missing")));
        var response = await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(failure),
            CancellationToken.None);

        response.IsFailure.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
        aggregate.UncommittedEvents().Should().HaveCount(1, "events stay on the aggregate when the command fails so they can be retried by a re-issued command");
    }

    [Fact]
    public async Task Handle_NonAggregateResponse_IsNoOp()
    {
        var publisher = new RecordingPublisher();
        var behavior = new DomainEventDispatchBehavior<StringCommand, Result<string>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<StringCommand, Result<string>>>.Instance);

        var response = await behavior.Handle(
            new StringCommand("hello"),
            (_, _) => new ValueTask<Result<string>>(Result.Ok("hello")),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AggregateWithNoEvents_IsNoOp()
    {
        var aggregate = new TestAggregate(Id1);
        var publisher = new RecordingPublisher();
        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var response = await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_HandlerRaisesNewEvent_DrainedOnNextWave_AndAccepted()
    {
        var aggregate = new TestAggregate(Id1);
        var firstEvent = new TestEventA("first", DateTimeOffset.UtcNow);
        var followUp = new TestEventB(99, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(firstEvent);

        var publisher = new RecordingPublisher();
        // First-wave handler for TestEventA raises a follow-up TestEventB on the same aggregate.
        publisher.OnPublishing = e =>
        {
            if (ReferenceEquals(e, firstEvent))
                aggregate.RaiseEvent(followUp);
        };

        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var response = await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().Equal(firstEvent, followUp);
        aggregate.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RunawayHandler_CapsAtMaxWaves_AndLogsAndClears()
    {
        var aggregate = new TestAggregate(Id1);
        var seed = new TestEventA("seed", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(seed);

        var publisher = new RecordingPublisher();
        // Every publish of a TestEventA raises another TestEventA — runaway.
        publisher.OnPublishing = e =>
        {
            if (e is TestEventA)
                aggregate.RaiseEvent(new TestEventA("cascade", DateTimeOffset.UtcNow));
        };

        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var response = await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().HaveCount(DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>.MaxDispatchWaves);
        aggregate.UncommittedEvents().Should().BeEmpty("the cap-exceeded path defensively clears the aggregate so it does not stay dirty across requests");
    }

    [Fact]
    public async Task Handle_CancellationBeforeDispatch_DoesNotPublish_PreservesEvents_AndPropagates()
    {
        var aggregate = new TestAggregate(Id1);
        var preserved = new TestEventA("payload", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(preserved);

        var publisher = new RecordingPublisher();
        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        publisher.Published.Should().BeEmpty();
        aggregate.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(preserved,
            "cancellation must not strip events from the aggregate — they remain available for retry");
        aggregate.IsChanged.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CancellationMidDispatch_PreservesUndispatchedEvents()
    {
        var aggregate = new TestAggregate(Id1);
        var first = new TestEventA("first", DateTimeOffset.UtcNow);
        var second = new TestEventB(2, DateTimeOffset.UtcNow);
        var third = new TestEventA("third", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(first);
        aggregate.RaiseEvent(second);
        aggregate.RaiseEvent(third);

        using var cts = new CancellationTokenSource();
        var publisher = new RecordingPublisher();
        // Cancel after the second event is published; the third must remain on the aggregate.
        publisher.OnPublishing = e =>
        {
            if (ReferenceEquals(e, second))
                cts.Cancel();
        };

        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var act = async () => await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        publisher.Published.Should().Equal(first, second);
        aggregate.UncommittedEvents().Should().Equal(
            new IDomainEvent[] { first, second, third },
            "AcceptChanges() never runs on cancellation, so the entire event list stays on the aggregate. Handlers must be idempotent because a retry will re-publish events that already fired before cancellation.");
    }

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> Published { get; } = [];
        public Action<IDomainEvent>? OnPublishing { get; set; }

        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            OnPublishing?.Invoke(domainEvent);
            Published.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }
}
