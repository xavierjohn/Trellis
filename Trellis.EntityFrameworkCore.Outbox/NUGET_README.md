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
- Persists optional W3C trace context and business lineage on domain rows and translated integration rows.
- Dispatches domain events only after a successful commit.
- Publishes pending integration events through a resilient background service.
- Supports configurable batching, locking, lease recovery, retry scheduling, and dead-lettering.
- Exposes an explicit retryability contract for publisher failures.
- Requires no broker dependency; transport packages implement `IIntegrationEventPublisher`.

## Delivery Semantics

**Domain rows** use reporting dispatch to retry failed domain handlers, skipping saved completed-handler progress. **Integration rows** have one publisher handoff, not per-consumer retry progress. Default local fan-out is best-effort: ordinary consumer failures are logged and swallowed, so a new row is processed with `Attempts == 0` and no relay `LastError`, and those consumers are not retried.

Broker adapters must await publication acceptance and propagate publication failures. An exposed non-cancellation failure keeps the row pending with an incremented failure count, retained error, and retry under the same message id. Acceptance is not consumer success. Transactional consumption requires an adapter to invoke the non-swallowing inbox and expose failures for transport redelivery; inbox registration does not reroute default local publication.

All three wiring steps are required: map the table, register `AddTrellisOutboxInterceptor()` for capture, and register the relay. Let the unit-of-work pipeline own `SaveChangesAsync()`. Carry `OutboundIntegrationMessage.MessageId` unchanged as the transport message ID so inbox consumers can deduplicate end to end.

The alpha outbox schema includes nullable `MessageSource`, `CausationId`, `CorrelationId`, `TraceParent`, and `TraceState` columns; no migration or backfill is provided. Use `IntegrationMessageContext.BeginCorrelation("workflow-id")` to supply an application-owned business correlation id; one is never inferred from the trace.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-outbox.html)
- [Outbox integration guide](https://xavierjohn.github.io/Trellis/articles/integration-outbox.html)
- [Azure Service Bus transport](https://www.nuget.org/packages/Trellis.Messaging.AzureServiceBus)
- [Inbox package](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Inbox)
