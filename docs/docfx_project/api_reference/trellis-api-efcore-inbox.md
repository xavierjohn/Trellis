---
package: Trellis.EntityFrameworkCore.Inbox
namespaces: [Trellis.EntityFrameworkCore]
types: [InboxMessage, InboxOptions, IntegrationEnvelope, InboxDispatchOutcome, IInboxStore, IInboxDispatcher, InboxServiceCollectionExtensions, InboxModelBuilderExtensions, IConsumerCheckpointStore, ConsumerCheckpoint, ConsumerCheckpointConfiguration, CheckpointServiceCollectionExtensions, CheckpointModelBuilderExtensions]
version: v1
last_verified: 2026-06-21
audience: [llm]
---
# Trellis.EntityFrameworkCore.Inbox

**Package:** `Trellis.EntityFrameworkCore.Inbox`  
**Namespace:** `Trellis.EntityFrameworkCore`  
**Purpose:** Transactional inbox — the consume-side complement to the outbox. Make integration-event consumption idempotent by deduplicating redeliveries on `(ConsumerId, MessageId)` inside the consumer's unit of work, so a handler's local side effects commit exactly once even though the transport delivers at-least-once.

This package composes `Trellis.EntityFrameworkCore` (the `DbContext`, transaction, and table mapping) with `Trellis.Mediator` (the `IIntegrationEvent` / `IIntegrationEventHandler<TEvent>` contracts it fans out to). It opts out of AOT/trim, exactly like `Trellis.EntityFrameworkCore`.

> [!NOTE]
> The **contracts** are store-agnostic and live outside the EF Core packages. The store SPIs — `IUnitOfWork`, `IInboxStore`, `IConsumerCheckpointStore`, and `InboxRecord` — are in `Trellis.Persistence.Abstractions` (namespace `Trellis`); the consume-side dispatch contracts — `IInboxDispatcher`, `IntegrationEnvelope`, `InboxDispatchOutcome` — are in `Trellis.Mediator`. This page documents the EF Core **adapter** (`EfInboxStore`, `EfConsumerCheckpointStore`, `InboxDispatcher<TContext>`, `InboxMessage`, and the registration extensions) that implements them.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#trellis-cross-package-cookbook) — recipes using this package.

## Use this file when

- You receive integration events from a broker (or an in-process channel) and must process each one **effectively once**, even though the transport can redeliver after a lock-renewal timeout, an offset replay, or a producer re-publish.
- You are wiring `AddTrellisInbox`, `modelBuilder.AddTrellisInbox()`, or the `UseInbox<TContext>()` builder slot.
- You are reasoning about the inbox's idempotency guarantee, its transaction boundary, or what `(ConsumerId, MessageId)` must be.

## Patterns Index

