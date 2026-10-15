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

Translate a domain event while the outbox relay dispatches it by adding an external contract to the relay-scoped collector:

```csharp
public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId,
    DateTimeOffset OccurredAt)
    : IIntegrationEvent;

public sealed class OrderPlacedTranslator(
    IIntegrationEventCollector collector)
    : IDomainEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(
        OrderPlaced domainEvent,
        CancellationToken cancellationToken)
    {
        collector.Add(new OrderPlacedIntegrationEvent(
            domainEvent.OrderId.Value,
            domainEvent.OccurredAt));

        return ValueTask.CompletedTask;
    }
}
```

## Key Features

- Captures domain-event rows in the same transaction as aggregate changes.
- Records provider-orderable UTC capture evidence for domain and translated integration rows.
- Persists optional W3C trace context and business lineage on domain rows, and inherits them when staging translated integration rows.
- Dispatches domain events only after a successful commit.
- Publishes pending integration events through a resilient background service.
- Supports configurable batching, locking, lease recovery, retry scheduling, and dead-lettering.
- Exposes an explicit retryability contract for publisher failures.
- Requires no broker dependency; transport packages implement `IIntegrationEventPublisher`.

## Delivery Semantics

**Domain rows** use reporting dispatch: failed domain handlers are retried while saved completed-handler progress is skipped. **Integration rows** have one publisher handoff, not per-consumer retry progress. The default in-process publisher is best-effort: ordinary consumer failures are logged and swallowed, so a new row is processed with `Attempts == 0` and no relay `LastError`; later drains do not retry it.

A broker adapter must await publication acceptance and propagate publication failures. An exposed failure keeps the row pending, increments its persisted failure count, retains the error, and schedules retry under the same message id. `OperationCanceledException` aborts the drain only when the relay token is canceled; an internal publisher timeout is recorded like any other failure. Broker acceptance is not downstream consumer success. Transactional consumption requires an adapter to call the non-swallowing inbox and expose failures for transport redelivery; registering an inbox does not reroute default local publication.

## Important Setup

- `UseOutbox<TContext>()` registers the relay only; register domain-event dispatch and the EF unit of work separately.
- Map the table with `modelBuilder.AddTrellisOutbox()`.
- Register capture with `AddTrellisOutboxInterceptor()` on the context options.
- Do not manually call `SaveChangesAsync()` inside a transactional command handler; the unit-of-work pipeline owns the commit.
- Make integration events immutable records with unique, stable `[IntegrationEventName("...")]` values.
- Carry `OutboundIntegrationMessage.MessageId` unchanged as the transport message ID so inbox consumers can deduplicate end to end.
- The alpha `TrellisOutboxMessages` schema includes nullable `CapturedAt`, `MessageSource`, `CausationId`, `CorrelationId`, `TraceParent`, and `TraceState` columns; no migration is provided. `CapturedAt` is nullable only for migration rollout and legacy rows—new domain and translated rows receive their own UTC capture instant. Use `IntegrationMessageContext.BeginCorrelation("workflow-id")` around a producing transaction to supply an application-owned business correlation id; inbound inbox handlers inherit nonblank envelope correlation and use the inbound message id as their domain row's cause.

The package suppresses recursive domain-event generation while appending outbox rows, preventing outbox persistence from creating more domain events.

## Opt-in Outbox Readiness

The named `trellis-outbox` .NET health check reports bounded pending/parked metadata,
optional durable capture age, optional application-provided consumer lag, and local relay
responsiveness, with durable row completions reported separately. Existing outbox
registration does not enable it:

```csharp
var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();
builder.Services.AddTrellis(trellis => trellis
    .UseDomainEvents()
    .UseOutbox<AppDbContext>()
    .UseOutboxReadiness<AppDbContext>(profile));
```

Direct registration is `services.AddTrellisOutboxReadiness<AppDbContext>(profile)`.
Tune the immutable profile with `with` for workload and poll/batch durations. Counts
include leased/backoff pending rows and disclose lower bounds; probe faults/timeouts are
Unhealthy, while parked rows or missing initial relay evidence are Degraded. Shared
refreshes isolate caller cancellation and preserve original sample times.

