---
package: Trellis.EntityFrameworkCore.Outbox
namespaces: [Trellis.EntityFrameworkCore]
types: [OutboxMessage, OutboxMessageKind, OutboxOptions, OutboxServiceCollectionExtensions, OutboxModelBuilderExtensions]
version: v1
last_verified: 2026-06-09
audience: [llm]
---
# Trellis.EntityFrameworkCore.Outbox

**Package:** `Trellis.EntityFrameworkCore.Outbox`  
**Namespace:** `Trellis.EntityFrameworkCore`  
**Purpose:** Transactional outbox — atomically capture aggregate domain events to an EF Core table in the same transaction as the aggregate change, then durably relay them to Trellis domain-event handlers after the commit.

This package composes `Trellis.EntityFrameworkCore` (the capture interceptor and table mapping) with `Trellis.Mediator` (the `IDomainEventPublisher` dispatch seam the relay reuses). It opts out of AOT/trim, exactly like `Trellis.EntityFrameworkCore`.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#trellis-cross-package-cookbook) — recipes using this package.

## Use this file when

- You need domain events to survive a crash between the commit and the in-pipeline dispatch (the gap a plain `UseDomainEvents()` pipeline leaves open).
- You are wiring `AddTrellisOutbox`, `AddTrellisOutboxInterceptor`, or the `UseOutbox<TContext>()` builder slot.
- You are reasoning about the outbox's delivery guarantee, retry/parking behavior, or event-serialization constraints.

## Patterns Index

