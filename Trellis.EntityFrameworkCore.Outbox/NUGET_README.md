# Trellis.EntityFrameworkCore.Outbox

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Outbox.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox)

Transactional outbox and post-commit domain-event dispatch for EF Core applications built with Trellis.

It captures domain events in the aggregate transaction and relays them after commit. Translators can then stage durable integration rows; publication guarantees depend on the configured publisher.

> This package opts out of NativeAOT and trimming because it builds on EF Core and discovers integration-event types at runtime.

## Installation

```bash
dotnet add package Trellis.EntityFrameworkCore.Outbox
```

## Quick Example

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.AddTrellisOutbox();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
        .AddTrellisInterceptors()
        .AddTrellisOutboxInterceptor());

builder.Services.AddTrellis(trellis => trellis
    .UseDomainEvents(typeof(Program).Assembly)
    .UseIntegrationEvents(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseOutbox<AppDbContext>());
```

Translate domain events into integration events by adding them to `IIntegrationEventCollector` from a domain-event handler.

## Key Features

- Captures domain-event rows in the same transaction as aggregate changes.
- Records provider-orderable UTC capture evidence for domain and translated integration rows.
- Persists optional W3C trace context and business lineage on domain rows and translated integration rows.
- Dispatches domain events only after a successful commit.
- Publishes pending integration events through a resilient background service.
- Supports configurable batching, locking, lease recovery, retry scheduling, and dead-lettering.
- Exposes an explicit retryability contract for publisher failures.
- Requires no broker dependency; transport packages implement `IIntegrationEventPublisher`.

## Delivery Semantics

**Domain rows** use reporting dispatch to retry failed domain handlers, skipping saved completed-handler progress. **Integration rows** have one publisher handoff, not per-consumer retry progress. Default local fan-out is best-effort: ordinary consumer failures are logged and swallowed, so a new row is processed with `Attempts == 0` and no relay `LastError`, and those consumers are not retried.

Broker adapters must await publication acceptance and propagate publication failures. An exposed failure keeps the row pending with an incremented failure count, retained error, and retry under the same message id. `OperationCanceledException` aborts the drain only when the relay token is canceled; an internal publisher timeout is recorded like any other failure. Acceptance is not consumer success. Transactional consumption requires an adapter to invoke the non-swallowing inbox and expose failures for transport redelivery; inbox registration does not reroute default local publication.

All three wiring steps are required: map the table, register `AddTrellisOutboxInterceptor()` for capture, and register the relay. Let the unit-of-work pipeline own `SaveChangesAsync()`. Carry `OutboundIntegrationMessage.MessageId` unchanged as the transport message ID so inbox consumers can deduplicate end to end.

The alpha outbox schema includes nullable `CapturedAt`, `MessageSource`, `CausationId`, `CorrelationId`, `TraceParent`, and `TraceState` columns; no migration or backfill is provided. New domain and translated rows receive their own UTC `CapturedAt`; legacy nulls remain unknown. Use `IntegrationMessageContext.BeginCorrelation("workflow-id")` to supply an application-owned business correlation id; one is never inferred from the trace.

## Opt-in Outbox Readiness

Explicitly select
`OutboxReadinessOptions.CreateStagedOperationalProfile()` and register
`services.AddTrellisOutboxReadiness<AppDbContext>(profile)` or
`.UseOutboxReadiness<AppDbContext>(profile)` alongside the relay. Existing outbox calls
do not enable the named `trellis-outbox` .NET health check.

It reports bounded pending/parked metadata, optional durable capture age, local relay
responsiveness, and separate durable row completions. Counts include leases/backoff and disclose lower bounds.
Shared refreshes isolate caller cancellation; probe faults/timeouts are Unhealthy,
and parked rows or missing initial relay evidence are Degraded. Tune the immutable
profile with `with` for workload and relay timing.

Capture age defaults off for migration-first rollout. Add nullable UTC `CapturedAt` and
the `{ ProcessedAt, CapturedAt, Sequence }` index, deploy writers, then set
`CaptureAgeEnabled = true` with a `with` expression. The inclusive reference thresholds
are 2/10 minutes. Unknown legacy pending rows degrade and are counted separately; never
backfill them from `OccurredAt` or migration time.

For split API/worker hosts, keep local monitoring in the worker and select
`OutboxReadinessOptions.CreateMetadataOnlyProfile(workerMaxAttempts)` in the API,
using the same registration method without a relay. `WorkerMaxAttempts` must equal
the worker's `OutboxOptions.MaxAttempts`; derive both from one configuration key or
constant. Incidental local options are never resolved or compared, so mismatches
generate no warning. Relay monitoring is disabled, not observed healthy.
Metadata-only does not observe remote worker responsiveness. With capture age disabled,
inactivity alone raises no signal until pending depth reaches 1,000 or a row is parked.
Enabled age can detect an aging pending row, but does not prove the worker is dead.
Probe faults and stale/invalid evidence still apply.

Generic Host lifecycle startup validates the actual matching relay in local mode,
before hosted services start; decorators hiding the instance are unsupported.
Context registration is checked without construction when supported by the container.
Bare service providers do not run this startup validation.

The host owns endpoint authorization and traffic-readiness versus alert-only exposure:
shared-store breaches can affect every replica. Degraded remains ready; selected
Unhealthy fails readiness. No endpoint, mutation, or restart policy is added.

**Not implemented:** optional consumer/source lag. `OccurredAt` is not queue age, and
publication is not downstream consumer success.
See [readiness APIs and diagnostics](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-outbox.html#outbox-readiness)
and the [remaining staged contract](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-outbox.html#health-check-requirements).

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-outbox.html)
- [Outbox integration guide](https://xavierjohn.github.io/Trellis/articles/integration-outbox.html)
- [Azure Service Bus transport](https://www.nuget.org/packages/Trellis.Messaging.AzureServiceBus)
- [Inbox package](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Inbox)
