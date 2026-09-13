# Trellis.EntityFrameworkCore.Outbox

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Outbox.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox)

Transactional outbox for Trellis: capture aggregate domain events into an EF Core table in the **same transaction** as the aggregate change, then relay them to your Trellis domain-event handlers after the commit succeeds — durable, at-least-once, in-process dispatch.

## Installation
```bash
dotnet add package Trellis.EntityFrameworkCore.Outbox
```

## Why
The default in-pipeline domain-event dispatch fires *after* the transaction commits, so a crash between commit and dispatch loses the events. The outbox closes that gap: each event is written atomically with the aggregate, survives a crash, and is re-dispatched by a background relay.

## Quick Example
Three wiring points — the table, the capture interceptor, and the relay.

```csharp
// Raise events from inside the aggregate, exactly as you do today.
public sealed record OrderPlaced(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : Aggregate<OrderId>
{
    public void Place(TimeProvider clock) =>
        DomainEvents.Add(new OrderPlaced(Id, clock.GetUtcNow()));
}

// 1. Map the outbox table (OnModelCreating).
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.AddTrellisOutbox();

// 2. Add the capture interceptor where the context options are built.
options.UseNpgsql(connectionString)
       .AddTrellisInterceptors()
       .AddTrellisOutboxInterceptor();

// 3. Register the relay (plus your handlers) at the composition root.
services.AddTrellis(trellis => trellis
    .UseDomainEvents(typeof(Program).Assembly)     // handlers + IDomainEventPublisher
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseOutbox<AppDbContext>(o =>
    {
        o.PollInterval = TimeSpan.FromSeconds(2);
        o.BatchSize = 100;
        o.MaxAttempts = 10;
    }));
```

Now `await dbContext.SaveChangesAsync(ct)` commits the `OrderPlaced` row in the **same transaction** as the order; the relay re-dispatches it to `IDomainEventHandler<OrderPlaced>` after the commit.

> Prefer raw DI? Call `services.AddTrellisOutbox<AppDbContext>()` instead of the `UseOutbox` builder slot — the table and interceptor wiring (steps 1–2) are identical.

## Key Features
- **Atomic capture** — one `TrellisOutboxMessages` row per uncommitted domain event, written in the same `SaveChanges` transaction as the aggregate. State and notifications commit together or not at all.
- **Single dispatch path** — after the commit succeeds, the capture interceptor clears aggregate events in `SavedChanges`; in-pipeline dispatch sees none. This also applies to successful `FailAfterCommit` saves. Trellis post-commit bookkeeping ignores a newly canceled token.
- **At-least-once delivery** — the relay drains by `Sequence` through `IReportingDomainEventPublisher`, retaining completed-handler progress and retrying remaining handlers with exponential backoff up to `MaxAttempts`. A bookkeeping save failure instead fails the drain and does not durably advance the attempt counter.
- **Fail-fast host startup** — the relay validates the reporting publisher before starting its loop. Integration features additionally require a constructible integration publisher; domain-only hosts do not.
- **Optional DI registration probing** — providers need not expose `IServiceProviderIsService`. Without it, startup checks integration dependencies through scoped resolution instead. Collector/publisher construction failures still propagate, and the validation scope is asynchronously disposed.
- **Safe to scale out** — each drain claims its batch under an optimistic lease (`LockedBy` is a concurrency token), so concurrent relay instances never both own a row at once and a drain that outlives its lease abandons its write instead of clobbering the new owner — no leader election needed. Delivery stays at-least-once (a batch that outlives its lease can re-deliver).
- **Dead-letter & replay** — a message that exhausts `MaxAttempts` is parked for inspection; re-drive it with `IOutboxMaintenance` once you've fixed the cause.
- **Crash-safe** — a failed save rolls back the captured rows and preserves the in-memory events for retry; a crash after commit re-delivers on the next drain. Handlers must be idempotent.

## Integration events
Translate internal domain events into stable external contracts by handling the domain event and adding an `IIntegrationEvent` to the scoped collector:

```csharp
public sealed record OrderPlacedIntegrationEvent(Guid OrderId, DateTimeOffset OccurredAt)
    : IIntegrationEvent;

public sealed class OrderPlacedTranslator(IIntegrationEventCollector collector)
    : IDomainEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        collector.Add(new OrderPlacedIntegrationEvent(domainEvent.OrderId.Value, domainEvent.OccurredAt));
        return ValueTask.CompletedTask;
    }
}
```

The relay drains `IIntegrationEventCollector` after dispatching each domain event and publishes through `IIntegrationEventPublisher`. The default publisher fans out in-process; replace it with a broker adapter for cross-service delivery. Delivery is at least once, so consumers must be idempotent on business identity.

## Delivery & Serialization Notes
- The collector accepts `Add` only during the relay's `BeginTranslation()` lease; command/inactive-scope additions throw. The relay stages all drained events alongside partial handler progress, including additions made before a translator throws. Completed translators are skipped on retry, but a failed translator may produce a new row with a new ID; inbox message-ID deduplication does not replace business-identity deduplication.
- The guarantee is at-least-once **delivery**, and delivery means *every handler completed*: a handler that throws leaves the message pending, and the retry re-invokes only the failed handlers (`OutboxMessage.CompletedHandlers` tracks the rest), backing off exponentially until `MaxAttempts` parks it. Handlers must still be idempotent — a crash before the relay's bookkeeping save re-delivers to all of them.
- Events are serialized with a Trellis-owned `System.Text.Json` options instance. Value objects that carry a `[JsonConverter]` attribute round-trip, and `Maybe<T>` members are supported (present → value, absent → `null`); use a nullable transport for members that rely on a caller-registered converter.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-outbox.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