Capture age is also explicit and migration-first. Add nullable UTC `CapturedAt` and the
`{ ProcessedAt, CapturedAt, Sequence }` index, deploy writers, then enable it:

```csharp
var profile = OutboxReadinessOptions.CreateStagedOperationalProfile() with
{
    CaptureAgeEnabled = true,
};
```

The inclusive reference thresholds are 2 minutes Degraded and 10 minutes Unhealthy.
Legacy null pending rows are reported separately and degrade readiness; do not backfill
them from `OccurredAt`, UUIDv7, sequence, lease time, or migration time. A translated row
gets its own staging instant, and retries/replay preserve it.

Consumer lag is also explicit. Register one scoped or singleton application/transport
adapter and select the authoritative measures it supplies:

```csharp
builder.Services.AddScoped<IConsumerLagProvider, OrdersConsumerLagProvider>();
profile = profile with
{
    ConsumerLagMeasures = ConsumerLagMeasures.Age | ConsumerLagMeasures.Count,
};
```

The provider returns one conservative `ConsumerLagEvidence` snapshot: stable consumer,
source and authorized scope; original source sample time; required/covered partition
counts; the oldest durably unaccounted source timestamp; and/or the summed unaccounted
count with lower-bound status. It must own gap-aware accounting across out-of-order work,
duplicates and retention. Trellis does not infer lag from inbox rows or opaque checkpoints.
Missing coverage or selected measurements and stale/future evidence degrade readiness;
age/count breaches use inclusive 2/10-minute and 1,000/10,000 reference thresholds.
A lower-bound count below 1,000 also degrades because it cannot prove the true count is
healthy. Provider faults and the shared probe timeout are Unhealthy; fresh queue metadata
already sampled before a lag-provider timeout remains available.

For a split API/worker deployment, keep that local-relay profile in the worker and
select metadata-only in the API, without registering a relay:

```csharp
const int workerMaxAttempts = 10; // Share the same configuration value with the worker.
var profile = OutboxReadinessOptions.CreateMetadataOnlyProfile(workerMaxAttempts);
builder.Services.AddTrellis(trellis => trellis.UseOutboxReadiness<AppDbContext>(profile));
```

`WorkerMaxAttempts` must match the worker's `OutboxOptions.MaxAttempts`. Incidental
local options are ignored without comparison or warning; derive both values from one
configuration key or constant. Relay monitoring is explicitly disabled, not observed
healthy. Metadata-only never observes remote worker responsiveness. With capture age
disabled, worker inactivity alone raises no signal until pending depth reaches 1,000 or
a row is parked. Enabled age can detect an aging pending row, but does not prove the
worker is dead. Probe faults and stale/invalid evidence remain independent conditions.

Local mode requires an actual matching relay instance at Generic Host lifecycle startup,
before any hosted service starts; wrappers hiding that instance are unsupported.
Both modes check context registration without constructing it when the container exposes
registration probing. Bare providers do not run this startup validation.

The host owns endpoint authorization and deliberately chooses traffic-readiness gating
versus alert-only exposure, especially because shared-store breaches affect every replica.
Degraded remains ready; Unhealthy fails readiness when selected for it. Probes are
read-only and add no endpoint or automatic restart/remediation.

`OccurredAt` is not queue age, and publication is not downstream consumer success.
See [readiness APIs and diagnostics](../docs/docfx_project/api_reference/trellis-api-efcore-outbox.md#outbox-readiness)
and the [consumer-lag contract](../docs/docfx_project/api_reference/trellis-api-efcore-inbox.md#consumer-lag-health-requirements).

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-efcore-outbox.md)
- [Outbox integration guide](https://xavierjohn.github.io/Trellis/articles/integration-outbox.html)
- [Azure Service Bus transport](../Trellis.Messaging.AzureServiceBus/README.md)
- [Inbox package](../Trellis.EntityFrameworkCore.Inbox/README.md)

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.EntityFrameworkCore.Outbox\tests\Trellis.EntityFrameworkCore.Outbox.Tests.csproj -c Release
```
