namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="TrackedAggregateDomainEventDispatchBehavior{TMessage, TResponse}"/>.
/// The behavior auto-dispatches events from every aggregate the unit of work tracked at
/// commit time, sourced from <see cref="ITrackedAggregateSource"/> — independent of the
/// command's response shape.
/// </summary>
public class TrackedAggregateDomainEventDispatchBehaviorTests
{
    private static readonly TestAggregateId Id1 = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly TestAggregateId Id2 = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public async Task Outcome_DTO_command_dispatches_events_from_tracked_aggregate()
    {
        // The point of the tracked variant: an outcome-DTO handler (response is NOT
        // Result<TAggregate>) still gets its aggregate's events dispatched, because the
        // tracked-aggregate source — not the response shape — drives dispatch.
        var aggregate = new TestAggregate(Id1);
        var eventA = new TestEventA("dto-payload", DateTimeOffset.UtcNow);
        var eventB = new TestEventB(7, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(eventA);
        aggregate.RaiseEvent(eventB);

        var source = new FakeTrackedAggregateSource(aggregate);
        var publisher = new RecordingPublisher();
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var response = await behavior.Handle(
            new OutcomeDtoCommand("hello"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("hello-result"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().Equal(eventA, eventB);
        aggregate.UncommittedEvents().Should().BeEmpty();
        aggregate.IsChanged.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_response_does_not_dispatch()
    {
        var aggregate = new TestAggregate(Id1);
        aggregate.RaiseEvent(new TestEventA("payload", DateTimeOffset.UtcNow));

        var source = new FakeTrackedAggregateSource(aggregate);
        var publisher = new RecordingPublisher();
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var failure = Result.Fail<OutcomeDto>(new Error.NotFound(new ResourceRef("Aggregate", "missing")));
        var response = await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(failure),
            CancellationToken.None);

        response.IsFailure.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
        aggregate.UncommittedEvents().Should().HaveCount(1,
            "dispatch is skipped on failure; the events remain on the in-memory aggregate and are discarded with the request scope.");
    }

    [Fact]
    public async Task FailAfterCommit_response_does_not_dispatch()
    {
        // Issue #533 + #537 interaction: FailAfterCommit is still a failure, so even though
        // the transactional behavior committed, the tracked dispatcher must NOT fan out
        // events automatically. Handlers that need fan-out on permanent-failure paths must
        // call DispatchAggregateEventsAsync manually before returning FailAfterCommit, or
        // call AcceptChanges() to discard the events.
        var aggregate = new TestAggregate(Id1);
        var pendingEvent = new TestEventA("staged-during-failure", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(pendingEvent);

        var source = new FakeTrackedAggregateSource(aggregate);
        var publisher = new RecordingPublisher();
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var failAfterCommit = Result.FailAfterCommit<OutcomeDto>(new Error.Conflict(new ResourceRef("Aggregate", "id"), "duplicate_key"));
        var response = await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(failAfterCommit),
            CancellationToken.None);

        response.IsFailure.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
        aggregate.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(pendingEvent);
    }

    [Fact]
    public async Task Empty_committed_aggregates_returns_without_publishing()
    {
        var source = new FakeTrackedAggregateSource();
        var publisher = new RecordingPublisher();
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var response = await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("done"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Multiple_aggregates_each_dispatched()
    {
        var aggregateA = new TestAggregate(Id1);
        var aggregateB = new TestAggregate(Id2);
        var eventFromA = new TestEventA("from-A", DateTimeOffset.UtcNow);
        var eventFromB = new TestEventB(99, DateTimeOffset.UtcNow);
        aggregateA.RaiseEvent(eventFromA);
        aggregateB.RaiseEvent(eventFromB);

        var source = new FakeTrackedAggregateSource(aggregateA, aggregateB);
        var publisher = new RecordingPublisher();
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var response = await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("done"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().Contain(eventFromA);
        publisher.Published.Should().Contain(eventFromB);
        publisher.Published.Should().HaveCount(2);
        aggregateA.UncommittedEvents().Should().BeEmpty();
        aggregateB.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Cross_aggregate_cascade_throws_with_offending_aggregate_only()
    {
        // Handler running for aggregateA raises an event on aggregateB. Strict snapshot dispatch
        // publishes only the entry snapshots, then reports B as the only aggregate whose list changed.
        var aggregateA = new TestAggregate(Id1);
        var aggregateB = new TestAggregate(Id2);

        var triggerEvent = new TestEventA("trigger", DateTimeOffset.UtcNow);
        var cascadedEvent = new TestEventB(123, DateTimeOffset.UtcNow);
        aggregateA.RaiseEvent(triggerEvent);

        var source = new FakeTrackedAggregateSource(aggregateA, aggregateB);
        var publisher = new RecordingPublisher
        {
            OnPublishing = published =>
            {
                if (ReferenceEquals(published, triggerEvent))
                    aggregateB.RaiseEvent(cascadedEvent);
            },
        };
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var act = async () => await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("done"))),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        var offender = ex.Which.Offenders.Should().ContainSingle().Which;
        offender.AggregateType.Should().Be<TestAggregate>();
        offender.CascadedEventTypeNames.Should().Equal([typeof(TestEventB).FullName!]);
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(triggerEvent);
        aggregateA.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(triggerEvent);
        aggregateB.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(cascadedEvent);
    }

    [Fact]
    public async Task Cascades_on_multiple_aggregates_throw_with_all_offenders()
    {
        var aggregateA = new TestAggregate(Id1);
        var aggregateB = new TestAggregate(Id2);
        var eventFromA = new TestEventA("from-A", DateTimeOffset.UtcNow);
        var eventFromB = new TestEventB(99, DateTimeOffset.UtcNow);
        var cascadeFromA = new TestEventB(100, DateTimeOffset.UtcNow);
        var cascadeFromB = new TestEventA("from-B-cascade", DateTimeOffset.UtcNow);
        aggregateA.RaiseEvent(eventFromA);
        aggregateB.RaiseEvent(eventFromB);

        var source = new FakeTrackedAggregateSource(aggregateA, aggregateB);
        var publisher = new RecordingPublisher
        {
            OnPublishing = published =>
            {
                if (ReferenceEquals(published, eventFromA))
                    aggregateA.RaiseEvent(cascadeFromA);
                if (ReferenceEquals(published, eventFromB))
                    aggregateB.RaiseEvent(cascadeFromB);
            },
        };
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var act = async () => await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("done"))),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        ex.Which.Offenders.Should().HaveCount(2);
        ex.Which.Offenders[0].CascadedEventTypeNames.Should().Equal([typeof(TestEventB).FullName!]);
        ex.Which.Offenders[1].CascadedEventTypeNames.Should().Equal([typeof(TestEventA).FullName!]);
        publisher.Published.Should().Equal(eventFromA, eventFromB);
        aggregateA.UncommittedEvents().Should().Equal(new IDomainEvent[] { eventFromA, cascadeFromA });
        aggregateB.UncommittedEvents().Should().Equal(new IDomainEvent[] { eventFromB, cascadeFromB });
    }

    [Fact]
    public async Task Same_aggregate_cascade_throws_and_skips_accept_changes_for_all_aggregates()
    {
        var aggregateA = new TestAggregate(Id1);
        var aggregateB = new TestAggregate(Id2);
        var seed = new TestEventA("seed", DateTimeOffset.UtcNow);
        var untouched = new TestEventB(7, DateTimeOffset.UtcNow);
        TestEventA? cascaded = null;
        aggregateA.RaiseEvent(seed);
        aggregateB.RaiseEvent(untouched);

        var source = new FakeTrackedAggregateSource(aggregateA, aggregateB);
        var publisher = new RecordingPublisher
        {
            OnPublishing = published =>
            {
                if (!ReferenceEquals(published, seed)) return;

                cascaded = new TestEventA("cascade", DateTimeOffset.UtcNow);
                aggregateA.RaiseEvent(cascaded);
            },
        };
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var act = async () => await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("done"))),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        var offender = ex.Which.Offenders.Should().ContainSingle().Which;
        offender.AggregateType.Should().Be<TestAggregate>();
        offender.CascadedEventTypeNames.Should().Equal([typeof(TestEventA).FullName!]);
        publisher.Published.Should().Equal(seed, untouched);
        aggregateA.UncommittedEvents().Should().Equal(new IDomainEvent[] { seed, cascaded! });
        aggregateB.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(untouched,
            "no aggregate is cleared when any aggregate cascades");
    }

    [Fact]
    public async Task Reentrant_call_from_handler_skips_nested_dispatch()
    {
        // A domain-event handler that calls IMediator.Send re-enters the behavior on the
        // same async control flow. The AsyncLocal guard short-circuits the nested call so
        // the inner snapshot does NOT publish events, and the outer dispatch retains
        // ownership of its snapshot.
        var outerAggregate = new TestAggregate(Id1);
        var innerAggregate = new TestAggregate(Id2);
        var outerEvent = new TestEventA("outer", DateTimeOffset.UtcNow);
        var innerEvent = new TestEventB(42, DateTimeOffset.UtcNow);
        outerAggregate.RaiseEvent(outerEvent);
        innerAggregate.RaiseEvent(innerEvent);

        var outerSource = new FakeTrackedAggregateSource(outerAggregate);
        var innerSource = new FakeTrackedAggregateSource(innerAggregate);
        var publisher = new RecordingPublisher();

        var innerBehavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(innerSource, publisher);

        publisher.OnPublishing = async published =>
        {
            if (!ReferenceEquals(published, outerEvent)) return;

            // Simulate a nested mediator dispatch: the inner behavior runs on the same async flow.
            await innerBehavior.Handle(
                new OutcomeDtoCommand("nested"),
                (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("nested-result"))),
                CancellationToken.None);
        };

        var outerBehavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(outerSource, publisher);

        var response = await outerBehavior.Handle(
            new OutcomeDtoCommand("outer"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("outer-result"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(outerEvent);
        innerAggregate.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(innerEvent);
    }

    [Fact]
    public async Task Reentrant_call_with_different_closed_generic_also_skips_nested_dispatch()
    {
        // Drift guard for the re-entrancy contract: a nested command typically has a
        // DIFFERENT (TMessage, TResponse) shape from the outer command (e.g. the outer is
        // an outcome-DTO command and the nested is a Result<TAggregate> command). If the
        // re-entrancy flag lived as a static field on the generic behavior, each closed
        // generic would have its own AsyncLocal cell and the nested closed generic would
        // never observe IsInDispatch == true — bypassing the guard. This test enforces
        // the guard is shared across all closed-generic instantiations.
        var outerAggregate = new TestAggregate(Id1);
        var innerAggregate = new TestAggregate(Id2);
        var outerEvent = new TestEventA("outer", DateTimeOffset.UtcNow);
        var innerEvent = new TestEventB(42, DateTimeOffset.UtcNow);
        outerAggregate.RaiseEvent(outerEvent);
        innerAggregate.RaiseEvent(innerEvent);

        var outerSource = new FakeTrackedAggregateSource(outerAggregate);
        var innerSource = new FakeTrackedAggregateSource(innerAggregate);
        var publisher = new RecordingPublisher();

        // Outer and inner use DIFFERENT closed-generic message/response types.
        var outerBehavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(outerSource, publisher);
        var innerBehavior = NewBehavior<AggregateCommand, Result<TestAggregate>>(innerSource, publisher);

        publisher.OnPublishing = async published =>
        {
            if (!ReferenceEquals(published, outerEvent)) return;

            await innerBehavior.Handle(
                new AggregateCommand(innerAggregate),
                (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(innerAggregate)),
                CancellationToken.None);
        };

        var response = await outerBehavior.Handle(
            new OutcomeDtoCommand("outer"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("outer-result"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(outerEvent,
            "the nested closed-generic behavior must observe the same re-entrancy flag, " +
            "otherwise it would publish innerEvent too.");
        innerAggregate.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(innerEvent);
    }

    [Fact]
    public async Task Aggregate_in_response_but_not_in_source_is_not_dispatched()
    {
        // No reflection on TResponse: even if a Result<TestAggregate> is returned, the
        // tracked dispatcher publishes only what ITrackedAggregateSource exposes. If the
        // source is empty, nothing is dispatched even though the response payload IS an
        // aggregate carrying events.
        var responseAggregate = new TestAggregate(Id1);
        responseAggregate.RaiseEvent(new TestEventA("from-response", DateTimeOffset.UtcNow));

        var source = new FakeTrackedAggregateSource(); // empty: nothing committed
        var publisher = new RecordingPublisher();
        var behavior = NewBehavior<AggregateCommand, Result<TestAggregate>>(source, publisher);

        var response = await behavior.Handle(
            new AggregateCommand(responseAggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(responseAggregate)),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
        responseAggregate.UncommittedEvents().Should().HaveCount(1);
    }

    /// <summary>
    /// Tracked dispatch runs after <c>TransactionalCommandBehavior</c> commits (the registration
    /// re-appends the transactional behavior as innermost), so a mid-fan-out cancellation must not
    /// leave the committed aggregates with only some of their events published.
    /// </summary>
    [Fact]
    public async Task Cancellation_during_dispatch_completes_fan_out_and_accepts_changes()
    {
        var aggregate = new TestAggregate(Id1);
        var first = new TestEventA("first", DateTimeOffset.UtcNow);
        var second = new TestEventB(2, DateTimeOffset.UtcNow);
        var third = new TestEventA("third", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(first);
        aggregate.RaiseEvent(second);
        aggregate.RaiseEvent(third);

        using var cts = new CancellationTokenSource();
        var source = new FakeTrackedAggregateSource(aggregate);
        var publisher = new RecordingPublisher
        {
            OnPublishing = published =>
            {
                if (ReferenceEquals(published, second))
                    cts.Cancel();
            },
        };
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var response = await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("done"))),
            cts.Token);

        response.IsSuccess.Should().BeTrue();
        publisher.Published.Should().Equal(first, second, third);
        aggregate.UncommittedEvents().Should().BeEmpty("the full fan-out completed, so AcceptChanges runs");
    }

    [Fact]
    public async Task Dispatch_does_not_propagate_callers_token_to_handlers()
    {
        var aggregate = new TestAggregate(Id1);
        aggregate.RaiseEvent(new TestEventA("payload", DateTimeOffset.UtcNow));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var source = new FakeTrackedAggregateSource(aggregate);
        var publisher = new RecordingPublisher();
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("done"))),
            cts.Token);

        publisher.ObservedTokens.Should().ContainSingle()
            .Which.CanBeCanceled.Should().BeFalse("post-commit dispatch passes CancellationToken.None so handlers always run to completion");
    }

    [Fact]
    public async Task Reentrant_guard_is_cleared_after_successful_dispatch()
    {
        // Two sequential commands on the same async flow: the second must not see the first
        // command's re-entry flag (the finally clears it).
        var aggregate1 = new TestAggregate(Id1);
        var aggregate2 = new TestAggregate(Id2);
        var event1 = new TestEventA("one", DateTimeOffset.UtcNow);
        var event2 = new TestEventB(2, DateTimeOffset.UtcNow);
        aggregate1.RaiseEvent(event1);
        aggregate2.RaiseEvent(event2);

        var source1 = new FakeTrackedAggregateSource(aggregate1);
        var source2 = new FakeTrackedAggregateSource(aggregate2);
        var publisher = new RecordingPublisher();

        var behavior1 = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source1, publisher);
        var behavior2 = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source2, publisher);

        var r1 = await behavior1.Handle(
            new OutcomeDtoCommand("first"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("first-result"))),
            CancellationToken.None);
        var r2 = await behavior2.Handle(
            new OutcomeDtoCommand("second"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("second-result"))),
            CancellationToken.None);