| Goal | Use this | See |
|---|---|---|
| Make a consumer idempotent against redeliveries | `services.AddTrellisInbox<TContext>(o => o.ConsumerId = "...")` or `trellis.UseInbox<TContext>(...)` + `modelBuilder.AddTrellisInbox()` | [Wiring: two required calls](#wiring-two-required-calls), [InboxServiceCollectionExtensions](#inboxservicecollectionextensions) |
| Hand a received message to the inbox | `IInboxDispatcher.DispatchAsync(envelope, ct)` from your transport adapter — returns `Processed` or `SkippedDuplicate` | [IInboxDispatcher](#iinboxdispatcher), [InboxDispatchOutcome](#inboxdispatchoutcome) |
| Drive a gap-free pull / anti-join consumer | `IInboxStore.FilterUnprocessedAsync(consumerId, ids, ct)` to skip already-processed feed rows | [IInboxStore](#iinboxstore) |
| Resume a pull consumer without rescanning the whole feed | `services.AddTrellisConsumerCheckpointStore<TContext>()` + `modelBuilder.AddTrellisConsumerCheckpoints()`; `IConsumerCheckpointStore.GetAsync` / `SetAsync` | [Pull-consumer checkpoint](#pull-consumer-checkpoint-resume-cursor) |
| Map the deduplication table | `modelBuilder.AddTrellisInbox()` in `OnModelCreating` | [InboxModelBuilderExtensions](#inboxmodelbuilderextensions), [InboxMessage](#inboxmessage) |
| Identify this subscriber for per-consumer dedup | `InboxOptions.ConsumerId` (required) | [InboxOptions](#inboxoptions) |
| Supply the same guarantee over a non-EF store | Implement `IInboxStore` | [IInboxStore](#iinboxstore) |
| Understand why a failed handler reprocesses | The dispatcher is **non-swallowing**: a handler throw rolls back and rethrows, so the transport redelivers | [Processing semantics](#processing-semantics) |
| Connect a producer's outbox to a consumer's inbox | Carry the outbox `OutboxMessage.Id` verbatim as the envelope `MessageId` | [Relationship to the outbox](#relationship-to-the-outbox) |

## Common traps

- **`AddTrellisInbox<TContext>()` only registers the service half.** Without `modelBuilder.AddTrellisInbox()` the `TrellisInboxMessages` table is unmapped, and without a transport adapter calling `IInboxDispatcher.DispatchAsync(...)` nothing is ever deduplicated. See [Wiring: two required calls](#wiring-two-required-calls).
- **The `MessageId` must be stable across redeliveries.** Dedup keys on `(ConsumerId, MessageId)`. If the transport assigns a *new* id on every delivery (instead of carrying the producer's `OutboxMessage.Id` verbatim), every redelivery looks new and is reprocessed. The envelope's lineage fields (`MessageSource`, `CausationId`, `CorrelationId`) are observability only and never participate in dedup.
- **`ConsumerId` must be stable across deploys.** It is part of the dedup key, so renaming it resets dedup history and the consumer reprocesses everything still inside the transport's redelivery window. Each independent subscriber/consumer-group uses its own stable `ConsumerId`.
- **The guarantee is local-side-effects-only.** The dedup row and the handlers' writes through the injected `TContext` commit in one `SaveChanges`. Effects that are *not* part of that save — sending an email, calling a downstream API, writing through a different `DbContext`/connection — are not covered and need their own idempotency. A handler that writes through a second context escapes the dedup unit of work.
- **Handlers run before the save and must propagate failures.** Unlike the default `IIntegrationEventPublisher` (which logs and swallows handler exceptions), the inbox dispatcher is non-swallowing: a handler throw aborts the unit of work before anything is saved — no dedup row, no side effects — and propagates so the transport redelivers. Make handlers safe to re-run, and do not call `SaveChanges` inside a handler (it would break the single-save atomicity).
- **An absent `ConsumerId` fails fast at registration.** `AddTrellisInbox` calls `InboxOptions.Validate()`, which throws `InvalidOperationException` if `ConsumerId` is blank, so the misconfiguration surfaces at startup rather than on the first message.
- **The checkpoint is not a dedup substitute.** `IConsumerCheckpointStore` is a performance resume cursor, not the correctness boundary — a high-water cursor can skip a row committed out of order. Always pair it with an overlap window **and** `FilterUnprocessedAsync`; never advance it past rows that aren't known processed. See [Pull-consumer checkpoint](#pull-consumer-checkpoint-resume-cursor).

## How the inbox works

The inbox turns at-least-once **delivery** into effectively-once **processing** with one short unit of work per message:

1. **Receive.** An application-owned transport adapter (a broker consumer, or an in-process channel) deserializes the message into an [IntegrationEnvelope](#integrationenvelope) — a stable `MessageId` plus the `IIntegrationEvent` — and calls `IInboxDispatcher.DispatchAsync(envelope, ct)`.
2. **Open a unit of work.** The dispatcher creates a DI scope and resolves the consumer's `TContext` and the scoped `IInboxStore`.
3. **Deduplicate.** The dispatcher maps the envelope to an `InboxRecord` and calls `IInboxStore.TryRecordAsync(consumerId, record, ct)`, which checks for an existing `(ConsumerId, MessageId)` row. If one exists the dispatcher returns immediately — a no-op; nothing was staged. Otherwise it stages a new `InboxMessage` row (without saving).
4. **Fan out (non-swallowing).** The dispatcher resolves every `IIntegrationEventHandler<TConcrete>` for the runtime event type from the *same* scope and awaits each. Because the handlers share the scope, a handler that injects `TContext` writes through the very context that holds the staged dedup row. Handler exceptions **propagate** — and because nothing has been saved yet, a throw leaves no dedup row and no side effects, so the transport redelivers.
5. **Commit.** One `SaveChangesAsync` persists the dedup row and the handler writes together, atomically, under EF Core's implicit transaction. No user-initiated transaction is opened, so the inbox composes with a retrying execution strategy (`EnableRetryOnFailure`) like the rest of Trellis. A concurrent dispatch that recorded the same `(ConsumerId, MessageId)` first makes this save fail with a duplicate-key `DbUpdateException`; the dispatcher then re-checks, in a fresh scope, whether the dedup row was actually committed — if so the message is already processed and the failure is swallowed — this call commits nothing, the handlers' staged writes having rolled back with the save; if not, the duplicate came from a handler's own unique write and propagates so the transport redelivers.

## Wiring: two required calls

```csharp
// 1. Map the inbox dedup table.
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.AddTrellisInbox();

// 2. Register the dispatcher + store + options, and the handlers that consume the messages.
services.AddTrellis(trellis => trellis
    .UseIntegrationEvents(typeof(Program).Assembly)   // the IIntegrationEventHandler<T> consumers
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseInbox<AppDbContext>(o => o.ConsumerId = "orders-service"));
```

Then a transport adapter (app-owned) feeds received messages in:

```csharp
public sealed class BrokerConsumer(IInboxDispatcher inbox)
{
    public Task OnMessageAsync(TransportMessage raw, CancellationToken ct)
    {
        var envelope = new IntegrationEnvelope(raw.MessageId, Deserialize(raw))
        {
            MessageSource = raw.SourceService,
            CorrelationId = raw.CorrelationId,
        };
        return inbox.DispatchAsync(envelope, ct);
    }
}
```

The consumer's `DbContext` is registered separately by the app (as usual); the inbox does not register it. Register the integration-event handlers in the same container — `UseIntegrationEvents(...)` / `AddIntegrationEventHandler<TEvent, THandler>()` resolve them as the `IEnumerable<IIntegrationEventHandler<TEvent>>` the dispatcher fans out to.

## InboxServiceCollectionExtensions

Static class. Registers the inbox for a `DbContext`.

```csharp
public static IServiceCollection AddTrellisInbox<TContext>(
    this IServiceCollection services,
    Action<InboxOptions> configure)
    where TContext : DbContext;
```

- Runs `configure`, calls `InboxOptions.Validate()` (throws if `ConsumerId` is blank), and registers the validated `InboxOptions` as a singleton, `TimeProvider.System` (via `TryAdd`), the scoped `IInboxStore` → `EfInboxStore<TContext>`, and the singleton `IInboxDispatcher` → `InboxDispatcher<TContext>`.
- `configure` is **required** (not optional) because `ConsumerId` has no safe default.
- The dispatcher is a singleton that opens a fresh DI scope per `DispatchAsync`; the store and handlers are scoped, so each dispatch gets its own `TContext` and transaction.

This is the service-collection half only. Pair with `AddTrellisInbox(ModelBuilder)` and a transport adapter that calls `IInboxDispatcher`.

## InboxModelBuilderExtensions

Static class. EF Core model hook.

```csharp
public static ModelBuilder AddTrellisInbox(this ModelBuilder modelBuilder);
```

Applies `InboxMessageConfiguration`, mapping the `TrellisInboxMessages` table with the composite `(ConsumerId, MessageId)` primary key and an index on `ProcessedAt`. Call it from `OnModelCreating`. The table needs a migration like any other entity.

## UseInbox builder slot

`TrellisServiceBuilder.UseInbox<TContext>(Action<InboxOptions> configure)` (in `Trellis.ServiceDefaults`) is the opinionated entry point. It wraps `AddTrellisInbox<TContext>()` and:

- Fails fast with `InvalidOperationException` if called more than once — one inbox per composition.
- Is order-independent versus `UseEntityFrameworkUnitOfWork<TContext>()`: the dispatcher is an inbound seam, not a Mediator behavior, so the canonical pipeline order is identical whether `UseInbox` is called before or after the unit-of-work slot.
- Carries `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` because the inbox builds on the non-AOT `Trellis.EntityFrameworkCore`.

The slot owns only the service registration; the table mapping is still wired on the `DbContext` with `modelBuilder.AddTrellisInbox()`, mirroring how `UseOutbox` pairs with `AddTrellisOutbox`.

## IInboxDispatcher

The inbound entry point. A transport adapter builds an envelope and calls it; the dispatcher deduplicates and fans out.

```csharp
public interface IInboxDispatcher
{
    Task<InboxDispatchOutcome> DispatchAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken = default);
}
```

- A first delivery runs the handlers and commits the dedup row, returning `InboxDispatchOutcome.Processed`. A redelivery of the same `(ConsumerId, MessageId)` returns `InboxDispatchOutcome.SkippedDuplicate` — caught by the existence check on the fast path (no handler runs), or by the duplicate-key guard when a concurrent dispatch won the race (the handlers ran but rolled back). Either way this call commits nothing; both outcomes mean the message is durably accounted for, so a pull consumer can advance its checkpoint on either.
- A handler exception is **not** swallowed: it rolls the transaction back and propagates out of `DispatchAsync`, so the adapter can let the transport redeliver.

## InboxDispatchOutcome

The result of `DispatchAsync`, so a caller can branch on whether the message was newly processed without re-querying.

```csharp
public enum InboxDispatchOutcome
{
    Processed,
    SkippedDuplicate,
}
```

| Member | Meaning |
|---|---|
| `Processed` | The message was new: its handlers ran and their side effects committed atomically with the dedup record in this call. |
| `SkippedDuplicate` | The `(ConsumerId, MessageId)` pair was already processed, so this call committed nothing. Usually caught on the fast path before any handler runs; if a concurrent dispatch won the race, the handlers ran but rolled back with the duplicate-key save. |

Both outcomes mean the message is durably accounted for; the distinction is for metrics, logging, and a pull consumer's checkpoint / anti-join bookkeeping. A failure (handler throw, infrastructure error) does not return an outcome — it propagates so the transport redelivers.

## IInboxStore

Service-provider interface (SPI) for the dedup record, so a non-EF store can supply the same guarantee. `EfInboxStore<TContext>` is the shipped EF implementation.

```csharp
public interface IInboxStore
{
    Task<bool> TryRecordAsync(string consumerId, InboxRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> FilterUnprocessedAsync(
        string consumerId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);
}
```

`TryRecordAsync` records the message as processed by `consumerId` **inside the caller's current unit of work**, so the dedup record and the handler side effects commit together. Returns `true` if newly recorded, or `false` if the `(ConsumerId, MessageId)` pair was already processed — a duplicate the dispatcher skips. `EfInboxStore` does an existence check for the fast path and stages an `InboxMessage` for the slow path; the composite primary key is the authoritative guard under concurrency.

`FilterUnprocessedAsync` returns the subset of `messageIds` that `consumerId` has **not** yet processed (those without a dedup row), preserving the input order. It powers the gap-free **inbox-as-cursor / anti-join** pull model: read a window of the source feed and dispatch every row whose `MessageId` this query returns, rather than tracking a fragile high-water cursor that can skip a row committed out of sequence order. It is an optimization, not the correctness boundary — a row may be processed by another worker between this query and `DispatchAsync`, which still deduplicates — and it is a pure read that stages nothing. `EfInboxStore` runs it as a single anti-join query (`AsNoTracking`); it throws `ArgumentException` if `consumerId` is blank.

## Pull-consumer checkpoint (resume cursor)

`IConsumerCheckpointStore` is an optional, durable **resume cursor** for a pull consumer — it remembers per-`ConsumerId` where in the source feed the consumer last advanced to, so it resumes there instead of rescanning the whole log on every poll or restart.

```csharp
public interface IConsumerCheckpointStore
{
    Task<Maybe<string>> GetAsync(string consumerId, CancellationToken cancellationToken);
    Task SetAsync(string consumerId, string position, CancellationToken cancellationToken);
}
```

**Performance, not correctness.** The checkpoint narrows the scan window; it is **not** the deduplication boundary. A high-water cursor can skip a row that was assigned a low position but committed *late* — after the cursor advanced past it — which a cursor alone can never recover. Correctness stays with `FilterUnprocessedAsync` (the anti-join) plus the dedup row. The safe pattern:

1. `GetAsync(consumerId)` → the resume position (`Maybe.None` the first time → start from the feed's beginning).
2. Scan a window that **overlaps** the checkpoint — re-read a visibility-lag margin *behind* it, so a late-committed row is still inside the window.
3. `FilterUnprocessedAsync(consumerId, window.Ids, ct)` → the not-yet-processed ids; `DispatchAsync` each.
4. `SetAsync(consumerId, position)` → advance the cursor, only to a position whose predecessors are all known processed.

```csharp
var resume = await checkpoints.GetAsync(consumerId, ct);              // Maybe<string>
var since = resume.GetValueOrDefault(FeedStart);
var window = await feed.ReadFrom(Rewind(since, overlapMargin), ct);   // overlap behind the checkpoint
var todo = await inbox.FilterUnprocessedAsync(consumerId, window.Ids, ct);
foreach (var id in todo)
    await dispatcher.DispatchAsync(window.Envelope(id), ct);
await checkpoints.SetAsync(consumerId, window.HighWaterMark, ct);     // advance the cursor
```

- **Opaque `position`.** Trellis does not interpret it — encode whatever cursor the feed uses (a sequence number, a UUIDv7 high-water mark, a timestamp, a composite token) as a string. The store round-trips it verbatim.
- **Wiring (two calls).** `services.AddTrellisConsumerCheckpointStore<TContext>()` registers the store; `modelBuilder.AddTrellisConsumerCheckpoints()` maps the `TrellisConsumerCheckpoints` table (PK `ConsumerId`, width matching the inbox key). It is a **leaf store** — no `TrellisServiceBuilder` slot, like `AddInMemoryIdempotencyStore`.
- **Durable + isolated.** `EfConsumerCheckpointStore<TContext>` reads and upserts on its own fresh DI scope and `SaveChanges`, so an advance is persisted on return and never entangles the caller's unit of work. One logical advancer per `ConsumerId` is assumed (the usual pull-consumer shape); the cursor is not a coordination primitive.
- `GetAsync` / `SetAsync` throw `ArgumentException` for a blank `consumerId`; `SetAsync` also for a blank `position`.

## IntegrationEnvelope

The consume-side envelope handed to the dispatcher.

```csharp
public sealed record IntegrationEnvelope(Guid MessageId, IIntegrationEvent Event)
{
    public string? MessageSource { get; init; }
    public Guid? CausationId { get; init; }
    public string? CorrelationId { get; init; }
}
```

| Member | Type | Notes |
|---|---|---|
| `MessageId` | `Guid` | **Load-bearing.** The stable dedup id — the producer's outbox `OutboxMessage.Id` (a UUIDv7) carried verbatim by the transport. Redeliveries carry the same value. |
| `Event` | `IIntegrationEvent` | **Load-bearing.** The deserialized integration event dispatched to its handlers. |
| `MessageSource` | `string?` | Optional. The producing service / bounded context, for observability. |
| `CausationId` | `Guid?` | Optional lineage: the id of the message that directly caused this one. |
| `CorrelationId` | `string?` | Optional lineage: the workflow / conversation id shared across a business transaction. |

Only `MessageId` and `Event` participate in processing; the lineage members are recorded for audit/observability and never affect dedup.

## InboxMessage

A persisted record that a `(ConsumerId, MessageId)` message has been processed — one row per message per consumer. Its existence *is* the dedup guarantee. Read-only to application code (private constructors, private setters, internal `Create`).

| Member | Type | Notes |
|---|---|---|
| `ConsumerId` | `string` | Part of the primary key. The stable subscriber identifier. Max length 256. |
| `MessageId` | `Guid` | Part of the primary key. The message's stable id (the producer's outbox id). |
| `MessageSource` | `string?` | The producing service / bounded context, if the envelope supplied one. |
| `EventType` | `string` | Assembly-qualified name of the integration event type, recorded for audit. |
| `OccurredAt` | `DateTimeOffset` | Copied from the event's `OccurredAt`. |
| `ProcessedAt` | `DateTimeOffset` | When this consumer processed the message. Indexed to support pruning. |
| `CausationId` | `Guid?` | Optional lineage copied from the envelope. |
| `CorrelationId` | `string?` | Optional lineage copied from the envelope. |

`InboxMessageConfiguration` maps the table `TrellisInboxMessages`, the composite `(ConsumerId, MessageId)` primary key (the uniqueness guard that makes a concurrent duplicate fail at `SaveChanges`), and an index on `ProcessedAt`.

`InboxMessage` is transient infrastructure, not a domain aggregate. Rows may be pruned once they are older than the transport's maximum redelivery window — delete sooner and a late redelivery would be reprocessed.

## InboxOptions

Configuration for the inbox.

| Property | Type | Default | Notes |
|---|---|---|---|
| `ConsumerId` | `string` | `""` (must be set) | **Required.** A stable identifier for this subscriber / consumer-group; part of the dedup key, so two services consuming the same message each get one effective processing. Keep it stable across deploys — renaming it resets dedup history. `Validate()` throws `InvalidOperationException` if it is blank. |

## Processing semantics

The guarantee is effectively-once **processing of local side effects**, layered on the transport's at-least-once **delivery**:

- A first delivery runs the handlers and commits the `(ConsumerId, MessageId)` dedup row in the same `SaveChanges`. A redelivery finds the row and is skipped.
- **Failure leaves nothing persisted and redelivers.** A handler throw (or any infrastructure failure) aborts the unit of work before anything is saved — no dedup row, no side effects — and propagates out of `DispatchAsync`, so the transport's normal retry redelivers the message and it is reprocessed from scratch. The inbox does not itself retry or dead-letter; that is the transport's job.
- **Concurrency is resolved by the primary key.** When two deliveries of the same message race, both may pass the existence check, but only one `SaveChanges` inserts the `(ConsumerId, MessageId)` row; the other fails with a duplicate-key `DbUpdateException`. The dispatcher then re-checks in a fresh scope whether the dedup row was committed: present → the message is already processed, so the failure is swallowed and this call commits nothing; absent → the duplicate came from a handler's own unique write and propagates. The losing delivery's staged writes are discarded with the failed save, so exactly one delivery applies the side effects.
- **Local only.** Only effects written through the injected `TContext` are covered. External calls (emails, downstream APIs) and writes through a different connection are outside the save and need their own idempotency.

## Relationship to the outbox

The outbox is the **produce** side (atomically capture and reliably publish), the inbox is the **consume** side (idempotently receive). They meet at the message id: the producer's `OutboxMessage.Id` (a UUIDv7) is carried verbatim by the transport and becomes the inbox `MessageId`. Because the outbox is at-least-once and a retried domain event re-runs its translator, a consumer can see the same logical message more than once — the inbox collapses those to one effective processing per `ConsumerId`.

The transport between them (a broker, a log, an HTTP push) is **application-owned**: the inbox defines the `IInboxDispatcher` seam an adapter calls, and `IInboxStore` so a non-EF store can back the guarantee, but it does not ship a broker adapter. In a modular monolith the same in-process path the outbox already fans out to needs no inbox; reach for the inbox when messages cross a process boundary that can redeliver.
