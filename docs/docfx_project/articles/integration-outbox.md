---
title: Transactional Outbox Integration
package: Trellis.EntityFrameworkCore.Outbox
topics: [outbox, domain-events, reliability, efcore, messaging]
related_api_reference: [trellis-api-efcore-outbox.md, trellis-api-efcore.md, trellis-api-mediator.md]
last_verified: 2026-06-09
audience: [developer]
---
# Transactional Outbox Integration

`Trellis.EntityFrameworkCore.Outbox` makes domain-event dispatch crash-safe. It captures each uncommitted domain event into an EF Core table **in the same transaction as the aggregate change**, then a background relay re-dispatches the events to your Trellis handlers after the commit. State and notifications commit together or not at all.

## Why you need it

A plain `UseDomainEvents()` pipeline dispatches events *after* the transaction commits:

1. Command handler runs, raises `OrderPlaced`, repository stages the order.
2. `TransactionalCommandBehavior` commits the transaction.
3. `DomainEventDispatchBehavior` publishes `OrderPlaced` to its handlers.

If the process dies between (2) and (3) — a deploy, an OOM, a node eviction — the order is durably saved but `OrderPlaced` is gone. Any work the handler would have done (sending a confirmation, updating a read model, emitting an integration event) silently never happens.

The outbox closes that gap. The event is written atomically with the order, so it survives the crash and is relayed on the next poll.

## The three wiring points

The outbox has a capture half (on the `DbContext`) and a relay half (in the service container). Both are required.

```csharp
public sealed class AppDbContext : DbContext
{
    // 1. Map the TrellisOutboxMessages table.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddTrellisOutbox();
    }
}

// 2. Add the capture interceptor where the context options are built.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
           .AddTrellisInterceptors()
           .AddTrellisOutboxInterceptor());

// 3. Register the relay alongside your domain-event handlers.
builder.Services.AddTrellis(trellis => trellis
    .UseDomainEvents(typeof(Program).Assembly)     // handlers + IDomainEventPublisher
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseOutbox<AppDbContext>(o =>
    {
        o.PollInterval = TimeSpan.FromSeconds(2);
        o.BatchSize = 100;
        o.MaxAttempts = 10;
    }));
```

Raise events from inside the aggregate exactly as you do today:

```csharp
public sealed class Order : Aggregate<OrderId>
{
    public void Place(TimeProvider clock) =>
        DomainEvents.Add(new OrderPlaced(Id, clock.GetUtcNow()));
}

public sealed record OrderPlaced(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
```

> Prefer raw DI over the builder? Call `services.AddTrellisOutbox<AppDbContext>()` directly instead of `.UseOutbox<AppDbContext>()`. The table and interceptor wiring (steps 1–2) are identical; the `UseOutbox` slot simply also fails fast if you configure the outbox twice.

## How it works

The capture is a `SaveChangesInterceptor` with a deliberate three-phase lifecycle so a failed save never loses or double-captures events:

1. **`SavingChanges`** — scan the change tracker for aggregates with uncommitted events, serialize each event, and add one `OutboxMessage` row per event to the **current** `SaveChanges`. The rows enrol in the same transaction as the aggregate. The aggregate's in-memory events are *not* cleared yet.
2. **`SavedChanges`** (commit succeeded) — call each aggregate's `AcceptChanges()` to clear its events. Because this happens only after a successful commit, the in-pipeline `DomainEventDispatchBehavior` that runs next sees an empty event list and dispatches nothing — the relay is now the single dispatcher.
3. **`SaveChangesFailed`** — detach the outbox rows the interceptor staged. The aggregate keeps its events, so a retry re-captures cleanly without leaving orphaned rows.

The relay (`OutboxRelay<TContext>`) is a hosted `BackgroundService`. Each poll it opens a bookkeeping scope, reads a batch of pending rows ordered by the monotonic `Sequence` column, rehydrates each event from its stored type name, and publishes it through `IDomainEventPublisher` (the same fan-out the pipeline would use) **in its own per-message scope** — so a handler that injects `TContext` gets a fresh context, not the relay's bookkeeping one, and its tracked changes never ride the relay's save. It then marks each row processed and saves the batch.

## Delivery semantics

The guarantee is **at-least-once delivery**, not handler success — an important distinction:

- A message is marked processed once it has been handed to the publisher. Per the `IDomainEventHandler<TEvent>` contract, the publisher logs and swallows non-cancellation handler exceptions, so a **failing handler does not retry**. This matches the in-pipeline dispatch behavior exactly.
- Only infrastructure failures — an unresolvable event type, a deserialization error, or the relay's own save failing — leave a message pending. Those retry on later polls up to `MaxAttempts`, after which the message is *parked*: left unprocessed but skipped by the scan so it does not block later messages. `LastError` keeps the most recent failure.
- A crash between dispatch and the relay's bookkeeping save re-delivers the message. **Make your handlers idempotent.** The `OutboxMessage.Id` (a UUIDv7) is a stable per-message key you can use for consumer-side de-duplication.

Retry-until-handlers-succeed would need a non-swallowing publish path; that is a planned follow-up. Until then, a handler that must not silently drop work should surface failure through its own durable mechanism (its own outbox row, a dead-letter table, etc.).

## Domain events vs. integration events

A **domain event** is internal to your bounded context — raised by an aggregate, dispatched in-process, free to speak the domain's ubiquitous language. An **integration event** is the stable, versioned contract you publish to the outside world. Relaying raw domain events externally couples other systems to your internal model; the outbox lets you keep them separate and publish a deliberate contract instead.

Model the contract as an `IIntegrationEvent` (primitive/nullable members, no internal value objects), then **translate** from the domain event with an ordinary domain-event handler that adds to the scoped `IIntegrationEventCollector`:

```csharp
// The external contract.
public sealed record OrderPlacedIntegrationEvent(Guid OrderId, string CustomerEmail, decimal Total, DateTimeOffset OccurredAt)
    : IIntegrationEvent;

// The translator: a domain-event handler that emits the contract.
public sealed class OrderPlacedTranslator(IIntegrationEventCollector collector) : IDomainEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        collector.Add(new OrderPlacedIntegrationEvent(
            domainEvent.OrderId.Value, domainEvent.CustomerEmail.Value, domainEvent.Total.Amount, domainEvent.OccurredAt));
        return ValueTask.CompletedTask;
    }
}

// Wire the consumer side (or swap the publisher for a broker adapter).
builder.Services.AddTrellis(trellis => trellis
    .UseDomainEvents(typeof(Program).Assembly)        // translators are domain-event handlers
    .UseIntegrationEvents(typeof(Program).Assembly)   // publisher + collector + in-process consumers
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseOutbox<AppDbContext>());
```

**How delivery flows.** When the relay re-dispatches `OrderPlaced`, the translator runs and adds the integration event to the collector. The relay drains the collector and stages the integration event as a new `OutboxMessageKind.Integration` row — in the same save that marks the domain row processed — and a later drain publishes it through `IIntegrationEventPublisher`. So an integration event is emitted **only after** its source domain event is durably committed and dispatched, never for state that rolled back.

The default `IIntegrationEventPublisher` fans out to in-process `IIntegrationEventHandler<T>` registrations — ideal for a modular monolith and for tests. To deliver to other services, replace that one registration with a message-broker adapter; aggregates, translators, and the outbox are unchanged. Delivery is at-least-once and a retried domain event re-runs its translator, so a consumer may see the same integration event more than once (with a different `OutboxMessage.Id` each time) — **dedupe on business identity, not on the message id.**

## Persist-on-failure (`FailAfterCommit`) events

`TransactionalCommandBehavior` commits a `Result.FailAfterCommit` outcome (persist-on-failure), but `DomainEventDispatchBehavior` does **not** dispatch domain events for any failed result — by design those events are discarded, not a durable buffer.

The capture interceptor cannot see the command result, so with the outbox enabled it captures events from *every* commit, including persist-on-failure commits, and the relay delivers them. **Enabling the outbox therefore changes this behavior**: events raised on a persist-on-failure path become durable and dispatched. If you rely on the base suppression, do not raise domain events on persist-on-failure paths — return a success result for the events you want delivered, and model post-failure side effects explicitly (a follow-up command, or a dedicated outbox row).

## Serialization constraints

Events are serialized with the default `System.Text.Json` options:

- **Round-trips:** value objects that carry a `[JsonConverter]` attribute (the Trellis scalar and composite primitives) — the converter travels with the type.
- **Does not round-trip:** `Maybe<T>` and converters registered only through `JsonSerializerOptions` factories. Use a **nullable transport** in the event (`string?`, `decimal?`, …) rather than `Maybe<string>`. This is the same guidance the TRLS020 analyzer gives for event and DTO contracts.

## Operating the outbox

- **Monitor and replay dead-lettered messages.** Alert on the `OutboxRelay.MessageParked` error log (and on rows where `ProcessedAt IS NULL AND Attempts >= MaxAttempts`); those events exhausted their retries. Once you have fixed the cause, replay them by resolving `IOutboxMaintenance` (`GetDeadLetteredAsync`, `ReplayAsync`, `ReplayAllAsync`). Transient retries log at Warning (`OutboxRelay.RelayAttemptFailed`) and self-heal with an exponential backoff (`RetryBackoff` doubling up to `MaxRetryBackoff`, with per-message jitter), so they should not page on their own.
- **Prune processed rows.** Rows with a non-null `ProcessedAt` are a spent delivery buffer — a periodic job can delete old ones with no loss of source-of-truth state. The aggregate tables remain authoritative.
- **Keep the producing assemblies loaded.** The relay resolves each event by its assembly-qualified type name, so the worker process must reference the assemblies that declare your events.
- **Run as many relay instances as you need.** Each drain atomically claims a batch with a lease (`LockedBy` + `LockedUntil`), and `LockedBy` is an optimistic concurrency token, so concurrent instances never publish the same row twice and a drain that outlived its lease abandons its bookkeeping write — logging `OutboxRelay.LeaseLost` — rather than clobber the instance that reclaimed the row. No leader election or distributed lock is needed. Delivery is still at-least-once — a crash between publish and the relay's `SaveChanges`, or a batch that outlives its lease, can re-deliver — so keep handlers idempotent. Set `LeaseDuration` comfortably above the worst-case batch publish time and keep node clocks reasonably in sync, since the lease is compared against each relay's wall clock.
- **One outbox per composition.** `UseOutbox<TContext>()` throws if called twice. Multiple relays in one process are not supported by the builder slot today.

## Outbox vs. event sourcing

The outbox is **not** event sourcing. The `TrellisOutboxMessages` rows are a transient *messaging buffer*: they exist only until they are relayed and may be pruned afterward. Your aggregate tables remain the single source of truth, and you never rebuild state by replaying outbox rows. Event sourcing, by contrast, makes the event log itself the source of truth. Reach for the outbox when you want reliable delivery of notifications about state changes; reach for event sourcing when the events *are* the state.

## Related guides

- [Entity Framework Core Integration](integration-ef.md) — repositories, unit of work, and the interceptors the outbox builds on.
- [Mediator Pipeline](integration-mediator.md) — domain-event dispatch, the `IDomainEventPublisher` seam, and pipeline ordering.
