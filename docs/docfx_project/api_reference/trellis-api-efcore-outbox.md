---
package: Trellis.EntityFrameworkCore.Outbox
namespaces: [Trellis.EntityFrameworkCore]
types: [OutboxMessage, OutboxMessageConfiguration, OutboxMessageKind, OutboxOptions, OutboxServiceCollectionExtensions, OutboxModelBuilderExtensions, OutboxReadinessOptions, OutboxRelayMonitoring, ConsumerLagMeasures, ConsumerLagEvidence, IConsumerLagProvider, OutboxReadinessServiceCollectionExtensions]
version: v1
last_verified: 2026-10-14
audience: [llm]
agent_usage: onDemand
agent_description: "Open when configuring transactional outbox delivery, retry/parking, durable capture age, application-provided consumer lag, or opt-in readiness."
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
- You are enabling bounded outbox readiness, durable capture age, or application-provided consumer/source lag.

## Patterns Index

| Goal | Use this | See |
|---|---|---|
| Capture an aggregate's domain events into the outbox table atomically with the aggregate write | `optionsBuilder.AddTrellisOutboxInterceptor()` (capture) + `modelBuilder.AddTrellisOutbox()` (table) | [Wiring: three required calls](#wiring-three-required-calls), [`OutboxModelBuilderExtensions`](#outboxmodelbuilderextensions) |
| Run the background relay that re-dispatches captured events after commit | `services.AddTrellisOutbox<TContext>(configure?)` or `trellis.UseOutbox<TContext>(configure?)` | [`OutboxServiceCollectionExtensions`](#outboxservicecollectionextensions), [`UseOutbox` builder slot](#useoutbox-builder-slot) |
| Tune poll interval, batch size, or max attempts | `OutboxOptions` (via the `configure` delegate) | [`OutboxOptions`](#outboxoptions) |
| Inspect a captured-but-not-yet-relayed message | Query `TContext.Set<OutboxMessage>()` (read-only; rows are produced by the interceptor) | [`OutboxMessage`](#outboxmessage) |
| Understand what happens when a domain handler throws | The domain row stays pending and retries **only the failed handlers**, then parks after `MaxAttempts` | [Delivery semantics](#delivery-semantics) |
| Distinguish a throwing integration consumer from a throwing publisher | Default local consumer failures are swallowed and process the row; exposed publication failures keep it pending | [Delivery semantics](#delivery-semantics) |
| Decide how to shape an event so it round-trips | Use attribute-driven value objects; `Maybe<T>` members are supported (present → value, absent → `null`) | [Serialization](#serialization) |
| Publish a stable external contract instead of raw domain events | Translate a domain event into an `IIntegrationEvent` via `IIntegrationEventCollector`; the relay routes it to `IIntegrationEventPublisher` | [Integration events](#integration-events) |
| Enable readiness without confusing queue age, worker responsiveness, delivery progress, and consumer lag | `AddTrellisOutboxReadiness<TContext>(profile)` or `UseOutboxReadiness<TContext>(profile)`; durable capture age and provider-backed lag are separate profile opt-ins | [Outbox readiness](#outbox-readiness), [Durable capture age and rollout](#durable-capture-age-and-rollout), [`IConsumerLagProvider`](#iconsumerlagprovider-and-consumerlagevidence) |
| Monitor outbox metadata in an API host whose relay runs in a separate worker | `OutboxReadinessOptions.CreateMetadataOnlyProfile(workerMaxAttempts)`; no relay monitoring or retry-limit fallback | [Split API/worker hosts](#split-apiworker-hosts) |

## Common traps

- **The interceptor and the model mapping are *not* optional extras.** `AddTrellisOutbox<TContext>()` only registers the relay. Without `optionsBuilder.AddTrellisOutboxInterceptor()` nothing is captured, and without `modelBuilder.AddTrellisOutbox()` the `TrellisOutboxMessages` table is unmapped. All three calls are required — see [Wiring: three required calls](#wiring-three-required-calls).
- **The relay host must have the producing assemblies loaded.** The relay rehydrates each event from its assembly-qualified `EventType` via `Type.GetType`. If the worker process does not reference the assembly that declares the event, the message fails deserialization and parks after `MaxAttempts`.
- **Handlers must be idempotent.** A crash between dispatch/publication and the relay's bookkeeping `SaveChanges` can repeat work on the next drain. Domain per-handler progress tracking removes the *routine* duplicate (a sibling handler failing) but not successes whose progress was not saved.
- **A failing domain handler can park the domain row.** Reporting domain dispatch exposes handler failures to the relay, so a permanently failing domain handler exhausts `MaxAttempts` and dead-letters. Alert on the `"OutboxRelay.MessageParked"` log event name, and add a migration for the `CompletedHandlers` column — see [Delivery semantics](#delivery-semantics).
- **Default integration fan-out is best-effort, not durable consumer retry.** Ordinary consumer failures and handler-resolution failures are logged and swallowed by the default `IIntegrationEventPublisher`. Normal publisher completion makes the relay process the row; a newly staged row then has `Attempts == 0` and no relay `LastError`. A broker publisher must await acceptance and propagate publication failures for retry. Adding an inbox registration does not reroute this default path.
- **Caller-registered `JsonSerializerOptions` converters do not travel with the payload.** The outbox serializer round-trips `[JsonConverter]`-attributed value objects and `Maybe<T>` members; converters registered only on a caller's own options are not consulted — shape those members with attribute-driven types or nullable transports. See [Serialization](#serialization).
- **`OutboxMessage` is read-only to application code.** Its constructor is private and its mutators are internal; rows are produced exclusively by the capture interceptor and advanced by the relay.
- **Post-commit cancellation does not skip Trellis bookkeeping.** `AggregateETagInterceptor.SavedChangesAsync` synchronizes ETags and `OutboxCaptureInterceptor.SavedChangesAsync` clears captured events without observing a newly canceled token: the write has already committed. Cancellation before or during saving still propagates normally. A custom interceptor that throws after commit can still interrupt later callbacks; do not introduce such a post-commit cancellation check.
- **Persist-on-failure (`FailAfterCommit`) events are dispatched by the outbox.** The capture interceptor cannot see the command `Result`, so it captures events from *every* commit — including the persist-on-failure commits that `TransactionalCommandBehavior` performs. Without the outbox, `DomainEventDispatchBehavior` deliberately does **not** dispatch events for a failed result: the `FailAfterCommit` contract (documented under persist-on-failure in `trellis-api-core.md`) treats those events as discarded, *not* a durable retry buffer. With the outbox enabled, those events are captured and the relay delivers them — so the suppression no longer holds. If you depend on it, do not raise domain events on persist-on-failure paths under the outbox: return a success result for events you want delivered, and model post-failure side effects explicitly (a follow-up command, or a dedicated outbox row).

## How the outbox works

The outbox replaces the in-pipeline domain-event dispatch with a durable, two-phase flow:

1. **Capture (inside the transaction).** `OutboxCaptureInterceptor` (a `SaveChangesInterceptor`) scans the change tracker during `SavingChanges` for `IAggregate` entries with uncommitted events. It serializes each event and adds one `OutboxMessage` row per event to the same `SaveChanges` — so the rows commit atomically with the aggregate. It does **not** clear the aggregate's events yet.
   It also captures the current W3C `Activity` traceparent/tracestate and any explicit
   `IntegrationMessageContext` business correlation and inbound message id. When a valid
   inbound trace exists, the current activity is used only if it belongs to that same
   trace; otherwise the inbound trace is preserved (including when no inbox consumer
   activity was created). Missing values remain null; no request id, trace id, actor,
   or baggage is promoted to business lineage.
2. **Clear (after the commit succeeds).** In `SavedChanges` the interceptor calls each aggregate's `AcceptChanges()`. Because the events are cleared only after a successful commit, a failed save leaves the in-memory events intact for retry, and the interceptor detaches the rows it staged on `SaveChangesFailed` and `SaveChangesCanceled` so a retry on the same context does not double-capture.

> **Interceptor constraint.** No other `SaveChangesInterceptor` may raise domain events on a tracked aggregate between step 1 and step 2. `AcceptChanges()` empties an aggregate's whole event list, so an event raised in that window is discarded without ever reaching the outbox. No Trellis interceptor raises domain events, so this applies only to caller-supplied interceptors — raise events from the domain model before `SaveChanges` is called instead.
3. **Single dispatch path.** Since the aggregate's events are cleared after the commit (in `SavedChanges`), a post-commit in-pipeline `DomainEventDispatchBehavior` observes an empty list and dispatches nothing. The relay becomes the one dispatcher.
4. **Relay (after the commit).** `OutboxRelay<TContext>` is a `BackgroundService`. Each poll it opens a bookkeeping scope, drains a batch of pending rows ordered by `Sequence`, and routes each by `OutboxMessage.Kind`. A `Domain` row is published through `IReportingDomainEventPublisher` **in a dedicated per-message scope**: handlers receive their own context, never the bookkeeping context, and their tracked changes do not ride its save. An `Integration` row is published through `IIntegrationEventPublisher`. The relay saves processed/failed state, completed-handler progress, and translated rows together on the bookkeeping context.

## Integration events

A **domain event** is internal to the bounded context; an **integration event** (`IIntegrationEvent`) is the stable, versioned contract published to other services. The outbox keeps them separate: domain events are captured from aggregates, and integration events are *translated* from them so external consumers never couple to your internal model. See `trellis-api-mediator.md` for the `IIntegrationEvent*` types.

The flow:

1. A **translator** — an ordinary `IDomainEventHandler<TDomainEvent>` — injects `IIntegrationEventCollector` and `Add(...)`s integration events while the relay re-dispatches the domain event.
2. The relay opens the collector's `BeginTranslation()` lease only around publishing a `Domain` row and draining the per-message collector. Every drained event is staged as a new `OutboxMessageKind.Integration` row in the same bookkeeping save as handler progress, even if a sibling handler failed and the source domain row remains pending. Events added before a translator itself throws are also drained; there is no per-handler rollback of collector additions.
   Each new row inherits the source domain row's persisted trace context, correlation id,
   and producer namespace; its `CausationId` is the source domain row's `Id` (not the
   inbound integration message id).
3. A later drain publishes each `Integration` row through `IIntegrationEventPublisher` — its sole method `PublishAsync(OutboundIntegrationMessage, CancellationToken)`, carrying that row's own `OutboxMessage.Id` (default **best-effort** in-process fan-out to `IIntegrationEventHandler<T>`; replace the registration with a message-broker adapter to deliver to other services).

Register the consumer side with `services.AddIntegrationEventDispatch(...)` / `AddIntegrationEventHandler<TEvent, THandler>()`, or the `TrellisServiceBuilder.UseIntegrationEvents(...)` slot. The collector is optional: outboxes that capture only domain events never register it and are unaffected.

**Publication is one publisher handoff, not a per-consumer report.** The relay processes an integration row after normal publisher completion. The default local publisher can therefore process a row even when a consumer threw; those swallowed failures are not retried. A broker adapter must await acceptance and expose publication failures, while downstream processing remains a separate responsibility. For transactional consumption, an adapter must call the [non-swallowing inbox](trellis-api-efcore-inbox.md#processing-semantics) with the stable message id and expose failures for transport redelivery. Registering `UseInbox` alone does not route local publication through it.

A retry skips translators already recorded in `CompletedHandlers`; an unrelated sibling failure does not rerun them. A translator that failed after adding an event, or whose successful progress was not saved (crash/bookkeeping failure), can run again and produce a distinct integration row with a new ID. Deduplicate these logical duplicates on business identity, in addition to inbox message-ID deduplication.

### Two different duplicates — only one is the message id's job

These are easy to conflate, and they need different defences:

| Duplicate | Cause | `OutboxMessage.Id` | Defence |
| --- | --- | --- | --- |
| **Redelivery of one `Integration` row** | The relay crashed between publishing and its bookkeeping `SaveChanges`, or the batch outlived its lease | **Same** on every attempt | The consumer's inbox, keyed `(ConsumerId, MessageId)` — provided the transport carried `OutboundIntegrationMessage.MessageId` verbatim |
| **A re-run translator staging a fresh row** | The translator itself failed after adding events, or its successful progress was not durably saved; completed translators are skipped on ordinary sibling retries | **Different** each time | Business identity — the inbox cannot help, because these genuinely are two messages |

The relay hands `OutboundIntegrationMessage.MessageId` to the publisher precisely so a broker adapter can stamp it on the wire (for example `ServiceBusMessage.MessageId`) and collapse the first row of the table. An adapter that minted its own id per publish attempt would turn every redelivery into a distinct message and silently defeat the consumer's inbox. It does **not** collapse the second row, which is why the "dedupe on business identity" guidance above still stands.

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
- `TimeProvider` is added with `TryAdd`. A single shared `OutboxOptions` instance backs all relays in the container. Configure the outbox for one `DbContext` per composition (the `UseOutbox` slot enforces this).
- **Repeated calls accumulate configuration.** `AddHostedService` dedupes by service + implementation type, so calling this twice for the same `TContext` yields exactly one relay. Each call applies its `configure` callback on top of the already-registered `OutboxOptions` instance and re-runs `Validate()`, so a later callback wins per setting rather than being silently discarded. Configuration is applied to a clone and committed only after `Validate()` succeeds, so a rejected callback leaves the container's options untouched. Prefer the `UseOutbox<TContext>()` builder slot, which fails fast on a duplicate.
- **Do not register `OutboxOptions` yourself via a factory or implementation type.** The helper layers onto the *last* `OutboxOptions` descriptor, which is the one the container resolves. If that descriptor is a factory or type registration it cannot be cloned, so passing a `configure` callback throws `InvalidOperationException` rather than silently configuring an instance the relay would never receive. Calling `AddTrellisOutbox<TContext>()` with no callback leaves your registration intact.

This is the service-collection half only. Pair with `AddTrellisOutbox(ModelBuilder)` and `AddTrellisOutboxInterceptor(DbContextOptionsBuilder)`.

## OutboxModelBuilderExtensions

Static class. EF Core model + interceptor hooks.

```csharp
public static ModelBuilder AddTrellisOutbox(this ModelBuilder modelBuilder);

public static DbContextOptionsBuilder AddTrellisOutboxInterceptor(
    this DbContextOptionsBuilder optionsBuilder);

public static DbContextOptionsBuilder AddTrellisOutboxInterceptor(
    this DbContextOptionsBuilder optionsBuilder,
    TimeProvider timeProvider);

public static DbContextOptionsBuilder<TContext> AddTrellisOutboxInterceptor<TContext>(
    this DbContextOptionsBuilder<TContext> optionsBuilder)
    where TContext : DbContext;

public static DbContextOptionsBuilder<TContext> AddTrellisOutboxInterceptor<TContext>(
    this DbContextOptionsBuilder<TContext> optionsBuilder,
    TimeProvider timeProvider)
    where TContext : DbContext;
```

- `AddTrellisOutbox(ModelBuilder)` — applies `OutboxMessageConfiguration`, mapping the `TrellisOutboxMessages` table. Call from `OnModelCreating`.
- `AddTrellisOutboxInterceptor(...)` — adds the capture interceptor. The no-clock overload uses `TimeProvider.System`; the clock overload records deterministic UTC capture evidence and rejects a repeated call on the same builder with a different clock instance. Same-clock repeated calls are idempotent. The generic overload preserves the `DbContextOptionsBuilder<TContext>` fluent type so it chains after `AddTrellisInterceptors<TContext>()`.

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
| `CapturedAt` | `DateTime?` | UTC instant when this row was first captured/staged. Nullable for rollout/legacy rows written before capture-aware writers were fully deployed; never inferred from `OccurredAt`. |
| `EventType` | `string` | Assembly-qualified name of the concrete event type, used to rehydrate the payload. |
| `Payload` | `string` | The JSON-serialized event. |
| `MessageSource` | `string?` | Optional producing-service name from `IntegrationMessageContext.BeginCorrelation`. |
| `CausationId` | `Guid?` | Inbound `MessageId` for a domain row; source domain outbox row `Id` for a translated integration row. |
| `CorrelationId` | `string?` | Application-owned opaque business workflow id; never inferred from a trace or request id. |
| `TraceParent` | `string?` | W3C traceparent captured with the original transaction and inherited by translations. |
| `TraceState` | `string?` | The matching W3C tracestate, when available. |
| `ProcessedAt` | `DateTimeOffset?` | When reporting domain dispatch completed or the integration publisher returned normally; `null` while pending. Integration publication is not proof of downstream consumer success. |
| `Attempts` | `int` | Persisted failed relay attempts. Successful publication neither increments nor resets the count. A new integration row processed after a swallowed local-consumer failure still has `0`. |
| `LastError` | `string?` | Most recent persisted relay failure; cleared when the row is processed. Swallowed local-consumer failures never become relay errors. |
| `LockedUntil` | `DateTime?` | UTC claim expiry while publishing, or retry-not-before time after a failed attempt. `LockedBy` is null during backoff; `null` here means no lease/backoff. |
| `LockedBy` | `Guid?` | Claim token of the relay drain that currently holds the row; `null` when unclaimed. |
| `CompletedHandlers` | `IReadOnlyList<string>` | For domain rows, the identity of every handler that has already completed for this event (see `DomainEventDispatchReport.HandlerIdentity`). A retry skips them, so a successful handler is never re-run because a sibling failed. Deliberately **not** cleared by `ReplayAsync` — replaying means "finish the work that did not happen". Empty for integration rows. |

The `OutboxMessageConfiguration` maps the table `TrellisOutboxMessages`, the `Sequence` primary key (`ValueGeneratedOnAdd`), a unique index on `Id`, the `Kind` discriminator (string, max length 32), a covering index on `{ ProcessedAt, LockedUntil, Sequence }` for the relay's claimable-rows scan, `{ ProcessedAt, CapturedAt, Sequence }` for capture-age evidence, and an index on `LockedBy` for loading a drain's just-claimed batch.

> **Schema migration.** Row-claiming added the nullable `LockedUntil` and `LockedBy` columns. An existing outbox table needs a migration to add them; both are nullable, so in-flight rows default to unclaimed and are picked up normally.

> **Alpha schema change.** The `TrellisOutboxMessages` mapping now includes nullable
> `MessageSource`, `CausationId`, `CorrelationId`, `TraceParent`, and `TraceState` columns.
> No migration or backfill is provided; rows with null metadata publish without it,
> never synthesizing lineage from a relay's ambient activity. The relay starts activities
> from valid persisted W3C context under the `Trellis.EntityFrameworkCore.Outbox` source; opt in with
> `AddSource("Trellis.EntityFrameworkCore.Outbox")` on the application's tracer provider.

> **Capture-age schema change.** Add nullable UTC `CapturedAt` plus the
> `{ ProcessedAt, CapturedAt, Sequence }` index in an application-owned migration.
> Existing rows deliberately remain null (unknown age). Do not backfill from `OccurredAt`,
> UUIDv7, `Sequence`, `LockedUntil`, or migration time.

`OutboxMessage` is an infrastructure record, not a domain aggregate. The rows are transient and may be pruned once `ProcessedAt` is set — deleting processed rows loses no source-of-truth state. This is an outbox, **not** an event store.

### `OutboxMessageConfiguration`

Public `IEntityTypeConfiguration<OutboxMessage>` implementation, normally applied by `AddTrellisOutbox(ModelBuilder)`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public void Configure(EntityTypeBuilder<OutboxMessage> builder)` | `void` | Applies the outbox table mapping described above, including the newline-delimited `CompletedHandlers` conversion and optimistic-concurrency token on `LockedBy`. Throws `ArgumentNullException` for a null builder. |

## OutboxOptions

Tuning for the relay.

| Property | Type | Default | Notes |
|---|---|---|---|
| `PollInterval` | `TimeSpan` | 5 seconds | How long the relay waits before polling again when the outbox is empty. Must be positive, with a truncated whole-millisecond value no greater than `uint.MaxValue - 1`, matching the supported timer range. |
| `BatchSize` | `int` | 100 | Maximum messages drained per poll. |
| `MaxAttempts` | `int` | 10 | After this many failed attempts a message is dead-lettered (parked): left unprocessed and skipped by the scan so it does not block later messages. With the exponential backoff below this is roughly a few hours of retrying at the defaults. Replay dead-lettered messages with `IOutboxMaintenance` once the cause is fixed. Because the dead-letter set is defined relative to this value, **raising it makes previously dead-lettered rows eligible again**. Must be greater than zero. |
| `LeaseDuration` | `TimeSpan` | 5 minutes | How long a relay drain holds an exclusive claim (lease) on the rows it drains before another instance may reclaim them. Set comfortably above the time to publish one batch so a slow batch is not reclaimed mid-flight; a crashed instance's rows become reclaimable once the lease expires. Must be greater than zero. |
| `RetryBackoff` | `TimeSpan` | 30 seconds | Base delay before retrying a failed message. The wait after the *n*th failed attempt is `RetryBackoff × 2^(n-1)`, capped at `MaxRetryBackoff`, so a transient failure (a brief outage) is retried with growing spacing instead of in a tight claim/fail/reclaim loop that would hammer the database. Must be greater than zero. |
| `MaxRetryBackoff` | `TimeSpan` | 1 hour | Ceiling on the exponential retry backoff, so a persistently failing message keeps retrying at a steady, bounded cadence rather than spacing out into many hours. Must be greater than or equal to `RetryBackoff`. |
| `RetryBackoffJitter` | `double` | 0.5 | Fraction in `[0, 1]` of deterministic, per-message jitter that only **subtracts** from the computed backoff (so the wait stays at or below the cap) to de-correlate messages that failed together — they don't all retry the instant a failed dependency recovers, which would flood it. `0` disables jitter; the jitter is keyed on the message id, so it is reproducible (no run-to-run randomness). Must be between 0 and 1 inclusive. |

## Delivery semantics

**Domain-handler delivery, integration publication, and downstream consumption are different boundaries.** "Every handler completed" applies to reporting **domain rows**, not to every integration consumer. The relay durably retries failures it can observe, up to `MaxAttempts`.

- A domain message is marked processed only once **every** registered `IDomainEventHandler<TEvent>` has completed. A handler that throws leaves the message pending, and the retry re-invokes **only the handlers that failed** — `OutboxMessage.CompletedHandlers` records the ones that already succeeded. An unrelated sibling's failure therefore never re-runs a successful handler's side effect.
- All drained integration events are staged on a failed attempt, including additions from successful handlers and additions made before a translator itself threw. There is no per-handler rollback of collector additions. Successful handlers are skipped on retry; failed translators can run again and produce logical duplicates under new row IDs.
- Event-type resolution, deserialization, and publisher exceptions become per-message failed attempts when their bookkeeping save succeeds. `OperationCanceledException` aborts the drain only when the relay's supplied cancellation token is canceled; an internal publisher timeout/cancellation while that token remains active is an ordinary failed attempt, so it receives backoff and can eventually park. A bookkeeping `SaveChanges` failure is different: it escapes to the drain loop, which logs `DrainFailed` and waits `PollInterval`. That attempt/progress is not durable, the prior lease remains until expiry, and repeated bookkeeping failures need not ever reach `MaxAttempts`. Monitor drain failures as well as parked messages.
- Failures retry on later polls with an **exponential backoff** (`RetryBackoff` doubling up to `MaxRetryBackoff`, minus a deterministic per-message jitter), up to `MaxAttempts`, after which the message is dead-lettered (parked): its `Attempts` reaches the cap and the scan skips it, so later messages are not blocked. `LastError` records the most recent failure. The backoff means a brief outage is ridden out by a later retry rather than burning every attempt in a tight loop. With the defaults, parking follows 10 failed attempts; the nine intervening backoffs total roughly 1.5-3 hours with jitter (about 3.06 hours without jitter), excluding polling, publishing, and database time.
- **Domain handlers must still be idempotent.** The progress record is only durable once the relay's bookkeeping `SaveChanges` lands, so a crash can repeat handlers whose successful progress was not saved. Per-handler tracking removes the routine duplicate (a failing sibling), not that crash window.
- **Default local integration notification is best-effort.** An integration row is marked processed after `IIntegrationEventPublisher.PublishAsync` returns normally. The default publisher logs and swallows ordinary consumer failures and handler-resolution failures, so a newly staged row becomes processed with `Attempts == 0` and `LastError == null`. No per-consumer outcome is reported; `CompletedHandlers` stays empty, and later drains do not retry the processed row. A handler's `OperationCanceledException` propagates when the supplied token is canceled.
- **Broker publication completes on acceptance, not consumption.** A broker adapter must await acceptance and propagate publication failures. An exposed failure while the relay token remains active leaves the row pending with an incremented `Attempts`, retained `LastError`, and scheduled backoff; retry uses the same row id and lineage. Successful publication clears `LastError` and the lease but retains the failure count. A crash before bookkeeping can publish that row again. Downstream consumers need their own processing/idempotency boundary, such as a transport adapter calling the non-swallowing inbox; merely registering an inbox is not routing.
- A dead-lettered message stays in the table for inspection and **replay** — see *Dead-letter and replay* below. Monitor for rows where `ProcessedAt IS NULL AND Attempts >= MaxAttempts`.
- **Logs (production support).** The relay emits structured events with stable `EventId.Name` strings. `"OutboxRelay.MessageParked"` (**Error**, `EventId` 5) is the alertable signal that a message exhausted `MaxAttempts` and is dead-lettered — it carries `MessageId`, `EventType`, `Attempts`, and the exception. Transient per-message failures log `"OutboxRelay.RelayAttemptFailed"` (**Warning**, `EventId` 4, with the attempt number); a drain that outlived its lease and was reclaimed by another instance logs `"OutboxRelay.LeaseLost"` (**Warning**, `EventId` 6); a whole drain cycle failing logs `"OutboxRelay.DrainFailed"` (**Error**, `EventId` 3); startup logs `"OutboxRelay.Started"` (Information), and `"OutboxRelay.DrainCompleted"` (Debug) reports the per-cycle count. Alert on `MessageParked`.
- **Safe to run N-up (row-claiming + concurrency guard).** Each drain atomically claims a batch with a lease — a single `UPDATE` sets `LockedBy` (a per-drain token) and `LockedUntil` (now + `LeaseDuration`) on eligible rows. Concurrent relays running that `UPDATE` are serialized by row locks and re-evaluate the lease guard, so every row is claimed by exactly one instance. `LockedBy` is additionally an **optimistic concurrency token**: if a slow batch outlives its lease and another instance reclaims a row, the first instance's bookkeeping `UPDATE` matches no row and it abandons its write for that row (dropping any integration rows it produced and logging `"OutboxRelay.LeaseLost"`) rather than clobber the new owner. This makes a horizontally-scaled deployment safe **without** leader election or an external lock. Delivery is still at-least-once and handlers must stay idempotent: a crash between publish and the relay's `SaveChanges`, or a batch that outlives its lease, can re-deliver. Set `LeaseDuration` comfortably above the worst-case batch publish time, and keep node clocks reasonably in sync — the lease is compared against each relay's wall clock.

> **Migration note (domain dispatch only).** Reporting domain dispatch replaced the previous "handed to the publisher = delivered" semantics for **domain rows**, under which a throwing domain handler silently dropped its work. Domain-handler failures now retry and can park (alert on `"OutboxRelay.MessageParked"`), and the `TrellisOutboxMessages` table gains a `CompletedHandlers` column — add a migration before deploying. The column is non-nullable with an empty default, so existing rows backfill cleanly. This does **not** change the default integration publisher's best-effort fan-out or add per-integration-consumer retry.

The relay obtains **domain-row reporting** from `IReportingDomainEventPublisher` (see `trellis-api-mediator.md`), the non-swallowing counterpart to `IDomainEventPublisher`. Default in-pipeline domain dispatch still swallows, because its post-commit callers have no retry mechanism. That rationale does not establish integration-delivery semantics: the integration relay owns retry, but only for failures its publisher exposes. The relay's `StartAsync` resolves `IReportingDomainEventPublisher` in a fresh scope before starting the drain loop and fails host startup if it is missing or cannot be constructed. When an `IIntegrationEventCollector` or `IIntegrationEventPublisher` registration enables integration features, startup also resolves the integration publisher; domain-only hosts need no integration publisher. This validates publisher construction, not broker connectivity or every handler dependency. Both response and tracked domain-dispatch registrations supply the reporting capability.

`IServiceProviderIsService` is optional. When available, the relay probes registrations without constructing the collector. With a provider or scoped-provider wrapper that omits this capability, startup resolves the optional collector to determine whether an integration publisher is required; when there is no collector, it still resolves any optional integration publisher to validate its construction. Optional resolution follows the `IServiceProvider` contract: an absent service returns `null`, while construction exceptions propagate. Each publisher is requested directly at most once during validation, and the scope is asynchronously disposed on success or failure. A provider without registration probing therefore need not register a dummy probe or integration publisher for a domain-only outbox.

## Dead-letter and replay

A message that exhausts `MaxAttempts` is **dead-lettered** (parked): it stays in `TrellisOutboxMessages` with `ProcessedAt == null` and `Attempts >= MaxAttempts`, is skipped by the relay scan so it does not block later messages, and logs `"OutboxRelay.MessageParked"` (Error). It is kept in the table for inspection — a soft dead-letter, not a separate queue.

Resolve `IOutboxMaintenance` (registered by `AddTrellisOutbox<TContext>`, scoped) to inspect and replay dead-lettered messages once you have fixed the cause (deployed the missing handler assembly, corrected a bad payload):

| Member | Behavior |
|---|---|
| `GetDeadLetteredAsync(limit = 100, ct)` | Returns up to `limit` dead-lettered rows, oldest first, for inspection. |
| `ReplayAsync(id, ct)` | Replays one message by `Id`: resets `Attempts` to 0, clears `LastError` and the lease so the relay drains it again. Returns the rows affected (0 if it does not exist or is not dead-lettered, 1 otherwise). |
| `ReplayAllAsync(ct)` | Replays every currently dead-lettered message and returns the count. The set is a snapshot taken when the statement runs — messages that dead-letter afterward are not included. |

Replaying a dead-lettered row is race-free against a running relay because the relay never claims a row whose `Attempts >= MaxAttempts`. Note that the dead-letter set is defined relative to `MaxAttempts`, so lowering or raising that option also changes which rows are considered dead-lettered. `IOutboxMaintenance` targets the single outbox context registered in the container (like `OutboxOptions`); run one outbox per composition.

## Outbox readiness

**All three staged signals are implemented.** The named `trellis-outbox` .NET health
check reports bounded queue metadata, optional durable capture age, explicitly selected
local relay observations, and optional application-provided consumer/source lag.
`UseOutbox` and `AddTrellisOutbox` still do **not** enable health checks. There is no
endpoint, liveness restart policy, or standalone public snapshot/query API; diagnostics
are returned in `HealthCheckResult.Data`.

### `OutboxReadinessServiceCollectionExtensions`

```csharp
public static IServiceCollection AddTrellisOutboxReadiness<TContext>(
    this IServiceCollection services, OutboxReadinessOptions profile)
    where TContext : DbContext;
```

The explicit profile is required. Local-relay mode requires the matching relay separately
with `AddTrellisOutbox<TContext>` or `UseOutbox<TContext>`; metadata-only mode requires none.
Equal profiles for the same context deduplicate direct and builder registration, including
the startup validator; conflicting readiness contexts or profiles throw.
Relay/readiness context mismatches throw in local mode in either registration order.
Metadata-only readiness for context B may coexist with a relay for context A.
Registration may precede or follow relay/unit-of-work registration without changing the
Mediator pipeline.

**Generic Host startup guarantee:** an `IHostedLifecycleService.StartingAsync` validator
checks configuration before any hosted-service `StartAsync`. Local mode requires an actual
matching relay instance among the hosted services and resolvable local options/observations.
Removing the relay, or decorating it so its actual instance is hidden, fails startup with
an actionable exception; a marker or unrelated relay does not satisfy this requirement.
Both modes check context registration through `IServiceProviderIsService`, without
constructing the context or querying the database. A lag-enabled profile also requires
an `IConsumerLagProvider` descriptor; startup checks only registration presence and does
not construct it. Containers without registration-probing capability skip only these
prechecks, without a warning or construction fallback; missing dependencies still fail
when the health check runs.

Bare service providers and hosts that do not invoke lifecycle hooks do not get this
startup validation. A resolvable check with an unstarted local relay is Degraded with
`relay-not-started`; no start timestamp or positive observation is fabricated.
Dependency/provider construction failures remain failures, not healthy fallbacks.

The matching `TrellisServiceBuilder.UseOutboxReadiness<TContext>(profile)` slot is
single-selection and applied after outbox registration. It is annotated non-AOT, like
`UseOutbox`. The host selects `trellis-outbox` for readiness or alert-only exposure and
authorizes detailed output. The registration's tags are `trellis` and `outbox`, not an
automatic readiness/liveness endpoint policy.

```csharp
var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();
services.AddTrellis(trellis => trellis
    .UseDomainEvents()
    .UseOutbox<AppDbContext>()
    .UseOutboxReadiness<AppDbContext>(profile));
```

Capture age is deliberately disabled in both factories so the schema can roll out first.
After applying the nullable `CapturedAt` migration to every queried store, enable it in
either local or metadata-only mode:

```csharp
var profile = OutboxReadinessOptions.CreateStagedOperationalProfile() with
{
    CaptureAgeEnabled = true,
};
```

Consumer lag is independently disabled by default. Register an application/transport
adapter directly and select age, count, or both:

```csharp
services.AddScoped<IConsumerLagProvider, OrdersConsumerLagProvider>();
var profile = OutboxReadinessOptions.CreateStagedOperationalProfile() with
{
    ConsumerLagMeasures = ConsumerLagMeasures.Age | ConsumerLagMeasures.Count,
};
```

Use one fixed outbox store per composition. The context is resolved in a fresh background
scope without the initiating caller's ambient execution context, matching the relay's
storage scope. A request-local tenant/connection selector is not a cache partition;
use separate compositions for separate stores. Application-owned provider registration,
table mapping, capture interception, and schema migrations remain unchanged.

### Split API/worker hosts

The worker selects the local-relay profile. An API host can explicitly select
metadata-only readiness against the same fixed store without registering a relay,
domain-event publisher, or local outbox options:

```csharp
const int workerMaxAttempts = 10; // Use the same configuration value in both hosts.
var profile = OutboxReadinessOptions.CreateMetadataOnlyProfile(workerMaxAttempts);
services.AddTrellis(trellis => trellis.UseOutboxReadiness<AppDbContext>(profile));
```

`WorkerMaxAttempts` must equal the worker's `OutboxOptions.MaxAttempts`. There is no
default, cross-process verification, or fallback to incidental local options. A higher
limit can hide parked rows; a lower one can classify still-retryable rows as parked.
Derive both values from one configuration key or constant. Metadata-only never resolves
or compares local `OutboxOptions`, even if registered with a different value or a throwing
factory, so such mismatches produce no warning.
Metadata-only registers no relay observation store; an explicitly registered store is
left intact and is not resolved or read by the health check.

**Weaker guarantee:** metadata-only does not observe remote worker responsiveness.
With capture age disabled, worker inactivity alone raises no signal until pending depth
reaches 1,000 or a row is parked. With capture age enabled, an aging pending row can
breach the queue-age SLO, but that is not proof the worker is dead: it can be alive and
retrying correctly. Probe failures, stale evidence, and invalid clocks remain independent
health conditions. `relayMonitoring = "disabled"` is configuration, not a healthy relay
observation; a Healthy description says "Outbox metadata is healthy."

### `ConsumerLagMeasures`

`[Flags] public enum ConsumerLagMeasures`

| Value | Meaning |
|---|---|
| `None` | Consumer/source lag is disabled. The health result reports `"disabled"` and does not resolve a provider. |
| `Age` | Require authoritative oldest-unaccounted source-time evidence. A null oldest timestamp means authoritative quiet-source zero lag. |
| `Count` | Require an authoritative nonnegative unaccounted count. The provider can mark it as a conservative lower bound; a lower bound below the degraded threshold cannot prove healthy and therefore degrades. |

`Age | Count` requires both measurements. Unknown enum bits are invalid.

### `IConsumerLagProvider` and `ConsumerLagEvidence`

`public interface IConsumerLagProvider`

| Member | Meaning |
|---|---|
| `ValueTask<ConsumerLagEvidence> GetLagAsync(CancellationToken cancellationToken)` | Reads one non-mutating, conservative lag snapshot. The provider must honor cancellation and preserve its source sample time. |

`public sealed record ConsumerLagEvidence`

```csharp
public ConsumerLagEvidence(
    string consumerId,
    string source,
    string scope,
    DateTimeOffset sampledAt,
    ConsumerLagMeasures measures,
    int requiredPartitionCount,
    int coveredPartitionCount,
    DateTimeOffset? oldestUnaccountedAt,
    long? unaccountedCount,
    bool unaccountedCountIsLowerBound = false);
```

| Property | Meaning |
|---|---|
| `ConsumerId` | Stable logical consumer identity. Do not put secrets here because it is exposed in health data. |
| `Source` | Stable source/subscription identity. Do not put secrets here because it is exposed in health data. |
| `Scope` | Authorized storage/tenant/environment scope. Do not put secrets here because it is exposed in health data. |
| `SampledAt` | Original source sample time. Cache refresh never renews it. |
| `Measures` | Measurements the provider declares authoritative for this snapshot. |
| `RequiredPartitionCount` | Positive count of partitions/shards required for complete evidence. |
| `CoveredPartitionCount` | Number represented in the snapshot, between zero and the required count. |
| `OldestUnaccountedAt` | Oldest durably unaccounted source timestamp for `Age`; null is authoritative zero lag, not unknown. |
| `UnaccountedCount` | Nonnegative aggregate for `Count`. |
| `UnaccountedCountIsLowerBound` | Whether the count is known only as a conservative lower bound. Requires `UnaccountedCount`. A value below the degraded threshold produces `consumer-lag-count-lower-bound`; a lower bound that crosses a threshold uses the ordinary count reason at that severity. |

The constructor rejects null/blank identities, non-UTC timestamps, `None` or unknown
measurement flags, invalid coverage, missing selected count data, unselected measurement
data, negative counts, and a lower-bound flag without a count.

This contract is deliberately aggregated rather than per-partition. The adapter owns
partition enumeration, source-specific positions, gap-aware durable accounting across
out-of-order work and redelivery, and safe retention. It reports the oldest unaccounted
timestamp across covered partitions, sums counts conservatively, keeps the lower-bound
marker if any contribution is incomplete, uses the oldest source sample time, and
reports explicit required/covered counts. Trellis applies freshness and threshold policy
without taking a vendor SDK dependency.

### `OutboxReadinessOptions`

Immutable sealed record with no public constructor. Explicitly select
`CreateStagedOperationalProfile()` or `CreateMetadataOnlyProfile(workerMaxAttempts)`;
tune thresholds and timings with a `with` expression. Mode and retry-policy source
are get-only and cannot be changed by copying. Registration validates the complete
profile, including unused relay timing values in metadata-only mode, before adding services.

| Member | Reference value | Constraint / meaning |
|---|---|---|
| `CreateStagedOperationalProfile()` | New profile | Explicit selection; existing relay calls never select it. |
| `CreateMetadataOnlyProfile(int workerMaxAttempts)` | New metadata-only profile | Requires the worker's positive retry limit; throws `ArgumentOutOfRangeException` for zero/negative `workerMaxAttempts`. Same reference thresholds/timings; no relay dependencies. |
| `RelayMonitoring` | `OutboxRelayMonitoring.LocalRelay` or `OutboxRelayMonitoring.Disabled` | Get-only mode chosen by the factory. Local mode reads the local options; disabled mode reads only the explicit worker retry limit. |
| `WorkerMaxAttempts` | `null` in local mode; explicit positive `int?` in metadata-only | Get-only. Must equal the worker's `OutboxOptions.MaxAttempts`; local mode requires null and does not use it. Included in record equality and registration conflict detection. |
| `PendingDegradedThreshold` | 1,000 | Positive, inclusive pending-depth degradation threshold. |
| `PendingUnhealthyThreshold` | 10,000 | Greater than the degraded threshold; also the pending and unknown-capture-age count cap. |
| `CaptureAgeEnabled` | `false` | Explicit migration-first rollout switch. When false, no capture-age query or claim is made. Included in record equality and registration conflict detection. |
| `CaptureAgeDegradedAfter` | 2 minutes | Positive, inclusive oldest-known capture-age degradation threshold. Validated even while disabled. |
| `CaptureAgeUnhealthyAfter` | 10 minutes | Greater than the degraded age; inclusive oldest-known capture-age unhealthy threshold. Validated even while disabled. |
| `ConsumerLagMeasures` | `ConsumerLagMeasures.None` | Selects disabled, age, count, or both. Any enabled selection requires an `IConsumerLagProvider`. Included in record equality and registration conflict detection. |
| `ConsumerLagAgeDegradedAfter` | 2 minutes | Positive, inclusive oldest-unaccounted-age degradation threshold. Validated even while disabled. |
| `ConsumerLagAgeUnhealthyAfter` | 10 minutes | Greater than the degraded age; inclusive oldest-unaccounted-age unhealthy threshold. Validated even while disabled. |
| `ConsumerLagCountDegradedThreshold` | 1,000 | Positive, inclusive unaccounted-count degradation threshold. Validated even while disabled. |
| `ConsumerLagCountUnhealthyThreshold` | 10,000 | Greater than the degraded count; inclusive unaccounted-count unhealthy threshold. Validated even while disabled. |
| `RelayDegradedAfter` | 1 minute | Positive, inclusive gap since a positive local observation. |
| `RelayUnhealthyAfter` | 5 minutes | Greater than the degraded gap; missing initial evidence uses relay/host startup without resetting at grace expiry. |
| `StartupGracePeriod` | 30 seconds | Nonnegative and shorter than the degraded gap; changes the missing-evidence reason, never its Degraded status. |
| `ProbeTimeout` | 2 seconds | Positive, within the .NET timer range; bounds the entire provider probe, including scope creation/disposal and every enabled query. |
| `CacheDuration` | 10 seconds | Positive and at least the probe timeout; elapsed-time-based completed metadata cache. |
| `EvidenceMaxAge` | 30 seconds | Strictly greater than cache duration plus probe timeout; stale enabled metadata degrades. Rejecting equality conservatively reserves a margin for evaluation latency and clock granularity. |

### `OutboxRelayMonitoring`

| Member | Meaning |
|---|---|
| `LocalRelay` | Require and monitor the relay in this process; another instance cannot supply its evidence. |
| `Disabled` | Evaluate metadata only; neither a local nor a remote relay is observed healthy. |

Each pending or unknown-age count is a server-side count of at most
`PendingUnhealthyThreshold` matching rows; reaching the cap is explicitly a lower bound,
not an exact total. Parked presence is capped at one, also a lower bound when present.
With capture age enabled, a provider-translatable UTC `DateTime` ordering returns only
the oldest known capture instant; the probe does not materialize rows or payloads.
The metadata queries are sampled sequentially, not a promised atomic snapshot.
Every original sample time is independently checked for stale or future-dated evidence.

Concurrent callers share one refresh and cancel only their own waits. The refresh has
its own budget. Context queries and an enabled consumer-lag provider share that complete
budget. A provider that ignores cancellation cannot delay the health result past that
budget or cause overlapping probes: subsequent refreshes remain Unhealthy until the old
provider operation finishes and a new bounded probe supplies recovery evidence.
Caller cancellation is never cached. Probe faults/timeouts are logged with redacted
diagnostics, and result/log diagnostics do not include exception messages or objects.
Failed refreshes retain any prior counts and their original sample times, explicitly
marked unavailable, rather than inventing zero counts or fresh healthy evidence.

### Health result data

The named check returns standard `HealthStatus.Healthy`, `HealthStatus.Degraded`, or
`HealthStatus.Unhealthy`; the highest severity wins. A fresh local sample can report an
old last-positive instant. Cached metadata does not cache the evaluated status: each call
reevaluates enabled local relay gaps, startup reasons, and evidence age.
Each evaluation uses one instant: the local relay sample in local mode or a single UTC
clock read in metadata-only mode. Both modes share metadata clock/freshness evaluation.

| Data keys | Meaning |
|---|---|
| `profile`, `storageContext`, `maxAttempts`, `maxAttemptsSource`, `relayMonitoring` | `staged-operational-reference` / `metadata-only-reference`, fixed context scope, effective retry limit, `outbox-options` / `profile` source, and `local-relay` / `disabled` selection. Cache entries invalidate when the local attempt cap changes. |
| `metadataAvailable`, `pendingSampledAt`, `parkedSampledAt`, `captureAgeSampledAt`, `sampling` | Whether the current refresh completed queue metadata, plus its original separately sampled metadata/capture-age instants. Capture-age keys are emitted only when enabled. A lag provider can consume the remaining shared deadline after queue evidence completes: the result is still Unhealthy with `probe-timeout`, but fresh queue evidence remains available. Count/time keys are absent if no successful evidence exists; old retained evidence is not marked available after a database failure or timeout before queue sampling completed. |
| `pendingCount`, `pendingCountIsLowerBound`, `pendingCountCap` | Retryable pending rows, including leases/backoff, capped as above. |
| `parkedCount`, `parkedCountIsLowerBound`, `parkedCountCap` | Zero or at least one parked row. Any parked row degrades, never causes parked-only Unhealthy. |
| `oldestPendingCapturedAt`, `oldestPendingCaptureAge` | Oldest known UTC capture instant and its age among retryable pending rows, including leases/backoff. Absent when no known row exists. A future instant is reported and degrades with `capture-age-clock-invalid`; it is not clamped to zero. |
| `unknownCaptureAgeCount`, `unknownCaptureAgeCountIsLowerBound`, `unknownCaptureAgeCountCap` | Enabled mode only: legacy retryable pending rows with no authoritative capture evidence. Any nonzero value degrades with `capture-age-unknown`. |
| `consumerLag`, `consumerLagAvailable`, `consumerLagMeasures`, `consumerLagEvidenceMeasures` | `consumerLag` is `disabled` or `enabled`; disabled is configuration, not observed health. Enabled results report availability plus the selected and supplied measure flags. |
| `consumerLagConsumerId`, `consumerLagSource`, `consumerLagScope`, `consumerLagSampledAt` | Enabled mode only: the provider's stable scope identities and original source sample time. Retained evidence never gets a newer sample time from cache reuse. |
| `consumerLagRequiredPartitionCount`, `consumerLagCoveredPartitionCount` | Required and represented partition counts. Incomplete coverage degrades with `consumer-lag-coverage-missing`. |
| `consumerLagOldestUnaccountedAt`, `consumerLagOldestUnaccountedAge` | `Age` evidence. A null timestamp is authoritative quiet-source zero lag and omits both keys; a future timestamp remains visible and degrades rather than being clamped. |
| `consumerLagUnaccountedCount`, `consumerLagUnaccountedCountIsLowerBound` | `Count` evidence and whether it is conservative lower-bound data. A lower bound below the degraded threshold degrades because it cannot prove the true count is healthy. |
| `relayScope`, `relaySampledAt`, `relayStartedAt`, `lastPositiveRelayObservationAt`, `relayGap` | Local mode only: `local-instance`, fresh local sample and original local lifecycle/poll instants. Missing instants/gap keys are absent, not fabricated. |
| `deliveredRows`, `lastDurableDeliveryAt` | Local mode only: lifetime row-completion count and last observation after successful bookkeeping. Failed attempts and lost-lease rows are excluded; no downstream consumer-success claim. A missing last delivery is not itself an unhealthy condition. |
| `pendingDegradedThreshold`, `pendingUnhealthyThreshold`, `captureAgeDegradedAfter`, `captureAgeUnhealthyAfter`, `probeTimeout`, `cacheDuration`, `evidenceMaxAge` | Actual selected metadata/capture-age bounds and timing. Capture-age thresholds are emitted only when enabled. |
| `consumerLagAgeDegradedAfter`, `consumerLagAgeUnhealthyAfter`, `consumerLagCountDegradedThreshold`, `consumerLagCountUnhealthyThreshold` | Selected consumer-lag bounds. Age or count thresholds are emitted only when that measure is selected. |
| `relayDegradedAfter`, `relayUnhealthyAfter`, `startupGracePeriod` | Local mode only: selected relay timing. Omitted when relay monitoring is disabled, not zero-valued observations. |
| `captureAge` | `disabled` or `enabled` according to the explicit profile switch. Disabled never means observed healthy. |
| `reasons`, `probeFaultType`, `consumerLagFaultType` | Stable reason codes and safe fault categories. Exception failures use their type name; a provider that violates its non-null contract uses "NullEvidence". No exception message/object, payload, raw `LastError`, connection string, message id, or business/trace lineage is returned. |

Reasons include `pending-depth-degraded`, `pending-depth-unhealthy`, `parked-messages`,
`capture-age-degraded`, `capture-age-unhealthy`, `capture-age-unknown`,
`capture-age-clock-invalid`,
`relay-not-started`, `relay-warming-up`, `relay-observation-missing`,
`relay-observation-delayed`, `relay-observation-overdue`, `metadata-stale`,
`metadata-clock-invalid`, `relay-clock-invalid`, `consumer-lag-age-degraded`,
`consumer-lag-age-unhealthy`, `consumer-lag-count-degraded`,
`consumer-lag-count-unhealthy`, `consumer-lag-count-lower-bound`,
`consumer-lag-coverage-missing`,
`consumer-lag-evidence-missing`, `consumer-lag-stale`,
`consumer-lag-clock-invalid`, `consumer-lag-provider-failed`, `probe-timeout`, and
`probe-failed`.
Metadata-only emits no relay reasons or observation/delivery keys. With capture age
disabled, `sampling` is `separate-metadata-queries` or
`separate-metadata-queries-and-local-relay`; enabled profiles use
`separate-metadata-and-capture-age-queries` with the optional `-and-local-relay` suffix.
An enabled lag provider adds a `-and-consumer-lag` sampling component.

## Health-check requirements

**All three stages are implemented:** [outbox readiness](#outbox-readiness) supplies
bounded metadata/local-relay evidence plus explicitly enabled durable capture age and
application-provided consumer/source lag. No inbox-table health inference is shipped.
Readiness settings are not members of `OutboxOptions`. Delivery, retry, lease, replay,
and retention behavior is unchanged.

### Scope and staged delivery

The selected design is **outbox readiness plus optional application/transport-provided
consumer lag**, delivered in stages:

| Stage | Required capability |
|---|---|
| Metadata and relay observations | Implemented: bounded, read-only pending/parked summaries and truthful local relay responsiveness observations, with delivery progress reported separately. |
| Durable capture age | Implemented: nullable UTC evidence for both domain and translated integration rows, provider-compatible bounded queries, application-owned migration rollout, and explicit unknown age for legacy rows. |
| Optional consumer/source lag | Implemented: explicitly selected age/count evidence from an application-owned `IConsumerLagProvider`, with conservative partition coverage, durable accounting, freshness and bounded execution; see [inbox lag requirements](trellis-api-efcore-inbox.md#consumer-lag-health-requirements). |

Each stage must be independently useful without claiming signals from a later stage.
Capture-age evaluation stays disabled until the selected immutable profile sets
`CaptureAgeEnabled = true`; optional lag stays disabled until `ConsumerLagMeasures`
selects age, count, or both and a provider is registered. Neither disabled condition is a
healthy observation of that signal.

These are read-only readiness checks, **not liveness restart triggers or remediation**.
When included in the host's readiness endpoint, Healthy and Degraded remain ready;
Degraded carries a warning. Unhealthy fails readiness.
Never replay, prune, dispatch, publish, change leases, or reset attempts from a probe.

Shared-store depth/capture-age breaches can make every replica unhealthy during a
broker outage. Hosts must deliberately choose whether these checks gate traffic
readiness or are exposed for alerting only; this does not change their reported
severity. Parked-only degradation still remains ready with a warning.

### Evidence and interpretation

| Signal | Required interpretation |
|---|---|
| Pending depth | Count rows with `ProcessedAt == null` and `Attempts < MaxAttempts`, using local relay options or explicit `WorkerMaxAttempts` in metadata-only mode. Include leased and retry-backoff rows, not just rows claimable now. |
| Parked rows | Separately report rows with `ProcessedAt == null` and `Attempts >= MaxAttempts`, matching [maintenance](#dead-letter-and-replay). Any parked row degrades health. Changing `MaxAttempts` changes this classification. |
| Capture age | Age of the oldest retryable pending row with authoritative capture evidence, including leased/backoff rows. Report unknown-age pending rows separately; do not substitute `OccurredAt`, UUIDv7 time, `Sequence`, or `LockedUntil`. |
| Worker responsiveness | Time since a positive local relay poll/drain observation, including successful empty polls. A timer tick, startup log, or caught drain failure is not a positive observation. |
| Durable delivery progress | Successful domain completion or normal integration publisher completion whose bookkeeping persisted, excluding lost-lease rows. Persisting a failed attempt is not successful delivery. |
| Consumer/source lag | Fresh, scoped `ConsumerLagEvidence` selected as age, count, or both. Require complete partition coverage and the selected measurements. Age is the oldest durably unaccounted source time; count is the conservative sum and can be marked a lower bound. A lower bound below the degraded threshold degrades because it cannot establish a healthy upper bound. Null age means authoritative quiet-source zero lag. Never infer lag from inbox table size or an opaque checkpoint; publisher acceptance does not prove consumer processing. |

Depth counts infrastructure **rows**, not unique business events: a domain row can produce
multiple integration rows. An empty, successfully queried outbox has no pending-age breach,
but does not establish relay responsiveness. A responsive worker can still be failing
publication, and retries can honor their policy while breaching a delivery-age SLO.

When local-relay readiness is selected, the relay records a positive local observation only after
a poll/drain completes successfully, including an empty poll. Its separately reported
delivery count includes only processed rows whose bookkeeping persisted and whose leases
were not lost. Persisted failures can prove responsiveness, not delivery; escaping drain
or bookkeeping failures do not renew the observation. Existing logs remain separate:
empty polls do not emit "OutboxRelay.DrainCompleted", and its nonempty count includes
persisted failures. Whole-drain/bookkeeping failures need not increase `Attempts` or
ever park a row. Opting into observation changes no delivery behavior.

Scope local responsiveness to the current relay instance/host lifetime. Another instance's
heartbeat cannot prove this worker is responsive; shared-store delivery progress is a
separate observation. Lack of new delivery on a quiet or backoff-only queue is not itself
a stuck-worker diagnosis. Tune responsiveness deadlines for legitimate poll intervals,
batch/handler durations, and operation timeouts.

### Durable capture age and rollout

`OutboxMessage.CapturedAt` records when **that row** was first captured/staged and persists
with it, for both capture-interceptor domain rows and relay-created integration rows.
Preserve it across retries and parked-row replay; a translated row has its own capture
instant, not its source domain row's age. Capture/staging time precedes the save and is
**not an exact commit timestamp** or exact post-commit queue-residence measurement.

The interceptor's no-argument overload uses `TimeProvider.System`; its explicit
`TimeProvider` overload supports deterministic capture and must use the same instance on
repeated builder calls. The relay uses its DI `TimeProvider`. Both persist
`GetUtcNow().UtcDateTime` as provider-orderable UTC `DateTime`; the health check interprets
materialized values as UTC. Existing rows without authoritative evidence remain unknown;
neither migration time nor business-event time is a valid backfill. The result reports
the known oldest age and capped unknown-row coverage. Enabled age evaluation with unknown
coverage degrades health; a known unhealthy breach still takes precedence. Future
capture evidence degrades with `capture-age-clock-invalid`, without clamping to zero.

The application owns its EF schema migration; Trellis ships no migration. Roll out in
this order:

1. Add nullable UTC `CapturedAt` and the `{ ProcessedAt, CapturedAt, Sequence }` index to
   every queried outbox store.
2. Deploy writers/relays using the new mapping so every new domain and translated row
   receives its own capture instant. Leave legacy values null.
3. Enable `CaptureAgeEnabled` only after all health-check hosts query the migrated schema.
   Unknown retryable legacy rows then make readiness Degraded until processed, parked,
   or deliberately handled by an application-owned data policy.

Enabling against an unmigrated table is a provider fault and therefore Unhealthy. Do not
backfill `CapturedAt` from `OccurredAt`, UUIDv7 time, `Sequence`, `LockedUntil`, or the
migration instant.

### Explicit opt-in reference profile

The **Staged operational readiness reference profile** is a named starting point for
explicit consumer selection and tuning, **not automatic or universal defaults**. No
existing registration selects it. Thresholds are inclusive; when several conditions
apply, the highest severity wins.

| Signal | Degraded | Unhealthy |
|---|---|---|
| Retryable pending depth | At least 1,000 rows | At least 10,000 rows |
| Oldest pending capture age, once supported and enabled | At least 2 minutes | At least 10 minutes |
| Gap since positive local relay observation | At least 1 minute | At least 5 minutes |
| Optional source lag, when enabled | At least 2 minutes **or** 1,000 unaccounted events | At least 10 minutes **or** 10,000 unaccounted events |
| Parked messages | Any parked row | No parked-count-only unhealthy threshold in this profile |

| Timing policy | Reference value | Requirement |
|---|---|---|
| Startup grace | 30 seconds | A missing first relay observation is Degraded with a warming-up reason during grace. Grace never masks parked-row degradation, probe faults, or other unhealthy conditions. |
| Probe budget | 2 seconds | Bound the complete database/provider probe, not each query independently. Its own timeout or a query/provider error is unhealthy. |
| Cache interval | 10 seconds | Reuse bounded snapshots by storage/consumer/instance scope and effective configuration; avoid polling-request stampedes. |
| Observation freshness | 30 seconds | Missing or stale **enabled** evidence degrades health, never silently becomes healthy. |

Measure the relay gap from the last positive observation, or host start if none exists,
without resetting it when startup grace ends. With no first observation, the relay signal
is Degraded from host start: use a warming-up reason before 30 seconds and a missing-evidence
reason from 30 seconds onward. It is therefore Degraded at 45 seconds, and Unhealthy at
5 minutes without a positive observation. Grace changes the reason, not the status.
Once a positive observation arrives, evaluate its actual gap and freshness normally.

Keep **sample freshness** separate from that gap: a fresh snapshot can accurately report
an old last-positive instant. Reading or re-caching it must not renew the positive instant
or an external provider's original sample time.
Missing/stale evidence is a condition mapped to Degraded, not a fourth health status.

Tune depth for workload and capacity, age/lag for the delivery SLO, and relay deadlines
for the actual loop timing. Default scheduled retry waits total roughly 1.5-3 hours
with jitter (about 3.06 hours without it), before adding polling/processing time.
A 2/10-minute age breach can therefore be intentional SLO reporting while the worker
is alive and retrying correctly. It must not be presented as proof that restarting it helps.
Validate configured bounds and timing relationships explicitly rather than clamp them.

### Probe and registration boundaries

- Use a fresh read scope/context, independent of command, dispatch, and relay bookkeeping
  contexts. Use metadata-only server-side summaries; do not materialize payloads or
  deserialize events. `GetDeadLetteredAsync` returns full entities, not a health summary.
- Reuse the maintenance/relay classification semantics, not replay methods. Bound query
  work and result size with suitable provider-supported aggregates or threshold-capped
  reads. A capped count is a lower bound, not an exact total. Do not scan/materialize an
  entire history or introduce coordination locks that serialize normal delivery.
- Preserve original sample times, scope, enabled/disabled/unavailable signals, evidence
  coverage, thresholds, and reasons in the result. Disclose separately sampled evidence
  rather than promise an atomic cross-store snapshot. Cache refresh/failure must not
  erase an unhealthy condition without evidence of recovery.
- Propagate caller cancellation. Distinguish it from the probe's own deadline, which
  produces an unhealthy result. For a shared/coalesced refresh, each caller cancels only
  its own wait: its cancellation must not cancel the refresh or be cached as a health
  result/fault. Other waiters keep their own tokens; the refresh retains its independent
  probe budget. Expose query/provider failures with safe diagnostics;
  never return zero counts, an empty-success result, or an old healthy result as fresh.
- The host owns endpoint authorization and readiness mapping. Restrict detailed results
  to the authorized storage/consumer scope; `ConsumerId` is not an authorization boundary.
  Do not expose bodies, connection strings, raw `LastError`, or business/trace identifiers
  in public health output or high-cardinality metric labels.
- Outbox readiness is the composition-root feature and already has matching direct and
  builder registration. `IConsumerLagProvider` implementations and vendor SDK adapters
  are leaves registered directly by the application; they remain unslotted and add no
  cloud SDK dependency to `Trellis.ServiceDefaults`. See [health registration
  classification](trellis-api-servicedefaults.md#health-registration-classification).

### Acceptance boundary for staged health checks

Tests cover metadata/local relay, durable capture age, consumer lag, cache,
timeout/cancellation, and registration outcomes.

| Scenario | Required outcome |
|---|---|
| No first relay observation inside or after startup grace | Under the reference profile: Degraded with a warming-up reason at 15 seconds; Degraded with a missing-evidence reason at 30 and 45 seconds; Unhealthy at 5 minutes from host start. Grace never masks parked rows or other unhealthy conditions. |
| Empty queue with successful polls | Fresh local responsiveness without requiring a delivery; no inference from the absence of a drain log. |
| Leased/backoff rows, or depth exactly at a threshold | Include all retryable pending rows; use inclusive thresholds and separate parked classification. |
| Failed attempts saved, or repeated bookkeeping failures | Persisted failed attempts can prove responsiveness, not delivery; failed drain/catch paths do not renew positive worker evidence. |
| Legitimate slow batch/retry policy | Apply the tuned responsiveness budget; report age SLO breaches separately from a stuck-worker claim. |
| Legacy rows or old business events | Unknown capture age stays visible; `OccurredAt` is not queue age, and migrations do not fabricate capture time. |
| Publisher acceptance or swallowed local-consumer failure | No claim of downstream success; optional lag needs its own consumer/source evidence. |
| Quiet source, disabled lag, or missing/stale/incomplete enabled lag | Null oldest time plus zero count with complete authoritative coverage is healthy; disabled is explicit; unavailable, incomplete, stale, future, or missing selected evidence degrades. |
| Age/count exactly at a lag threshold | Apply inclusive 2/10-minute and 1,000/10,000 reference boundaries; the highest severity wins. |
| Provider fault, null evidence, or provider consuming the shared deadline | Preserve successful queue metadata, expose only a safe fault category ("NullEvidence" for a null contract violation), and return Unhealthy with `consumer-lag-provider-failed` or the stable overall `probe-timeout` reason. |
| Probe timeout/fault versus caller cancellation | Timeout/fault is unhealthy with diagnostics; caller cancellation propagates. |
| One caller cancels while others await a coalesced refresh | Cancellation propagates only to that caller; the bounded refresh and other waiters continue. Do not cache that cancellation as a result/fault; the refresh's own timeout/fault still follows the unhealthy policy. |
| Repeated cached reads or several relay instances | Preserve evidence age and scope; another instance's activity cannot refresh local responsiveness. |
| Any degraded signal plus an unhealthy breach | Unhealthy wins; degraded-only results remain ready with warning, without mutation or automatic restart. |

## Serialization

Events are serialized and rehydrated with a Trellis-owned `System.Text.Json` options instance that adds a `Maybe<T>` converter.

- **Supported:** value objects that carry a `[JsonConverter]` attribute (the Trellis scalar and composite primitives) round-trip, because the converter travels with the type. `Maybe<T>` members also round-trip — a present value serializes as the underlying value and an absent one as JSON `null`.
- **Not supported:** converters registered only through a caller-supplied `JsonSerializerOptions` factory (they are not consulted by the outbox serializer). Shape those members with attribute-driven value objects or a **nullable transport** (e.g. `string?`).

A fully configurable serializer is a planned follow-up; until then, keep event payloads to attribute-driven, `Maybe<T>`, and primitive-or-nullable members.
