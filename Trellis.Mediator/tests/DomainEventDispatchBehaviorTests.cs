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
        aggregate.UncommittedEvents().Should().HaveCount(1, "dispatch is skipped on failure, so the events the handler raised remain on the in-memory aggregate instance and are discarded with the request scope");
    }

    /// <summary>
    /// Issue #533 regression: a persist-on-failure outcome (created via
    /// <c>Result.FailAfterCommit&lt;TAggregate&gt;(error)</c>) is still a failure, so
    /// <c>DomainEventDispatchBehavior</c> must not fan out events. The commit happens upstream
    /// in <c>TransactionalCommandBehavior</c>; this behavior only handles event dispatch, and
    /// the rule "no dispatch on failure" continues to apply. Any events the handler raised
    /// stay on the in-memory aggregate instance and are discarded with the request scope —
    /// if the permanent-failure transition needs to drive downstream work, write an outbox
    /// row inside the same handler so it commits alongside the state change.
    /// </summary>
    [Fact]
    public async Task Handle_FailAfterCommitResult_DoesNotDispatch_AndLeavesEventsOnAggregate()
    {
        var aggregate = new TestAggregate(Id1);
        var pendingEvent = new TestEventA("staged-during-failure", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(pendingEvent);

        var publisher = new RecordingPublisher();
        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var persistOnFailure = Result.FailAfterCommit<TestAggregate>(
            new Error.Conflict(null, "external.permanent_failure") { Detail = "gateway rejected" });
        var response = await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(persistOnFailure),
            CancellationToken.None);

        response.IsFailure.Should().BeTrue();
        ((IPersistOnFailure)response).PersistOnFailure.Should().BeTrue(
            "the response shape is preserved end-to-end — the failure stays opt-in to commit");
        publisher.Published.Should().BeEmpty(
            "FailAfterCommit is still a failure; event dispatch must not run");
        aggregate.UncommittedEvents().Should().HaveCount(1,
            "dispatch is skipped, so the events the handler raised remain on the in-memory aggregate instance");
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
    public async Task Handle_UnitResponse_IsNoOp()
    {
        var publisher = new RecordingPublisher();
        var behavior = new DomainEventDispatchBehavior<UnitCommand, Result<Trellis.Unit>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<UnitCommand, Result<Trellis.Unit>>>.Instance);

        var response = await behavior.Handle(
            new UnitCommand(),
            (_, _) => new ValueTask<Result<Trellis.Unit>>(Result.Ok()),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().BeEmpty(
            "Result<Unit> commands have no aggregate to drain — dispatch is a documented no-op for this shape in v1");
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
    public async Task Handle_HandlerRaisesNewEvent_ThrowsCascadedException_AndPreservesEvents()
    {
        var aggregate = new TestAggregate(Id1);
        var firstEvent = new TestEventA("first", DateTimeOffset.UtcNow);
        var followUp = new TestEventB(99, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(firstEvent);

        var publisher = new RecordingPublisher
        {
            OnPublishing = e =>
            {
                if (ReferenceEquals(e, firstEvent))
                    aggregate.RaiseEvent(followUp);
            },
        };

        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var act = async () => await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        var offender = ex.Which.Offenders.Should().ContainSingle().Which;
        offender.AggregateType.Should().Be<TestAggregate>();
        offender.CascadedEventTypeNames.Should().Equal([typeof(TestEventB).FullName!]);
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(firstEvent);
        aggregate.UncommittedEvents().Should().Equal(new IDomainEvent[] { firstEvent, followUp },
            "AcceptChanges must not run when a handler cascades events");
        aggregate.IsChanged.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RunawayHandler_ThrowsCascadedException_AndDoesNotClearEvents()
    {
        var aggregate = new TestAggregate(Id1);
        var seed = new TestEventA("seed", DateTimeOffset.UtcNow);
        TestEventA? cascaded = null;
        aggregate.RaiseEvent(seed);

        var publisher = new RecordingPublisher
        {
            OnPublishing = e =>
            {
                if (e is not TestEventA) return;

                cascaded = new TestEventA("cascade", DateTimeOffset.UtcNow);
                aggregate.RaiseEvent(cascaded);
            },
        };

        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var act = async () => await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        var offender = ex.Which.Offenders.Should().ContainSingle().Which;
        offender.AggregateType.Should().Be<TestAggregate>();
        offender.CascadedEventTypeNames.Should().Equal([typeof(TestEventA).FullName!]);
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(seed);
        aggregate.UncommittedEvents().Should().Equal(new IDomainEvent[] { seed, cascaded! },
            "cascade detection leaves the aggregate dirty so an operator can inspect the undispatched event");
    }

    [Fact]
    public async Task Handle_HandlerAcceptsChangesThenRaisesNewEvent_ThrowsCascadedException_AndPreservesNewEvent()
    {
        var aggregate = new TestAggregate(Id1);
        var seed = new TestEventA("seed", DateTimeOffset.UtcNow);
        var replacement = new TestEventB(42, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(seed);

        var publisher = new RecordingPublisher
        {
            OnPublishing = e =>
            {
                if (!ReferenceEquals(e, seed)) return;

                aggregate.AcceptChanges();
                aggregate.RaiseEvent(replacement);
            },
        };

        var behavior = new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance);

        var act = async () => await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        var offender = ex.Which.Offenders.Should().ContainSingle().Which;
        offender.AggregateType.Should().Be<TestAggregate>();
        offender.CascadedEventTypeNames.Should().Equal([typeof(TestEventB).FullName!]);
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(seed);
        aggregate.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(replacement,
            "reference-equality validation catches handlers that clear the original snapshot before raising a replacement event");
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
