namespace Trellis.Mediator.Tests;

using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="DomainEventPublisherExtensions.DispatchAggregateEventsAsync"/>.
/// Mirrors the snapshot and cancellation contracts already covered by
/// <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/> but for the standalone helper.
/// </summary>
public class DomainEventPublisherExtensionsTests
{
    private static readonly TestAggregateId Id1 = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public async Task Dispatches_each_event_in_order_and_calls_accept_changes()
    {
        var aggregate = new TestAggregate(Id1);
        var eventA = new TestEventA("first", DateTimeOffset.UtcNow);
        var eventB = new TestEventB(42, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(eventA);
        aggregate.RaiseEvent(eventB);

        var publisher = new RecordingPublisher();

        await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        publisher.Published.Should().HaveCount(2);
        publisher.Published[0].Should().BeSameAs(eventA);
        publisher.Published[1].Should().BeSameAs(eventB);
        aggregate.UncommittedEvents().Should().BeEmpty("AcceptChanges runs on the full-success path");
        aggregate.IsChanged.Should().BeFalse();
    }

    [Fact]
    public async Task No_events_calls_accept_changes_and_publishes_nothing()
    {
        var aggregate = new TestAggregate(Id1);
        var publisher = new RecordingPublisher();

        await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        publisher.Published.Should().BeEmpty();
        aggregate.IsChanged.Should().BeFalse();
    }

    [Fact]
    public async Task Events_raised_during_dispatch_throw_and_preserve_events()
    {
        var aggregate = new TestAggregate(Id1);
        var first = new TestEventA("first", DateTimeOffset.UtcNow);
        var cascaded = new TestEventB(99, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(first);

        var publisher = new RecordingPublisher
        {
            OnPublishing = evt =>
            {
                if (ReferenceEquals(evt, first))
                    aggregate.RaiseEvent(cascaded);
            },
        };

        var act = async () => await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        var offender = ex.Which.Offenders.Should().ContainSingle().Which;
        offender.AggregateType.Should().Be<TestAggregate>();
        offender.CascadedEventTypeNames.Should().Equal([typeof(TestEventB).FullName!]);
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(first);
        aggregate.UncommittedEvents().Should().Equal(new IDomainEvent[] { first, cascaded });
    }

    [Fact]
    public async Task Repeated_cascade_throws_and_leaves_events_on_aggregate()
    {
        var aggregate = new TestAggregate(Id1);
        var seed = new TestEventA("seed", DateTimeOffset.UtcNow);
        TestEventA? cascaded = null;
        aggregate.RaiseEvent(seed);

        var publisher = new RecordingPublisher
        {
            OnPublishing = _ =>
            {
                cascaded = new TestEventA("cascade", DateTimeOffset.UtcNow);
                aggregate.RaiseEvent(cascaded);
            },
        };

        var act = async () => await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        var offender = ex.Which.Offenders.Should().ContainSingle().Which;
        offender.AggregateType.Should().Be<TestAggregate>();
        offender.CascadedEventTypeNames.Should().Equal([typeof(TestEventA).FullName!]);
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(seed);
        aggregate.UncommittedEvents().Should().Equal(new IDomainEvent[] { seed, cascaded! },
            "the helper leaves cascaded events on the aggregate so the caller can inspect them");
        aggregate.IsChanged.Should().BeTrue("AcceptChanges is not called when cascade detection fails");
    }

    [Fact]
    public async Task Handler_accepts_changes_then_raises_new_event_throws_and_preserves_new_event()
    {
        var aggregate = new TestAggregate(Id1);
        var seed = new TestEventA("seed", DateTimeOffset.UtcNow);
        var replacement = new TestEventB(42, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(seed);

        var publisher = new RecordingPublisher
        {
            OnPublishing = evt =>
            {
                if (!ReferenceEquals(evt, seed)) return;

                aggregate.AcceptChanges();
                aggregate.RaiseEvent(replacement);
            },
        };

        var act = async () => await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<DomainEventHandlerCascadedException>();
        var offender = ex.Which.Offenders.Should().ContainSingle().Which;
        offender.AggregateType.Should().Be<TestAggregate>();
        offender.CascadedEventTypeNames.Should().Equal([typeof(TestEventB).FullName!]);
        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(seed);
        aggregate.UncommittedEvents().Should().ContainSingle().Which.Should().BeSameAs(replacement);
    }

    /// <summary>
    /// The helper is documented for post-commit call sites, so a token canceled mid-loop must
    /// not strand an already-durable write with a partially published event set.
    /// </summary>
    [Fact]
    public async Task Cancellation_mid_loop_completes_fan_out_and_accepts_changes()
    {
        var aggregate = new TestAggregate(Id1);
        var first = new TestEventA("first", DateTimeOffset.UtcNow);
        var second = new TestEventA("second", DateTimeOffset.UtcNow);
        var third = new TestEventA("third", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(first);
        aggregate.RaiseEvent(second);
        aggregate.RaiseEvent(third);

        using var cts = new CancellationTokenSource();
        var publisher = new RecordingPublisher
        {
            OnPublishing = evt =>
            {
                if (ReferenceEquals(evt, second))
                    cts.Cancel();
            },
        };

        await publisher.DispatchAggregateEventsAsync(aggregate, cts.Token);

        publisher.Published.Should().Equal(first, second, third);
        aggregate.UncommittedEvents().Should().BeEmpty("the full fan-out completed, so AcceptChanges runs");
    }

    [Fact]
    public async Task Pre_canceled_token_still_publishes_and_accepts_changes()
    {
        var aggregate = new TestAggregate(Id1);
        var only = new TestEventA("should-publish", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(only);

        var publisher = new RecordingPublisher();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await publisher.DispatchAggregateEventsAsync(aggregate, cts.Token);

        publisher.Published.Should().ContainSingle().Which.Should().BeSameAs(only);
        aggregate.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_propagate_callers_token_to_handlers()
    {
        var aggregate = new TestAggregate(Id1);
        aggregate.RaiseEvent(new TestEventA("payload", DateTimeOffset.UtcNow));

        var publisher = new RecordingPublisher();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await publisher.DispatchAggregateEventsAsync(aggregate, cts.Token);

        publisher.ObservedTokens.Should().ContainSingle()
            .Which.CanBeCanceled.Should().BeFalse("post-commit dispatch passes CancellationToken.None so handlers always run to completion");
    }

    [Fact]
    public async Task Calling_twice_does_not_re_publish_first_call_events()
    {
        var aggregate = new TestAggregate(Id1);
        var firstWave = new TestEventA("first-wave", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(firstWave);

        var publisher = new RecordingPublisher();

        await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        publisher.Published.Should().HaveCount(1);

        // Second call with a fresh event; the first-call event is already cleared by AcceptChanges.
        var secondWave = new TestEventB(7, DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(secondWave);

        await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        publisher.Published.Should().Equal(firstWave, secondWave);
        aggregate.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Null_publisher_throws_argument_null()
    {
        var aggregate = new TestAggregate(Id1);
        IDomainEventPublisher publisher = null!;

        var act = async () => await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>().Where(e => e.ParamName == "publisher");
    }

    [Fact]
    public async Task Null_aggregate_throws_argument_null()
    {
        var publisher = new RecordingPublisher();

        var act = async () => await publisher.DispatchAggregateEventsAsync(null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>().Where(e => e.ParamName == "aggregate");
    }

    [Fact]
    public async Task Publisher_exception_propagates_and_skips_accept_changes()
    {
        // Locks in the documented contract: if the IDomainEventPublisher implementation propagates
        // a handler exception (rather than swallowing it like MediatorDomainEventPublisher does),
        // the helper rethrows and AcceptChanges() is NOT called so undispatched events remain on
        // the aggregate for the caller to inspect.
        var aggregate = new TestAggregate(Id1);
        var first = new TestEventA("first", DateTimeOffset.UtcNow);
        var second = new TestEventA("second", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(first);
        aggregate.RaiseEvent(second);

        var publisher = new ThrowingPublisher(throwOn: second, new InvalidOperationException("handler-blew-up"));

        var act = async () => await publisher.DispatchAggregateEventsAsync(aggregate, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("handler-blew-up");
        publisher.Published.Should().Equal(first);
        aggregate.UncommittedEvents().Should().Equal(
            new IDomainEvent[] { first, second },
            "AcceptChanges() is not called when a publisher propagates a handler exception, so the entire event list stays on the aggregate");
        aggregate.IsChanged.Should().BeTrue();
    }

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> Published { get; } = [];

        public List<CancellationToken> ObservedTokens { get; } = [];

        public Action<IDomainEvent>? OnPublishing { get; set; }

        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            OnPublishing?.Invoke(domainEvent);
            Published.Add(domainEvent);
            ObservedTokens.Add(cancellationToken);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingPublisher : IDomainEventPublisher
    {
        private readonly IDomainEvent _throwOn;
        private readonly Exception _exception;

        public ThrowingPublisher(IDomainEvent throwOn, Exception exception)
        {
            _throwOn = throwOn;
            _exception = exception;
        }

        public List<IDomainEvent> Published { get; } = [];

        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            if (ReferenceEquals(domainEvent, _throwOn))
                throw _exception;

            Published.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }
}