        r1.IsSuccess.Should().BeTrue();
        r2.IsSuccess.Should().BeTrue();
        publisher.Published.Should().Equal(event1, event2);
    }

    [Fact]
    public async Task Publisher_exception_propagates()
    {
        // A throwing publisher surfaces the exception up through the pipeline. AcceptChanges
        // never runs on the throw path, so undispatched events stay on the aggregate.
        var aggregate = new TestAggregate(Id1);
        var first = new TestEventA("first", DateTimeOffset.UtcNow);
        var second = new TestEventA("second", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(first);
        aggregate.RaiseEvent(second);

        var source = new FakeTrackedAggregateSource(aggregate);
        var publisher = new RecordingPublisher
        {
            OnPublishing = published =>
            {
                if (ReferenceEquals(published, second))
                    throw new InvalidOperationException("publisher-failed");
            },
        };
        var behavior = NewBehavior<OutcomeDtoCommand, Result<OutcomeDto>>(source, publisher);

        var act = async () => await behavior.Handle(
            new OutcomeDtoCommand("x"),
            (_, _) => new ValueTask<Result<OutcomeDto>>(Result.Ok(new OutcomeDto("done"))),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("publisher-failed");
        publisher.Published.Should().Equal(new[] { first },
            "the throw on 'second' fires before it is recorded, so only 'first' was published.");
        aggregate.UncommittedEvents().Should().HaveCount(2,
            "AcceptChanges does not run on the throw path, so all events stay on the aggregate.");
    }

    private static TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>
        NewBehavior<TMessage, TResponse>(ITrackedAggregateSource source, IDomainEventPublisher publisher)
        where TMessage : global::Mediator.ICommand<TResponse>
        where TResponse : IResult
        => new(
            source,
            publisher,
            NullLogger<TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>>.Instance);

    private sealed class FakeTrackedAggregateSource : ITrackedAggregateSource
    {
        private readonly IAggregate[] _aggregates;

        public FakeTrackedAggregateSource(params IAggregate[] aggregates) => _aggregates = aggregates;

        public IReadOnlyList<IAggregate> CommittedAggregates => _aggregates;
    }

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> Published { get; } = [];
        public List<CancellationToken> ObservedTokens { get; } = [];
        public Func<IDomainEvent, Task>? OnPublishingAsync { get; set; }
        public Action<IDomainEvent>? OnPublishing { get; set; }

        public async ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            // Invoke the synchronous callback FIRST so a test that injects an event-raising
            // callback observes the publishing-then-record ordering. Throws propagate.
            OnPublishing?.Invoke(domainEvent);
            if (OnPublishingAsync is not null)
                await OnPublishingAsync(domainEvent).ConfigureAwait(false);
            Published.Add(domainEvent);
            ObservedTokens.Add(cancellationToken);
        }
    }
}

/// <summary>
/// Outcome-DTO returned by an outcome-DTO command whose handler mutates an aggregate
/// without making it the response payload. Used to verify that the tracked-aggregate
/// dispatcher fires for non-aggregate response shapes.
/// </summary>
internal sealed record OutcomeDto(string Status);

/// <summary>
/// Command whose handler mutates a tracked aggregate but returns a non-aggregate
/// outcome DTO. Exercises the tracked-aggregate dispatcher's response-shape-agnostic path.
/// </summary>
internal sealed record OutcomeDtoCommand(string Input) : global::Mediator.ICommand<Result<OutcomeDto>>;
