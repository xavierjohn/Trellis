---
package: Trellis.EntityFrameworkCore.Inbox
namespaces: [Trellis.EntityFrameworkCore]
types: [InboxMessage, InboxMessageConfiguration, InboxOptions, IntegrationEnvelope, InboxDispatchOutcome, IInboxStore, IInboxDispatcher, InboxServiceCollectionExtensions, InboxModelBuilderExtensions, IConsumerCheckpointStore, ConsumerCheckpoint, ConsumerCheckpointConfiguration, CheckpointServiceCollectionExtensions, CheckpointModelBuilderExtensions]
version: v1
last_verified: 2026-10-14
audience: [llm]
agent_usage: onDemand
agent_description: "Open when wiring inbox dedup/transactions, inspecting retained processing evidence, or defining optional consumer/source-lag requirements; no inbox health checks are shipped."
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
- You are defining metadata inspection, safe dedup retention, transport redrive, or controlled replay into an isolated rebuild target.
- You need to define meaningful consumer/source-lag evidence without treating inbox receipts as a queue; no inbox-table health inference is implemented.

## Patterns Index

| Goal | Use this | See |
|---|---|---|
| Make a consumer idempotent against redeliveries | `services.AddTrellisInbox<TContext>(o => o.ConsumerId = "...")` or `trellis.UseInbox<TContext>(...)` + `modelBuilder.AddTrellisInbox()` | [Wiring: two required calls](#wiring-two-required-calls), [InboxServiceCollectionExtensions](#inboxservicecollectionextensions) |
| Hand a received message to the inbox | `IInboxDispatcher.DispatchAsync(envelope, ct)` from your transport adapter — returns `Processed` or `SkippedDuplicate` | [IInboxDispatcher](#iinboxdispatcher), [InboxDispatchOutcome](#inboxdispatchoutcome) |
| Drive a gap-free pull / anti-join consumer | `IInboxStore.FilterUnprocessedAsync(consumerId, ids, ct)` to skip already-processed feed rows | [IInboxStore](#iinboxstore) |
| Resume a pull consumer without rescanning the whole feed | `services.AddTrellisConsumerCheckpointStore<TContext>()` + `modelBuilder.AddTrellisConsumerCheckpoints()`; `IConsumerCheckpointStore.GetAsync` / `SetAsync` | [Pull-consumer checkpoint](#pull-consumer-checkpoint-resume-cursor) |
| Map the deduplication table | `modelBuilder.AddTrellisInbox()` in `OnModelCreating` | [InboxModelBuilderExtensions](#inboxmodelbuilderextensions), [InboxMessage](#inboxmessage) |
| Identify this subscriber for per-consumer dedup | `InboxOptions.ConsumerId` (required) | [InboxOptions](#inboxoptions) |
| Supply the same guarantee over a non-EF store | Implement `IInboxStore` **and** a coordinated `IInboxDispatcher` that commits it with handler effects | [IInboxStore](#iinboxstore) |
| Understand why a failed handler reprocesses | The dispatcher is **non-swallowing**: a handler throw rolls back and rethrows, so the transport redelivers | [Processing semantics](#processing-semantics) |
| Connect a producer's outbox to a consumer's inbox | Carry the outbox `OutboxMessage.Id` verbatim as the envelope `MessageId` | [Relationship to the outbox](#relationship-to-the-outbox) |
| Inspect processing evidence, retain dedup history safely, or rebuild from archived events | Application-owned operations; metadata-only inspection, opt-in retention, and isolated replay | [Operational requirements and transport boundary](#operational-requirements-and-transport-boundary) |
| Supply optional consumer/source-lag evidence | Implement the outbox package's `IConsumerLagProvider`; no inbox-table health inference is shipped | [Consumer-lag health requirements](#consumer-lag-health-requirements) |

## Common traps

- **`AddTrellisInbox<TContext>()` only registers the service half.** Without `modelBuilder.AddTrellisInbox()` the `TrellisInboxMessages` table is unmapped, and without a transport adapter calling `IInboxDispatcher.DispatchAsync(...)` nothing is ever deduplicated. See [Wiring: two required calls](#wiring-two-required-calls).
- **Inbox registration is not publisher routing.** `UseIntegrationEvents(...)` and `UseInbox<TContext>()` can share handler registrations, but the default in-process `IIntegrationEventPublisher` does not call the inbox. Its swallowed consumer failures still process integration outbox rows without retry. An adapter must actually call `IInboxDispatcher` with the stable message id and expose failures to a redelivering transport.
- **The `MessageId` must be stable across redeliveries.** Dedup keys on `(ConsumerId, MessageId)`. If the transport assigns a *new* id on every delivery (instead of carrying the producer's `OutboxMessage.Id` verbatim), every redelivery looks new and is reprocessed. The envelope's lineage fields (`MessageSource`, `CausationId`, `CorrelationId`) are observability only and never participate in dedup.
- **`ConsumerId` must be stable across deploys.** It is part of the dedup key, so renaming it changes the dedup namespace. Records under the old id do not protect deliveries under the new id, so any redelivered or replayed message can run again. Each independent subscriber/consumer-group uses its own stable `ConsumerId`.
- **The guarantee is local-side-effects-only.** The dedup row and the handlers' writes through the injected `TContext` commit in one `SaveChanges`. Effects that are *not* part of that save — sending an email, calling a downstream API, writing through a different `DbContext`/connection — are not covered and need their own idempotency. A handler that writes through a second context escapes the dedup unit of work.
- **Handlers run before the save and must propagate failures.** Unlike the default `IIntegrationEventPublisher` (which logs and swallows handler exceptions), the inbox dispatcher is non-swallowing: a handler throw aborts the unit of work before anything is saved — no dedup row, no side effects — and propagates so the transport redelivers. Make handlers safe to re-run, and do not call `SaveChanges` inside a handler (it would break the single-save atomicity).
- **A new message must have at least one registered handler.** An empty `IEnumerable<IIntegrationEventHandler<TEvent>>` is a configuration failure: the dispatcher throws before `SaveChanges`, leaving no dedup row, so a transport redelivery can succeed after registration is corrected. Register an explicit no-op handler when intentionally acknowledging an event without local work.
- **The built-in EF dispatcher and store are an atomic pair.** Every `AddTrellisInbox<TContext>()` call rejects an incompatible `IInboxStore` already in the collection, including an override added after an earlier call; `InboxDispatcher<TContext>` remains the runtime backstop for overrides added after the final call. A non-EF or otherwise custom store needs its own coordinated `IInboxDispatcher`; a separately committing receipt can suppress redelivery after handler failure.
- **Inbound trace context is optional.** Valid W3C `TraceParent` / `TraceState` continue the producer's trace; missing or malformed headers do not prevent processing. Correlation is an application-owned business identifier, not the W3C trace id. The dispatcher does not forward arbitrary baggage or authenticated actor identity.
- **An absent `ConsumerId` fails fast at registration.** `AddTrellisInbox` calls `InboxOptions.Validate()`, which throws `InvalidOperationException` if `ConsumerId` is blank, so the misconfiguration surfaces at startup rather than on the first message.
- **The checkpoint is not a dedup substitute.** `IConsumerCheckpointStore` is a performance resume cursor, not the correctness boundary — a high-water cursor can skip a row committed out of order. Always pair it with an overlap window **and** `FilterUnprocessedAsync`; never advance it past rows that aren't known processed. See [Pull-consumer checkpoint](#pull-consumer-checkpoint-resume-cursor).
- **The inbox is not a failed-message queue or payload archive.** It stores committed dedup metadata, not bodies, failed attempts, or dead-letter reasons. Absence of a row means no **retained** processing evidence, not proof that the message never ran. See [Operational requirements and transport boundary](#operational-requirements-and-transport-boundary).

## How the inbox works

The inbox turns at-least-once **delivery** into effectively-once **processing** with one short unit of work per message:

1. **Receive.** An application-owned transport adapter (a broker consumer, or an in-process channel) deserializes the message into an [IntegrationEnvelope](#integrationenvelope) — a stable `MessageId` plus the `IIntegrationEvent` — and calls `IInboxDispatcher.DispatchAsync(envelope, ct)`.
2. **Open a unit of work.** The dispatcher creates a DI scope and resolves the consumer's `TContext` and its matching scoped `EfInboxStore<TContext>`. An arbitrary `IInboxStore` replacement is rejected because the dispatcher cannot prove it shares this commit.
3. **Deduplicate.** The dispatcher maps the envelope to an `InboxRecord` and calls `IInboxStore.TryRecordAsync(consumerId, record, ct)`, which checks for an existing `(ConsumerId, MessageId)` row. If one exists the dispatcher returns immediately — a no-op; nothing was staged. Otherwise it stages a new `InboxMessage` row (without saving).
4. **Fan out (non-swallowing).** The dispatcher resolves every `IIntegrationEventHandler<TConcrete>` for the runtime event type from the *same* scope and awaits each. Because the handlers share the scope, a handler that injects `TContext` writes through the very context that holds the staged dedup row. Handler exceptions **propagate** — and because nothing has been saved yet, a throw leaves no dedup row and no side effects, so the transport redelivers. Resolving zero handlers also throws before the save rather than acknowledging and permanently deduplicating an unhandled message.
5. **Commit.** One `SaveChangesAsync` persists the dedup row and the handler writes together, atomically, under EF Core's implicit transaction. No user-initiated transaction is opened, so the inbox composes with a retrying execution strategy (`EnableRetryOnFailure`) like the rest of Trellis. A concurrent dispatch that recorded the same `(ConsumerId, MessageId)` first makes this save fail with a duplicate-key `DbUpdateException`; the dispatcher then re-checks, in a fresh scope, whether the dedup row was actually committed — if so the message is already processed and the failure is swallowed — this call commits nothing, the handlers' staged writes having rolled back with the save; if not, the duplicate came from a handler's own unique write and propagates so the transport redelivers.

## Wiring: two required calls

```csharp
// 1. Map the inbox dedup table.
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.AddTrellisInbox();

// 2. Register the dispatcher + store + options, and the handlers that consume the messages.
services.AddTrellis(trellis => trellis
    .UseIntegrationEvents(typeof(Program).Assembly)   // the IIntegrationEventHandler<T> consumers
    .UseEntityFrameworkUnitOfWork<AppDbContext>()     // only for this process's Mediator commands
    .UseInbox<AppDbContext>(o => o.ConsumerId = "orders-service"));
```

`UseEntityFrameworkUnitOfWork<TContext>()` is not required by inbox dispatch; the
dispatcher owns its single `SaveChangesAsync`. Select that slot only when this process
also handles transactional Mediator commands. An inbox handler must stage writes on its
scoped context and return. Do not dispatch a command through
`TransactionalCommandBehavior` from the handler: that command commits the already-staged
dedup row and prior handler writes early. If that handler or a later handler then fails,
redelivery sees the committed dedup row and skips the message, silently losing the
remaining work.

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
            TraceParent = raw.TraceParent,
            TraceState = raw.TraceState,
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
- **Repeated calls for the same `TContext` accumulate configuration.** Each call applies its `configure` callback on top of the already-registered `InboxOptions` instance and re-runs `Validate()`, so a later callback wins per setting rather than being silently discarded by `TryAdd`. Configuration is applied to a clone and committed only after `Validate()` succeeds, so a rejected callback leaves the container's options untouched. A call for a different `TContext` throws before changing options; one inbox is supported per service collection.
- **Do not pre-register or override `IInboxStore` when using this helper.** The built-in EF dispatcher requires `EfInboxStore<TContext>` so the receipt and handler writes share its one `SaveChanges`. A custom store must be paired with a custom dispatcher instead of being mixed into `AddTrellisInbox<TContext>()`.
- **Do not register `InboxOptions` yourself via a factory or implementation type.** The helper layers onto the *last* `InboxOptions` descriptor, which is the one the container resolves. If that descriptor is a factory or type registration it cannot be cloned, so the call throws `InvalidOperationException` rather than silently configuring an instance the dispatcher would never receive.
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
- A new message with no registered `IIntegrationEventHandler<TEvent>` throws `InvalidOperationException` before the commit. It is not returned as `Processed`, and no dedup row is written.
- The shipped `InboxDispatcher<TContext>` accepts only its matching `EfInboxStore<TContext>`. Pair a custom `IInboxStore` with a custom dispatcher that can commit the receipt and handler effects atomically.
- The dispatcher opens `IntegrationMessageContext.BeginProcessing(envelope)` for the handler and commit, restoring the previous async-local context even on failure or deduplication. A nonblank inbound `CorrelationId` is available to handler-side domain-event capture; `CurrentMessageId` is the inbound message's stable id, the direct cause of any resulting domain row. A surrounding application-supplied correlation remains available if the envelope has none. The dispatcher starts a consumer `Activity` from a valid remote W3C `TraceParent` / `TraceState` pair; invalid or missing trace metadata is ignored for activity parent selection rather than throwing or dead-lettering. The activity and ambient message scope end when dispatch completes.

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

Service-provider interface (SPI) for the dedup record. `EfInboxStore<TContext>` is the shipped EF implementation. A non-EF adapter supplies the same guarantee by implementing both this store and a coordinated dispatcher; the shipped EF dispatcher deliberately rejects arbitrary store replacements because it cannot include a separately committing store in its `SaveChanges`.

```csharp
public interface IInboxStore
{
    Task<bool> TryRecordAsync(string consumerId, InboxRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> FilterUnprocessedAsync(
        string consumerId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);
}
```

`TryRecordAsync` records the message as processed by `consumerId` **inside the caller's current unit of work**, so the dedup record and the handler side effects commit together. Returns `true` if newly recorded, or `false` if the `(ConsumerId, MessageId)` pair was already processed — a duplicate the dispatcher skips. `EfInboxStore` does an existence check for the fast path and stages an `InboxMessage` for the slow path; the composite primary key is the authoritative guard under concurrency. It throws `ArgumentException` when `consumerId` is blank or exceeds `InboxOptions.MaxConsumerIdLength`.

`FilterUnprocessedAsync` returns the subset of `messageIds` that `consumerId` has **not** yet processed (those without a dedup row), preserving the input order. It powers the gap-free **inbox-as-cursor / anti-join** pull model: read a window of the source feed and dispatch every row whose `MessageId` this query returns, rather than tracking a fragile high-water cursor that can skip a row committed out of sequence order. It is an optimization, not the correctness boundary — a row may be processed by another worker between this query and `DispatchAsync`, which still deduplicates — and it is a pure read that stages nothing. `EfInboxStore` runs it as a single anti-join query (`AsNoTracking`); it throws `ArgumentException` if `consumerId` is blank or exceeds `InboxOptions.MaxConsumerIdLength`.

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

### `CheckpointServiceCollectionExtensions`

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellisConsumerCheckpointStore<TContext>(this IServiceCollection services) where TContext : DbContext` | `IServiceCollection` | Registers singleton `IConsumerCheckpointStore` backed by `EfConsumerCheckpointStore<TContext>` and supplies `TimeProvider.System` when absent. Leaf store registration; no builder slot. Does not register `TContext` or map its table. |

### `CheckpointModelBuilderExtensions`

| Signature | Returns | Description |
| --- | --- | --- |
| `public static ModelBuilder AddTrellisConsumerCheckpoints(this ModelBuilder modelBuilder)` | `ModelBuilder` | Applies `ConsumerCheckpointConfiguration` to map the checkpoint table. Call from `OnModelCreating` and add the corresponding migration. |

### `ConsumerCheckpointConfiguration`

Public `IEntityTypeConfiguration<ConsumerCheckpoint>` implementation.

| Signature | Returns | Description |
| --- | --- | --- |
| `public void Configure(EntityTypeBuilder<ConsumerCheckpoint> builder)` | `void` | Maps `TrellisConsumerCheckpoints`, the `ConsumerId` primary key with the inbox's consumer-id width, and required `Position` / `UpdatedAt` properties. Throws `ArgumentNullException` for a null builder. |

### ConsumerCheckpoint

The persisted row behind the store, mapped by `AddTrellisConsumerCheckpoints()`.

| Property | Type | Notes |
|---|---|---|
| `ConsumerId` | `string` | The stable subscriber identifier; the primary key. |
| `Position` | `string` | The opaque resume cursor, in the consumer's own encoding. |
| `UpdatedAt` | `DateTimeOffset` | When the checkpoint was last advanced. Diagnostic — useful for spotting a stalled consumer; nothing in the inbox reads it to make decisions. |

It is **performance state, not a domain aggregate**: losing or resetting a checkpoint forces a wider rescan. The inbox anti-join still deduplicates messages whose rows are retained; a rewind beyond the dedup retention horizon can reprocess older messages. Include feed rewind and replay in the [retention policy](#dedup-retention).

## IntegrationEnvelope

The consume-side envelope handed to the dispatcher.

```csharp
public sealed record IntegrationEnvelope(Guid MessageId, IIntegrationEvent Event)
{
    public string? MessageSource { get; init; }
    public Guid? CausationId { get; init; }
    public string? CorrelationId { get; init; }
    public string? TraceParent { get; init; }
    public string? TraceState { get; init; }
}
```

| Member | Type | Notes |
|---|---|---|
| `MessageId` | `Guid` | **Load-bearing.** The stable dedup id — the producer's outbox `OutboxMessage.Id` (a UUIDv7) carried verbatim by the transport. Redeliveries carry the same value. |
| `Event` | `IIntegrationEvent` | **Load-bearing.** The deserialized integration event dispatched to its handlers. |
| `MessageSource` | `string?` | Optional. The producing service / bounded context, for observability. |
| `CausationId` | `Guid?` | Optional lineage: the id of the message that directly caused this one. |
| `CorrelationId` | `string?` | Optional lineage: the workflow / conversation id shared across a business transaction. |
| `TraceParent` | `string?` | Optional persisted W3C traceparent from the producer; valid values parent the consumer activity. |
| `TraceState` | `string?` | Optional persisted W3C tracestate; carried alongside the traceparent. |

Only `(ConsumerId, MessageId)` determines deduplication. `Event` is the handler payload, not part of that key. `MessageSource`, `CausationId`, and `CorrelationId` are recorded in the dedup row for audit. The W3C fields are used for the scoped consumer activity and are **not** columns in `InboxMessage`.

## InboxMessage

A persisted record that a `(ConsumerId, MessageId)` message has been processed — one row per message per consumer. Its existence *is* the dedup guarantee. Read-only to application code (private constructors, private setters, internal `Create`).

| Member | Type | Notes |
|---|---|---|
| `ConsumerId` | `string` | Part of the primary key. The stable subscriber identifier. Max length 256. |
| `MessageId` | `Guid` | Part of the primary key. The message's stable id (the producer's outbox id). |
| `MessageSource` | `string?` | The producing service / bounded context, if the envelope supplied one. |
| `EventType` | `string` | Assembly-qualified name of the integration event type, recorded for audit. |
| `OccurredAt` | `DateTimeOffset` | Copied from the event's `OccurredAt`. |
| `ProcessedAt` | `DateTimeOffset` | UTC timestamp assigned when the dedup row is staged, before handlers and the final save; not a commit timestamp. Indexed to support explicitly approved pruning. |
| `CausationId` | `Guid?` | Optional lineage copied from the envelope. |
| `CorrelationId` | `string?` | Optional lineage copied from the envelope. |

`InboxMessageConfiguration` maps the table `TrellisInboxMessages`, the composite `(ConsumerId, MessageId)` primary key (the uniqueness guard that makes a concurrent duplicate fail at `SaveChanges`), and an index on `ProcessedAt`.

`InboxMessage` is transient infrastructure, not a domain aggregate. No automatic expiry is shipped. Pruning requires an application-approved horizon covering **all** redelivery and replay sources, not just ordinary broker retries; deleting a row removes its dedup protection. See [Dedup retention](#dedup-retention).

### `InboxMessageConfiguration`

Public `IEntityTypeConfiguration<InboxMessage>` implementation, normally applied by `AddTrellisInbox(ModelBuilder)`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public void Configure(EntityTypeBuilder<InboxMessage> builder)` | `void` | Applies the inbox table mapping described above. Throws `ArgumentNullException` for a null builder. |

## InboxOptions

Configuration for the inbox.

| Property | Type | Default | Notes |
|---|---|---|---|
| `ConsumerId` | `string` | `""` (must be set) | **Required.** A stable identifier for this subscriber / consumer-group; part of the dedup key, so two services consuming the same message each get one effective processing. Keep it stable across deploys — renaming it resets dedup history. `Validate()` throws `InvalidOperationException` if it is blank, or if it exceeds `MaxConsumerIdLength`. |
| `ActivitySource` | `ActivitySource` | `null` (dispatcher creates and owns its own) | The source the dispatcher starts consumer activities on. Left unset, `InboxDispatcher<TContext>` creates a default-named source and disposes it with itself; supplying one hands ownership to the caller. Tests that assert on sampled activities should supply their own uniquely-named source rather than subscribing to the shared default by name, since an `ActivityListener` is process-wide and matches by source name. |

| Constant | Value | Notes |
|---|---|---|
| `MaxConsumerIdLength` | `256` | The mapped key-column width. `ConsumerId` is stored in a fixed-width key column, so a longer value is rejected at registration rather than truncated at insert. |

## Processing semantics

The guarantee is effectively-once **processing of local side effects**, layered on a transport's at-least-once **delivery** when an adapter actually calls the dispatcher and exposes its failures:

- A first delivery runs the handlers and commits the `(ConsumerId, MessageId)` dedup row in the same `SaveChanges`. A redelivery finds the row and is skipped.
- **A handler failure before saving leaves nothing persisted.** A handler throw prevents the save of the dedup row and staged local writes, and propagates out of `DispatchAsync` so the transport can redeliver. A save that rolls back also commits neither. An exception or lost acknowledgement is not universal proof that no commit happened: commit confirmation or transport settlement can fail after durable processing. Retry with the same identity and let the retained dedup row resolve that uncertainty. The inbox does not itself retry or dead-letter; that is the transport's job.
- **Concurrency is resolved by the primary key.** When two deliveries of the same message race, both may pass the existence check, but only one `SaveChanges` inserts the `(ConsumerId, MessageId)` row; the other fails with a duplicate-key `DbUpdateException`. The dispatcher then re-checks in a fresh scope whether the dedup row was committed: present → the message is already processed, so the failure is swallowed and this call commits nothing; absent → the duplicate came from a handler's own unique write and propagates. The losing delivery's staged writes are discarded with the failed save, so exactly one delivery applies the side effects.
- **Local only.** Only effects written through the injected `TContext` are covered. External calls (emails, downstream APIs) and writes through a different connection are outside the save and need their own idempotency.

## Operational requirements and transport boundary

**Requirements, not a new API.** Trellis ships the dispatcher, dedup store, table mapping,
and optional checkpoint store. It does **not** ship an inbox maintenance facade, automatic
pruning job, payload archive, or replay coordinator. The following contract defines
application-owned operations and the acceptance boundary for any future implementation;
it does not authorize new public methods or change normal dispatch.

### Evidence and ownership

| Owner | Evidence or responsibility |
|---|---|
| Inbox | Retained, committed `(ConsumerId, MessageId)` records and the metadata listed under [InboxMessage](#inboxmessage). No persisted in-flight/failed state, delivery-attempt count, error history, body, or W3C trace columns. |
| Transport and adapter | Delivery attempts, locks/offsets, acknowledgements, retry policy, dead-letter payloads/reasons, and redrive. Both dispatcher outcomes can be acknowledged; failures must remain visible to the transport. See [Azure Service Bus settlement](trellis-api-messaging-azureservicebus.md#settlement). |
| Consumer application and payload source | Business idempotency, operator authorization, the complete redelivery/replay horizon, and a durable log or archive retaining original message identity, payload, and contract version when rebuild replay is required. |
| Operations host | Bounded inspection/pruning, an audit of authorized actions and partial progress, and isolation of replay from live storage and external effects. |

A failed handler before the dispatcher's save leaves no inbox failure record to inspect
or replay. A transport failure **after** the commit can coexist with a committed inbox row.
An operator must reconcile those sources by stable message identity rather than treating
either the absence of a row or a transport error as proof of the business outcome.
Unlike [outbox dead-letter maintenance](trellis-api-efcore-outbox.md#dead-letter-and-replay),
the inbox has no parked payload from which it could reconstruct a delivery.

### Metadata inspection

- Scope every lookup/list to the authorized storage/composition and a specific `ConsumerId`;
  a `ConsumerId` is a dedup namespace, not an authentication or tenant-isolation mechanism.
- Support exact message lookup and bounded lists of retained metadata. Use a processed-time
  range and deterministic ordering, with `MessageId` breaking `ProcessedAt` ties within
  one consumer. Source, event type, and lineage filters are diagnostic, not dedup keys.
- A present row is committed processing evidence under that consumer's dispatch contract.
  A missing row means **no retained record**: it may never have committed, may be in flight,
  may have been pruned, or may belong to another consumer/storage scope.
- Read without tracking or staging writes, independently of a live dispatch's context.
  Propagate cancellation and query failures; do not disguise unavailable evidence as an
  empty list or absent message. The host owns page/query limits and access auditing.
- Do not present inbox metadata as a message body, a stable wire-contract version, a
  failed-message list, or proof that an external effect happened. `EventType` is the
  recorded CLR type name; the replay source must retain its own contract/version data.

### Dedup retention

**No automatic expiry.** Retention is explicitly opt-in, with no universal time-based
default. The application must bound every source that can deliver an old identity:
broker retries, delayed delivery, dead-letter/manual redrive, producer republication,
archive replay, and pull-feed rewind/overlap. If any source permits arbitrarily old
messages, there is no finite dedup horizon that preserves the original guarantee.
Keeping payloads longer than dedup rows is safe only when their later use is constrained
to an isolated rebuild or the application explicitly accepts live reprocessing.

Pruning requirements:

- Require an explicit consumer/storage scope, UTC cutoff, bounded batch size, and
  application approval of the horizon. Use `ProcessedAt`, not the event's `OccurredAt`;
  an old business fact can have been processed only recently.
- `ProcessedAt` is assigned when the dedup row is **staged**, before handlers and the final
  save; it is not a commit timestamp. Account for dispatch duration, clock skew, and
  persistence/visibility lag. Coordinate with overlapping dispatch/replay when those
  bounds cannot establish a safe cutoff.
- Capture the cutoff once per run and delete only eligible rows in that scope. Record
  the cutoff, operator, and committed deletion counts. Cancellation or failure must
  expose partial progress; already committed batches are not rolled back by cancellation.
- Do not implicitly reset checkpoints or delete source payloads. A wider rescan or
  redelivery after dedup expiry may apply the message again; retaining a checkpoint is
  not a substitute for the deleted correctness boundary.

An unbounded source requires retaining dedup evidence or explicitly changing the
processing policy. Pruning does not make an expired message safe to redeliver.

### Redrive versus controlled rebuild replay

**Transport redrive** retries a delivery under its original `ConsumerId` and `MessageId`.
A retained committed pair returns `SkippedDuplicate`; a previously failed, uncommitted
delivery can run again. After dedup expiry the same pair can run again too, so redrive
must respect the approved retention horizon. Preserve the original payload/contract and
identity; do not mint a new message id merely to evade deduplication.

**Controlled replay of already-consumed messages** is restricted to an **isolated
rebuild**, not reapplication to live business state:

1. Authorize and audit an explicit replay run, its immutable source range, contract
   versions, replay consumer identity, and isolated target. Read payloads from the
   application-owned durable log/archive; inbox metadata cannot supply them.
2. Use a separate composition root with a distinct `ConsumerId`, stable for that run and
   every restart, plus isolated rebuild storage. Preserve each original `MessageId`.
   `UseInbox<TContext>()` still allows only one inbox per composition; do not mutate the
   live singleton `InboxOptions` or add a second inbox to the live root.
3. Retain live dedup rows unchanged. A new replay namespace intentionally has its own
   history; it is not a reset or continuation of the live consumer's once-only guarantee.
4. Bind handlers to the rebuild context so their local writes and replay dedup row share
   the same save. Disable or isolate emails, downstream calls, broker publication, and
   any emitted outbox relay; a separate consumer id alone does **not** isolate effects.
5. Resume the same run with the same consumer identity and retained replay dedup history.
   Any checkpoint remains a performance cursor: preserve overlap/anti-join correctness
   and do not advance past unaccounted source messages or prune active-run dedup records.
6. Surface unreadable/missing payloads, unsupported contract versions, handler failures,
   and cancellation with run progress. Declare completion only after the selected source
   range is durably accounted for and the application verifies the rebuild. Promotion
   of the rebuilt target is a separate, application-owned decision.

Deleting a live dedup row, renaming the live consumer, or changing a message id to force
live re-execution is **not** this replay policy. Corrective actions on live state require
an independently designed application repair/idempotency policy.

### Acceptance boundary for future operations

**Not implemented:** no inbox maintenance API is shipped. This table specifies
acceptance criteria for future operational tooling.

| Scenario | Required outcome |
|---|---|
| Inspection of an existing, missing, or pruned pair | Report retained evidence accurately, remain read-only, and enforce storage/consumer authorization and query limits. |
| Handler failure before save, or uncertain confirmation after commit | No invented inbox failure row; transport retry keeps the identity and retained dedup prevents a second committed application. |
| Unknown/unbounded redelivery horizon | No automatic pruning or claimed finite effectively-once window. |
| Pruning with recent processing, concurrent work, cancellation, or a database fault | Respect the approved cutoff and isolation; expose committed partial progress and failures. |
| One archived message delivered to live and rebuild consumers | Independent dedup histories; no deletion of live evidence and no rebuild writes/publication into live targets. |
| Interrupted replay resumed or an archived message repeated within that run | Same replay consumer and original message id; at most one committed local application while replay dedup is retained. |
| Missing payload or unsupported version | Explicit incomplete/failed run, not a success-shaped skip or reconstruction from inbox metadata. |

No registration API or builder slot is added here. Any later application-facing
maintenance/replay feature requires its own approval, registration classification,
and behavioral tests. Health checks have their own [requirements](#consumer-lag-health-requirements);
table size, a checkpoint timestamp, or an empty inbox alone does not establish transport
lag, relay liveness, or consumer health.

## Consumer-lag health requirements

No inbox-table health check is shipped. Consumer/source lag is evaluated by the outbox
package's named readiness check through an application-owned
[`IConsumerLagProvider`](trellis-api-efcore-outbox.md#iconsumerlagprovider-and-consumerlagevidence).
These requirements add no fields to `InboxMessage` and no methods to the checkpoint store.

Inbox rows are retained committed receipts, not source queue state. `ProcessedAt` is
assigned when staging before handlers/save, not at commit. Table size, the newest/oldest
receipt, an empty inbox, or a checkpoint's update time cannot establish consumer liveness
or pending work. `ConsumerCheckpoint.Position` is opaque; never subtract arbitrary cursor
strings or assume a high-water mark proves gap-free accounting.

An application/transport-owned provider returns one conservative aggregated
`ConsumerLagEvidence` snapshot and must define:

- The authorized storage/composition, stable `ConsumerId`, source/subscription, and
  partition scope, including which partitions must be covered.
- The measured quantity and unit: age of the oldest unaccounted source message and/or
  number of unaccounted events, with a UTC sample time and explicit freshness/coverage.
  Source business-event age and last-message inactivity are different quantities.
- How messages become durably accounted for, including `Processed` and
  `SkippedDuplicate` outcomes, gaps/out-of-order commits, redeliveries, and retention.
  A broker offset or queue-depth metric is not automatically proof of inbox completion.
- How partition evidence is combined: expose missing coverage, compare configured
  age/count thresholds, and do not average away a lagging partition. Declare any
  estimates/lower bounds rather than label them exact totals. A lower-bound count below
  the degraded threshold cannot establish a healthy upper bound and degrades readiness.

Lag is explicitly disabled when `ConsumerLagMeasures.None` is selected, not observed
healthy. Once age, count, or both are selected, missing coverage/measurements and
stale/future evidence degrade health; provider errors and the shared probe timeout are
unhealthy, and caller cancellation propagates only to that caller's wait. The provider
reports its actual measures, required/covered partition counts, original source sample
time, oldest durably unaccounted source timestamp and/or conservative summed count.
Live and isolated-rebuild consumers must not share evidence or cache entries merely
because they read the same source.

Do not dispatch, settle messages, advance/reset checkpoints, prune dedup rows, or trigger
replay to test health. The application owns the provider and endpoint authorization; vendor SDK adapters stay
outside `Trellis.ServiceDefaults`. Implementations must prove quiet-source zero lag with
authoritative evidence, missing/stale partition coverage,
out-of-order accounting, disabled versus unavailable signals, and timeout/cancellation
behavior without changing the dispatch or retention contract.

## Relationship to the outbox

The outbox is the **produce** side (atomically capture, then retry exposed publication failures), the inbox is the **consume** side (idempotently receive). Reliable broker handoff requires a publisher that awaits acceptance and propagates publication failures; acceptance does not prove downstream consumption. They meet at the message id: the producer's `OutboxMessage.Id` (a UUIDv7) is carried verbatim by the transport and becomes the inbox `MessageId`. The inbox collapses redeliveries of that same row to one effective processing per `ConsumerId`. A translator that reruns because its own attempt failed or its progress was not durably saved can create a new integration row with a new ID; the inbox does not collapse those logical duplicates. Deduplicate them on business identity in the consumer's transaction.

The transport adapter must actually call `IInboxDispatcher` and let failures reach the transport for redelivery; this package defines the dispatcher and store seams, not broker routing. An optional [Azure Service Bus adapter](trellis-api-messaging-azureservicebus.md#wire-format) is shipped separately.

A modular monolith can use default local fan-out without an inbox **when best-effort notification is acceptable**. Ordinary consumer failures are logged and swallowed, and the integration outbox row is processed without retrying them. If transactional local processing is required, an adapter must route through the inbox with the stable message id and expose failures to its delivery mechanism. `UseInbox` registration alone does not change the default publisher; Trellis does not install a reliable local loopback adapter automatically.