| Goal | Use this | See |
|---|---|---|
| Capture an aggregate's domain events into the outbox table atomically with the aggregate write | `optionsBuilder.AddTrellisOutboxInterceptor()` (capture) + `modelBuilder.AddTrellisOutbox()` (table) | [Wiring: three required calls](#wiring-three-required-calls), [`OutboxModelBuilderExtensions`](#outboxmodelbuilderextensions) |
| Run the background relay that re-dispatches captured events after commit | `services.AddTrellisOutbox<TContext>(configure?)` or `trellis.UseOutbox<TContext>(configure?)` | [`OutboxServiceCollectionExtensions`](#outboxservicecollectionextensions), [`UseOutbox` builder slot](#useoutbox-builder-slot) |
| Tune poll interval, batch size, or max attempts | `OutboxOptions` (via the `configure` delegate) | [`OutboxOptions`](#outboxoptions) |
| Inspect a captured-but-not-yet-relayed message | Query `TContext.Set<OutboxMessage>()` (read-only; rows are produced by the interceptor) | [`OutboxMessage`](#outboxmessage) |
| Understand why a failing handler does not re-deliver | At-least-once **delivery** semantics; handler exceptions are swallowed by the publisher | [Delivery semantics](#delivery-semantics) |
| Decide how to shape an event so it round-trips | Use attribute-driven value objects; `Maybe<T>` members are supported (present → value, absent → `null`) | [Serialization](#serialization) |
| Publish a stable external contract instead of raw domain events | Translate a domain event into an `IIntegrationEvent` via `IIntegrationEventCollector`; the relay routes it to `IIntegrationEventPublisher` | [Integration events](#integration-events) |

## Common traps

- **The interceptor and the model mapping are *not* optional extras.** `AddTrellisOutbox<TContext>()` only registers the relay. Without `optionsBuilder.AddTrellisOutboxInterceptor()` nothing is captured, and without `modelBuilder.AddTrellisOutbox()` the `TrellisOutboxMessages` table is unmapped. All three calls are required — see [Wiring: three required calls](#wiring-three-required-calls).
- **The relay host must have the producing assemblies loaded.** The relay rehydrates each event from its assembly-qualified `EventType` via `Type.GetType`. If the worker process does not reference the assembly that declares the event, the message fails deserialization and parks after `MaxAttempts`.
- **Handlers must be idempotent.** Delivery is at-least-once; a crash between dispatch and the relay's bookkeeping `SaveChanges` re-delivers the message on the next drain.
- **Caller-registered `JsonSerializerOptions` converters do not travel with the payload.** The outbox serializer round-trips `[JsonConverter]`-attributed value objects and `Maybe<T>` members; converters registered only on a caller's own options are not consulted — shape those members with attribute-driven types or nullable transports. See [Serialization](#serialization).
- **`OutboxMessage` is read-only to application code.** Its constructor is private and its mutators are internal; rows are produced exclusively by the capture interceptor and advanced by the relay.
- **Post-commit cancellation can defer the event-clear.** The capture interceptor clears aggregate events in `SavedChanges`, which runs *after* the commit. `AggregateETagInterceptor` (registered first by `AddTrellisInterceptors`) throws by design if the save's `CancellationToken` is cancelled in that post-commit window, and EF invokes `SavedChanges` interceptors in registration order, so the outbox clear can be skipped — leaving the aggregate's events in memory while the outbox row is already durable. Reusing the *same* context for another save then re-captures them into a duplicate row. This is bounded and absorbed by the at-least-once + idempotent-handler contract; disposing the context after a cancelled save (the normal per-request lifetime) avoids it entirely.
- **Persist-on-failure (`FailAfterCommit`) events are dispatched by the outbox.** The capture interceptor cannot see the command `Result`, so it captures events from *every* commit — including the persist-on-failure commits that `TransactionalCommandBehavior` performs. Without the outbox, `DomainEventDispatchBehavior` deliberately does **not** dispatch events for a failed result: the `FailAfterCommit` contract (documented under persist-on-failure in `trellis-api-core.md`) treats those events as discarded, *not* a durable retry buffer. With the outbox enabled, those events are captured and the relay delivers them — so the suppression no longer holds. If you depend on it, do not raise domain events on persist-on-failure paths under the outbox: return a success result for events you want delivered, and model post-failure side effects explicitly (a follow-up command, or a dedicated outbox row).

## How the outbox works

The outbox replaces the in-pipeline domain-event dispatch with a durable, two-phase flow:

1. **Capture (inside the transaction).** `OutboxCaptureInterceptor` (a `SaveChangesInterceptor`) scans the change tracker during `SavingChanges` for `IAggregate` entries with uncommitted events. It serializes each event and adds one `OutboxMessage` row per event to the same `SaveChanges` — so the rows commit atomically with the aggregate. It does **not** clear the aggregate's events yet.
2. **Clear (after the commit succeeds).** In `SavedChanges` the interceptor calls each aggregate's `AcceptChanges()`. Because the events are cleared only after a successful commit, a failed save leaves the in-memory events intact for retry, and the interceptor detaches the rows it staged on `SaveChangesFailed` and `SaveChangesCanceled` so a retry on the same context does not double-capture.
3. **Single dispatch path.** Since the aggregate's events are cleared after the commit (in `SavedChanges`), a post-commit in-pipeline `DomainEventDispatchBehavior` observes an empty list and dispatches nothing. The relay becomes the one dispatcher.
4. **Relay (after the commit).** `OutboxRelay<TContext>` is a `BackgroundService`. Each poll it opens a bookkeeping scope, drains a batch of pending rows ordered by `Sequence`, and routes each by `OutboxMessage.Kind`. A `Domain` row is rehydrated and published through `IDomainEventPublisher` **in a dedicated per-message scope** — so a handler that injects `TContext` receives its own context, never the relay's bookkeeping context, and its tracked changes never ride the relay's `SaveChanges`. An `Integration` row is published through `IIntegrationEventPublisher`. The relay marks each row processed (or records the failure) and persists the batch with one `SaveChanges` on the bookkeeping context.

## Integration events

A **domain event** is internal to the bounded context; an **integration event** (`IIntegrationEvent`) is the stable, versioned contract published to other services. The outbox keeps them separate: domain events are captured from aggregates, and integration events are *translated* from them so external consumers never couple to your internal model. See `trellis-api-mediator.md` for the `IIntegrationEvent*` types.

The flow:

1. A **translator** — an ordinary `IDomainEventHandler<TDomainEvent>` — injects `IIntegrationEventCollector` and `Add(...)`s integration events while the relay re-dispatches the domain event.
2. After publishing a `Domain` row, the relay drains the per-message scope's collector and stages each produced integration event as a new `OutboxMessageKind.Integration` row — in the **same** `SaveChanges` that marks the domain row processed, so an integration event is enrolled only once its source domain event is durably dispatched.
3. A later drain publishes each `Integration` row through `IIntegrationEventPublisher` (default in-process fan-out to `IIntegrationEventHandler<T>`; replace the registration with a message-broker adapter to deliver to other services).

Register the consumer side with `services.AddIntegrationEventDispatch(...)` / `AddIntegrationEventHandler<TEvent, THandler>()`, or the `TrellisServiceBuilder.UseIntegrationEvents(...)` slot. The collector is optional: outboxes that capture only domain events never register it and are unaffected.

Delivery is at-least-once and a retried domain event re-runs its translator, so a consumer may observe the same integration event more than once (with a different `OutboxMessage.Id` each time) — **dedupe on business identity, not the message id.**

## Wiring: three required calls

```csharp
// 1. Map the outbox table.
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.AddTrellisOutbox();

// 2. Register the capture interceptor on the context options.
options.UseNpgsql(connectionString)
       .AddTrellisInterceptors()
       .AddTrellisOutboxInterceptor();

// 3. Register the relay + your handlers + IDomainEventPublisher.
services.AddTrellis(trellis => trellis
    .UseDomainEvents(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseOutbox<AppDbContext>());
```

The relay needs `IDomainEventPublisher` and the `IDomainEventHandler<TEvent>` registrations that `UseDomainEvents()` / `AddDomainEventDispatch()` provide; register them in the same container.

## OutboxServiceCollectionExtensions

Static class. Registers the relay for a `DbContext`.

```csharp
public static IServiceCollection AddTrellisOutbox<TContext>(
    this IServiceCollection services,
    Action<OutboxOptions>? configure = null)
    where TContext : DbContext;
```

- Registers `OutboxRelay<TContext>` as an `IHostedService`, the (configured) `OutboxOptions` as a singleton, and `TimeProvider.System` if no `TimeProvider` is already registered.
- `OutboxOptions` and `TimeProvider` are added with `TryAdd`, so a single shared `OutboxOptions` instance backs all relays in the container. Configure the outbox for one `DbContext` per composition (the `UseOutbox` slot enforces this).
- Idempotent on the hosted service only in the sense that calling it twice for the same `TContext` registers two relays — prefer the `UseOutbox<TContext>()` builder slot, which fails fast on a duplicate.

This is the service-collection half only. Pair with `AddTrellisOutbox(ModelBuilder)` and `AddTrellisOutboxInterceptor(DbContextOptionsBuilder)`.

## OutboxModelBuilderExtensions

Static class. EF Core model + interceptor hooks.

```csharp
public static ModelBuilder AddTrellisOutbox(this ModelBuilder modelBuilder);

public static DbContextOptionsBuilder AddTrellisOutboxInterceptor(
    this DbContextOptionsBuilder optionsBuilder);

public static DbContextOptionsBuilder<TContext> AddTrellisOutboxInterceptor<TContext>(
    this DbContextOptionsBuilder<TContext> optionsBuilder)
    where TContext : DbContext;
```

- `AddTrellisOutbox(ModelBuilder)` — applies `OutboxMessageConfiguration`, mapping the `TrellisOutboxMessages` table. Call from `OnModelCreating`.
- `AddTrellisOutboxInterceptor(...)` — adds the shared, stateless `OutboxCaptureInterceptor`. The generic overload preserves the `DbContextOptionsBuilder<TContext>` fluent type so it chains after `AddTrellisInterceptors<TContext>()`. The interceptor is a singleton; registering it on multiple contexts is safe.

## UseOutbox builder slot

`TrellisServiceBuilder.UseOutbox<TContext>(Action<OutboxOptions>? configure = null)` (in `Trellis.ServiceDefaults`) is the opinionated entry point. It wraps `AddTrellisOutbox<TContext>()` and:

- Fails fast with `InvalidOperationException` if called more than once — one outbox relay per composition.
- Is order-independent versus `UseEntityFrameworkUnitOfWork<TContext>()`: the relay is a hosted service, not a Mediator behavior, so the canonical pipeline order is identical whether `UseOutbox` is called before or after the unit-of-work slot.
- Carries `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` because the outbox builds on the non-AOT `Trellis.EntityFrameworkCore`.

The slot owns only the service registration; the capture interceptor and table mapping are still wired on the `DbContext` (steps 1–2 above), mirroring how `UseEntityFrameworkUnitOfWork` pairs with `AddTrellisInterceptors`.

## OutboxMessage

A persisted event awaiting relay — one row per captured domain event or translated integration event. Read-only to application code (private constructor, internal mutators).

| Member | Type | Notes |
|---|---|---|
| `Sequence` | `long` | Database-generated, monotonic. Primary key and relay order (ascending). |
| `Id` | `Guid` | UUIDv7. Stable message identity for consumer-side idempotency / de-duplication. |
| `Kind` | `OutboxMessageKind` | `Domain` (captured from an aggregate) or `Integration` (translated). Routes the relay to the correct publisher. Stored as a string column. |
| `OccurredAt` | `DateTimeOffset` | Copied from the event's `OccurredAt`. |
| `EventType` | `string` | Assembly-qualified name of the concrete event type, used to rehydrate the payload. |
| `Payload` | `string` | The JSON-serialized event. |
| `ProcessedAt` | `DateTimeOffset?` | When the message was relayed; `null` while pending. |
| `Attempts` | `int` | Relay attempts so far. |
| `LastError` | `string?` | Most recent relay error, if any. |
| `LockedUntil` | `DateTime?` | UTC instant until which a relay drain holds an exclusive claim (lease) on the row; `null` when unclaimed. |
| `LockedBy` | `Guid?` | Claim token of the relay drain that currently holds the row; `null` when unclaimed. |

The `OutboxMessageConfiguration` maps the table `TrellisOutboxMessages`, the `Sequence` primary key (`ValueGeneratedOnAdd`), a unique index on `Id`, the `Kind` discriminator (string, max length 32), a covering index on `{ ProcessedAt, LockedUntil, Sequence }` for the relay's claimable-rows scan, and an index on `LockedBy` for loading a drain's just-claimed batch.

> **Schema migration.** Row-claiming added the nullable `LockedUntil` and `LockedBy` columns. An existing outbox table needs a migration to add them; both are nullable, so in-flight rows default to unclaimed and are picked up normally.

`OutboxMessage` is an infrastructure record, not a domain aggregate. The rows are transient and may be pruned once `ProcessedAt` is set — deleting processed rows loses no source-of-truth state. This is an outbox, **not** an event store.

## OutboxOptions

Tuning for the relay.

| Property | Type | Default | Notes |
|---|---|---|---|
| `PollInterval` | `TimeSpan` | 5 seconds | How long the relay waits before polling again when the outbox is empty. |
| `BatchSize` | `int` | 100 | Maximum messages drained per poll. |
| `MaxAttempts` | `int` | 10 | After this many failed attempts a message is dead-lettered (parked): left unprocessed and skipped by the scan so it does not block later messages. With the exponential backoff below this is roughly a few hours of retrying at the defaults. Replay dead-lettered messages with `IOutboxMaintenance` once the cause is fixed. Because the dead-letter set is defined relative to this value, **raising it makes previously dead-lettered rows eligible again**. Must be greater than zero. |
| `LeaseDuration` | `TimeSpan` | 5 minutes | How long a relay drain holds an exclusive claim (lease) on the rows it drains before another instance may reclaim them. Set comfortably above the time to publish one batch so a slow batch is not reclaimed mid-flight; a crashed instance's rows become reclaimable once the lease expires. Must be greater than zero. |
| `RetryBackoff` | `TimeSpan` | 30 seconds | Base delay before retrying a failed message. The wait after the *n*th failed attempt is `RetryBackoff × 2^(n-1)`, capped at `MaxRetryBackoff`, so a transient failure (a brief outage) is retried with growing spacing instead of in a tight claim/fail/reclaim loop that would hammer the database. Must be greater than zero. |
| `MaxRetryBackoff` | `TimeSpan` | 1 hour | Ceiling on the exponential retry backoff, so a persistently failing message keeps retrying at a steady, bounded cadence rather than spacing out into many hours. Must be greater than or equal to `RetryBackoff`. |
| `RetryBackoffJitter` | `double` | 0.5 | Fraction in `[0, 1]` of deterministic, per-message jitter that only **subtracts** from the computed backoff (so the wait stays at or below the cap) to de-correlate messages that failed together — they don't all retry the instant a failed dependency recovers, which would flood it. `0` disables jitter; the jitter is keyed on the message id, so it is reproducible (no run-to-run randomness). Must be between 0 and 1 inclusive. |

## Delivery semantics

The guarantee is at-least-once **delivery**, not handler success.

- A message is marked processed once it has been handed to `IDomainEventPublisher`. Per the `IDomainEventHandler<TEvent>` contract the publisher logs and swallows non-cancellation handler exceptions, so a **failing handler does not cause the message to retry**.
- Only infrastructure failures — the event type cannot be resolved, the payload cannot be deserialized, or the relay's own `SaveChanges` fails — leave a message pending. Those retry on later polls with an **exponential backoff** (`RetryBackoff` doubling up to `MaxRetryBackoff`, minus a deterministic per-message jitter), up to `MaxAttempts`, after which the message is dead-lettered (parked): its `Attempts` reaches the cap and the scan skips it, so later messages are not blocked. `LastError` records the most recent failure. The backoff means a brief outage is ridden out by a later retry rather than burning every attempt in a tight loop.
- A dead-lettered message stays in the table for inspection and **replay** — see *Dead-letter and replay* below. Monitor for rows where `ProcessedAt IS NULL AND Attempts >= MaxAttempts`.
- **Logs (production support).** The relay emits structured events. `OutboxRelay.MessageParked` (**Error**, `EventId` 5) is the alertable signal that a message exhausted `MaxAttempts` and is dead-lettered — it carries `MessageId`, `EventType`, `Attempts`, and the exception. Transient per-message failures log `OutboxRelay.RelayAttemptFailed` (**Warning**, `EventId` 4, with the attempt number); a drain that outlived its lease and was reclaimed by another instance logs `OutboxRelay.LeaseLost` (**Warning**, `EventId` 6); a whole drain cycle failing logs `OutboxRelay.DrainFailed` (**Error**, `EventId` 3); startup logs `OutboxRelay.Started` (Information), and `OutboxRelay.DrainCompleted` (Debug) reports the per-cycle count. Alert on `MessageParked`.
- **Safe to run N-up (row-claiming + concurrency guard).** Each drain atomically claims a batch with a lease — a single `UPDATE` sets `LockedBy` (a per-drain token) and `LockedUntil` (now + `LeaseDuration`) on eligible rows. Concurrent relays running that `UPDATE` are serialized by row locks and re-evaluate the lease guard, so every row is claimed by exactly one instance. `LockedBy` is additionally an **optimistic concurrency token**: if a slow batch outlives its lease and another instance reclaims a row, the first instance's bookkeeping `UPDATE` matches no row and it abandons its write for that row (dropping any integration rows it produced and logging `OutboxRelay.LeaseLost`) rather than clobber the new owner. This makes a horizontally-scaled deployment safe **without** leader election or an external lock. Delivery is still at-least-once and handlers must stay idempotent: a crash between publish and the relay's `SaveChanges`, or a batch that outlives its lease, can re-deliver. Set `LeaseDuration` comfortably above the worst-case batch publish time, and keep node clocks reasonably in sync — the lease is compared against each relay's wall clock.

Retry-until-handlers-succeed would require a non-swallowing publish path and is a planned follow-up; today, handlers that must not silently drop work should surface failures through their own durable mechanism.

## Dead-letter and replay

A message that exhausts `MaxAttempts` is **dead-lettered** (parked): it stays in `TrellisOutboxMessages` with `ProcessedAt == null` and `Attempts >= MaxAttempts`, is skipped by the relay scan so it does not block later messages, and logs `OutboxRelay.MessageParked` (Error). It is kept in the table for inspection — a soft dead-letter, not a separate queue.

Resolve `IOutboxMaintenance` (registered by `AddTrellisOutbox<TContext>`, scoped) to inspect and replay dead-lettered messages once you have fixed the cause (deployed the missing handler assembly, corrected a bad payload):

| Member | Behavior |
|---|---|
| `GetDeadLetteredAsync(limit = 100, ct)` | Returns up to `limit` dead-lettered rows, oldest first, for inspection. |
| `ReplayAsync(id, ct)` | Replays one message by `Id`: resets `Attempts` to 0, clears `LastError` and the lease so the relay drains it again. Returns the rows affected (0 if it does not exist or is not dead-lettered, 1 otherwise). |
| `ReplayAllAsync(ct)` | Replays every currently dead-lettered message and returns the count. The set is a snapshot taken when the statement runs — messages that dead-letter afterward are not included. |

Replaying a dead-lettered row is race-free against a running relay because the relay never claims a row whose `Attempts >= MaxAttempts`. Note that the dead-letter set is defined relative to `MaxAttempts`, so lowering or raising that option also changes which rows are considered dead-lettered. `IOutboxMaintenance` targets the single outbox context registered in the container (like `OutboxOptions`); run one outbox per composition.

## Serialization

Events are serialized and rehydrated with a Trellis-owned `System.Text.Json` options instance that adds a `Maybe<T>` converter.

- **Supported:** value objects that carry a `[JsonConverter]` attribute (the Trellis scalar and composite primitives) round-trip, because the converter travels with the type. `Maybe<T>` members also round-trip — a present value serializes as the underlying value and an absent one as JSON `null`.
- **Not supported:** converters registered only through a caller-supplied `JsonSerializerOptions` factory (they are not consulted by the outbox serializer). Shape those members with attribute-driven value objects or a **nullable transport** (e.g. `string?`).

A fully configurable serializer is a planned follow-up; until then, keep event payloads to attribute-driven, `Maybe<T>`, and primitive-or-nullable members.
