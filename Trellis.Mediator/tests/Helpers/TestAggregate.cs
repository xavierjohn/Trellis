namespace Trellis.Mediator.Tests.Helpers;

using global::Mediator;

/// <summary>
/// Minimal aggregate implementation for domain-event dispatch tests.
/// Tracks domain events and exposes raise/accept for direct test control.
/// </summary>
public sealed class TestAggregate : Aggregate<TestAggregateId>
{
    public TestAggregate(TestAggregateId id) : base(id) { }

    public void RaiseEvent(IDomainEvent domainEvent)
        => DomainEvents.Add(domainEvent);
}

/// <summary>
/// Identifier for <see cref="TestAggregate"/>.
/// </summary>
public sealed record TestAggregateId(Guid Value);

/// <summary>
/// First test domain event.
/// </summary>
public sealed record TestEventA(string Payload, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Second test domain event.
/// </summary>
public sealed record TestEventB(int Value, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Command that returns a successful aggregate. Used to drive the dispatch behavior
/// through its happy path under test.
/// </summary>
internal sealed record AggregateCommand(TestAggregate Aggregate)
    : ICommand<Result<TestAggregate>>;

/// <summary>
/// Command whose response is a non-aggregate Result&lt;string&gt;. Used to verify the
/// dispatch behavior is a no-op for non-aggregate response shapes.
/// </summary>
internal sealed record StringCommand(string Value) : ICommand<Result<string>>;

/// <summary>
/// Command whose response is <see cref="Result{Unit}"/>. Used to verify the
/// dispatch behavior is a no-op for Unit responses (e.g., delete commands).
/// </summary>
internal sealed record UnitCommand : ICommand<Result<Trellis.Unit>>;

/// <summary>
/// Query (NOT a command) returning an aggregate. Used to verify the dispatch behavior
/// does not run for queries even when they return aggregate shapes.
/// </summary>
internal sealed record AggregateQuery(TestAggregate Aggregate)
    : IQuery<Result<TestAggregate>>;

/// <summary>
/// Recording <see cref="IDomainEventHandler{TEvent}"/> that captures invocations.
/// </summary>
internal sealed class RecordingHandlerA : IDomainEventHandler<TestEventA>
{
    public List<TestEventA> Received { get; } = [];

    public ValueTask HandleAsync(TestEventA domainEvent, CancellationToken cancellationToken)
    {
        Received.Add(domainEvent);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Recording handler for <see cref="TestEventB"/>.
/// </summary>
internal sealed class RecordingHandlerB : IDomainEventHandler<TestEventB>
{
    public List<TestEventB> Received { get; } = [];

    public ValueTask HandleAsync(TestEventB domainEvent, CancellationToken cancellationToken)
    {
        Received.Add(domainEvent);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Handler that throws to verify per-handler isolation (subsequent handlers and events still run).
/// </summary>
internal sealed class ThrowingHandlerA : IDomainEventHandler<TestEventA>
{
    public ValueTask HandleAsync(TestEventA domainEvent, CancellationToken cancellationToken)
        => throw new InvalidOperationException("handler-failed-on-purpose");
}

/// <summary>
/// Handler that implements <see cref="IDomainEventHandler{TEvent}"/> for both
/// <see cref="TestEventA"/> and <see cref="TestEventB"/>. Verifies that a single
/// concrete type registered for multiple event interfaces is invoked for each.
/// </summary>
internal sealed class MultiEventHandler : IDomainEventHandler<TestEventA>, IDomainEventHandler<TestEventB>
{
    public List<IDomainEvent> Received { get; } = [];

    public ValueTask HandleAsync(TestEventA domainEvent, CancellationToken cancellationToken)
    {
        Received.Add(domainEvent);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(TestEventB domainEvent, CancellationToken cancellationToken)
    {
        Received.Add(domainEvent);
        return ValueTask.CompletedTask;
    }
}
