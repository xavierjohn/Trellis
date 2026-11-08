---
name: recipe-35-outbox
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows the transactional outbox for crash-safe domain events. Use when domain events must survive a crash."
---
# Recipe 35 — Transactional outbox for crash-safe domain events

**Problem.** Your command raises a domain event and commits, but the in-pipeline `UseDomainEvents()` dispatch runs *after* the transaction. If the process crashes between the commit and the dispatch, the event is lost — the order is saved but `OrderPlaced` never reaches its handler.

**Fix.** Install `Trellis.EntityFrameworkCore.Outbox`. The capture interceptor writes one row per uncommitted event into `TrellisOutboxMessages` in the **same** transaction as the aggregate, and a background relay re-dispatches them after the commit. Wire three things:

```csharp
// 1. Map the table.
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.AddTrellisOutbox();

// 2. Add the capture interceptor on the context options.
options.UseNpgsql(cs).AddTrellisInterceptors().AddTrellisOutboxInterceptor();

// 3. Register the relay (UseOutbox) alongside your handlers (UseDomainEvents).
services.AddTrellis(trellis => trellis
    .UseDomainEvents(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseOutbox<AppDbContext>());
```

Raise events exactly as before — `DomainEvents.Add(new OrderPlaced(Id, clock.GetUtcNow()))`. When the outbox is enabled the capture interceptor clears the aggregate's events after the commit succeeds (in the `SavedChanges` callback, not inside the transaction), so the in-pipeline dispatch sees none and the relay becomes the single, durable dispatcher.

**Semantics to remember.**

- **Domain rows** use reporting dispatch: every domain handler must complete before the row is processed. A handler that throws leaves it pending and retry re-invokes only failed handlers, up to `OutboxOptions.MaxAttempts`, after which it is parked. Make handlers idempotent — a crash can repeat successes whose progress was not saved. Default in-pipeline domain dispatch still swallows handler exceptions because its post-commit callers have no retry mechanism. Integration rows have a different publisher-handoff boundary; see Recipe 36.
- `Maybe<T>` event members are supported — a present value serializes as the underlying value, an absent one as JSON `null`. Members that depend on a caller-registered (non-attribute) `JsonSerializerOptions` converter still need a nullable transport, since the outbox serializer only honors `[JsonConverter]`-attributed types.
- This is an outbox, not an event store: rows are a transient delivery buffer and may be pruned once `ProcessedAt` is set.
- Capture persists the current W3C trace context with each domain row; the outbox schema includes nullable lineage and trace columns. See the outbox reference for the column names.
- Every new domain row also receives its own UTC `CapturedAt` from the interceptor's
  `TimeProvider`. Add the nullable column/index migration before deploying the writer;
  legacy nulls remain unknown and `OccurredAt` is never a queue-age substitute.

See [trellis-api-efcore-outbox.md](../trellis-api-efcore-outbox.md#how-the-outbox-works) for the full contract, options, and operational guidance.

**Optional readiness.** Existing outbox calls do not enable health checks. Explicitly
select `OutboxReadinessOptions.CreateStagedOperationalProfile()` and add
`.UseOutboxReadiness<AppDbContext>(profile)` alongside `.UseOutbox<AppDbContext>()`.
The named `trellis-outbox` check reports bounded pending/parked rows, local relay
responsiveness, and separately counted durable row completions. After the nullable
`CapturedAt` migration and writer rollout, opt into the 2/10-minute reference age SLO
with `profile with { CaptureAgeEnabled = true }`; legacy unknown pending rows degrade.
Consumer/source lag also remains disabled until the profile selects
`ConsumerLagMeasures.Age`, `Count`, or both and the application registers an
`IConsumerLagProvider`; the provider owns conservative gap-aware source evidence.
The host owns endpoint authorization and whether shared-store breaches gate traffic
or only alert.
See [outbox readiness](../trellis-api-efcore-outbox.md#outbox-readiness).

**Split API/worker hosts.** Keep the existing local-relay profile in the worker. The API
can select `OutboxReadinessOptions.CreateMetadataOnlyProfile(workerMaxAttempts)` and
`.UseOutboxReadiness<AppDbContext>(profile)` without `.UseOutbox<AppDbContext>()`.
`WorkerMaxAttempts` must equal the worker's `OutboxOptions.MaxAttempts`; derive both
from one configuration key or constant. Incidental local options are never resolved or
compared, so a mismatch generates no warning.

Metadata-only reports `relayMonitoring = "disabled"` and omits relay observations.
With capture age disabled, worker inactivity alone raises no signal until pending depth
reaches 1,000 or a row is parked. Enabling capture age can report an aging row but still
does not prove the remote worker is dead; it may be alive and retrying. Probe faults and
stale/invalid evidence still report failures independently.
Generic Host lifecycle startup validates required local-relay configuration and, when
the container supports registration probing, context registration without constructing
the context. Bare providers do not run this startup validation.
