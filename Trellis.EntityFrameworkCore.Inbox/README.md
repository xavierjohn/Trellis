# Trellis.EntityFrameworkCore.Inbox

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Inbox.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Inbox)

The transactional **inbox** for Trellis — the consume-side complement to the [transactional outbox](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox). It makes integration-event consumption **idempotent**: redeliveries of the same message (a broker lock-renewal timeout, a log offset replay, an outbox re-publish) are deduplicated by message id **within the consumer's unit of work**, so a handler's local side effects commit exactly once.

> **AOT / Trim compatibility:** This package opts **out** of NativeAOT and trimming because it
> builds on `Trellis.EntityFrameworkCore` (EF Core relies on runtime reflection for model building,
> change tracking, and query translation) and resolves handlers by event type at dispatch time. If
> your application targets `PublishAot=true`, do not reference this package.

## Installation
```bash
dotnet add package Trellis.EntityFrameworkCore.Inbox
```

## Why
Every durable transport delivers at least once, so a consumer will see the same message twice and must not apply it twice. A "have we handled this id?" check is only correct if the check and the work commit atomically — otherwise a crash in between reprocesses or drops. The inbox records that a message was processed **in the same transaction** as the handler's side effects, so the proof and the work are inseparable.

This is an **idempotency buffer, not an event store**: the rows are a transient dedup ledger that may be pruned once they are older than the transport's redelivery window.

## Quick Example
Two wiring points — the dedup table and the dispatcher — plus a transport adapter you own.

```csharp
// 1. Map the TrellisInboxMessages dedup table (OnModelCreating).
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.AddTrellisInbox();

// 2. Register the dispatcher, store, options, and the handlers that consume the events.
services.AddTrellis(trellis => trellis
    .UseIntegrationEvents(typeof(Program).Assembly)   // your IIntegrationEventHandler<T> consumers
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseInbox<AppDbContext>(o => o.ConsumerId = "orders-service"));

// 3. Your transport adapter hands each received message to the inbox.
public sealed class OrdersBrokerConsumer(IInboxDispatcher inbox)
{
    public Task OnMessageAsync(TransportMessage raw, CancellationToken ct) =>
        inbox.DispatchAsync(new IntegrationEnvelope(raw.MessageId, Deserialize(raw)), ct);
}
```

The dispatcher deduplicates on `(ConsumerId, MessageId)` so the event's handlers' side effects commit exactly once, together with the dedup record. A duplicate delivery commits nothing.

> Prefer raw DI? Call `services.AddTrellisInbox<AppDbContext>(o => o.ConsumerId = "orders-service")` instead of the `UseInbox` builder slot — the table wiring (step 1) is identical.

## Key Features
- **Atomic dedup** — the `(ConsumerId, MessageId)` row and the handler side effects commit in one `TContext` transaction. Either both land or neither does.
- **Effectively-once processing** — at-least-once transport delivery becomes exactly-once application of local side effects, per consumer.
- **Non-swallowing by design** — a handler throw rolls the transaction back and rethrows, so nothing is marked processed and the transport redelivers. (The default integration-event publisher swallows handler errors; the inbox must not.)
- **Concurrency-safe** — the composite primary key is the guard: when two deliveries race, exactly one inserts the dedup row and the other rolls back as a duplicate.
- **Store-agnostic seam** — `IInboxStore` is an SPI, so the same idempotency guarantee can be backed by a non-EF store. `EfInboxStore<TContext>` is the shipped implementation.
- **Stable dedup key** — use the producer's outbox `OutboxMessage.Id` (a UUIDv7) carried verbatim by the transport as the `MessageId`.
- **Pull-consumer ready** — `DispatchAsync` returns `Processed` vs `SkippedDuplicate`, and `IInboxStore.FilterUnprocessedAsync(consumerId, ids, ct)` returns a window's not-yet-processed ids for the gap-free inbox-as-cursor / anti-join model — no fragile high-water cursor.
- **Resume cursor (optional)** — `IConsumerCheckpointStore` (`AddTrellisConsumerCheckpointStore<TContext>()` + `AddTrellisConsumerCheckpoints()`) durably remembers a pull consumer's position so it resumes instead of rescanning the whole feed. A performance optimization, not a dedup substitute — pair it with an overlap window and `FilterUnprocessedAsync` for correctness.

## Delivery & idempotency notes
- The guarantee covers **local transactional side effects only** — writes through the injected `DbContext`. External calls (emails, downstream APIs) happen outside the transaction and still need their own idempotency (an idempotency key, or push them back out through an outbox).
- `ConsumerId` is part of the dedup key. Keep it stable across deploys; renaming it resets dedup history.
- `TrellisInboxMessages` rows can be pruned once they are older than the transport's maximum redelivery window (the `ProcessedAt` column is indexed for this).

## Inbox + outbox
The outbox makes a producer's publish reliable (at-least-once); the inbox makes a consumer's receive idempotent (effectively-once). They meet at the message id: the producer's `OutboxMessage.Id` carried across the wire becomes the inbox `MessageId`.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-inbox.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
