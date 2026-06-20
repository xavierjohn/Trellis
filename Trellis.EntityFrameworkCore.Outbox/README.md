# Trellis.EntityFrameworkCore.Outbox

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Outbox.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox)

Transactional outbox for Trellis: capture aggregate domain events into an EF Core table in the **same transaction** as the aggregate change, then relay them to your Trellis domain-event handlers after the commit succeeds — durable, at-least-once, in-process dispatch.

> **AOT / Trim compatibility:** This package opts **out** of NativeAOT and trimming because it
> builds on `Trellis.EntityFrameworkCore` (EF Core relies on runtime reflection for model building,
> change tracking, and query translation) and rehydrates events by name at relay time. If your
> application targets `PublishAot=true`, do not reference this package.

## Installation
```bash
dotnet add package Trellis.EntityFrameworkCore.Outbox
```

## Why
The default in-pipeline domain-event dispatch fires *after* the transaction commits, so a crash between commit and dispatch loses the events. The outbox closes that gap: each event is written atomically with the aggregate, survives a crash, and is re-dispatched by a background relay.

This is an **outbox, not an event store**: the rows are a transient delivery buffer that may be pruned once relayed. The aggregate tables remain the source of truth.

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
- **Single dispatch path** — when the outbox is enabled the capture interceptor clears the aggregate's events inside the commit, so the in-pipeline dispatch sees none and the durable relay is the one dispatcher.
- **At-least-once delivery** — a background relay drains pending rows in `Sequence` order and re-dispatches each event through `IDomainEventPublisher`, retrying infrastructure failures up to `MaxAttempts`.
- **Crash-safe** — a failed save rolls back the captured rows and preserves the in-memory events for retry; a crash after commit re-delivers on the next drain. Handlers must be idempotent.

## Integration events
Translate internal domain events into stable external contracts by handling the domain event and adding one or more `IIntegrationEvent` instances to the scoped `IIntegrationEventCollector`:

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

After each outboxed domain event is dispatched, the relay drains the collector and publishes the integration events through `IIntegrationEventPublisher`. The default publisher fans out to in-process `IIntegrationEventHandler<TEvent>` consumers; replace it with a broker adapter when contracts must leave the process. Delivery is at least once, so consumers must be idempotent on business identity.

## Delivery & Serialization Notes
- The guarantee is at-least-once **delivery**, not handler success: per the `IDomainEventHandler<TEvent>` contract the publisher logs and swallows handler exceptions, so a failing handler does **not** retry — only infrastructure failures do. Retry-until-handlers-succeed needs a non-swallowing publish path (a planned follow-up).
- Events are serialized with a Trellis-owned `System.Text.Json` options instance. Value objects that carry a `[JsonConverter]` attribute round-trip, and `Maybe<T>` members are supported (present → value, absent → `null`); use a nullable transport for members that rely on a caller-registered converter.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-outbox.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
