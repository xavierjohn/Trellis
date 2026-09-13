# Trellis.EntityFrameworkCore.Outbox

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Outbox.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox)

Transactional outbox and post-commit domain-event dispatch for EF Core applications built with Trellis.

It captures domain events in the aggregate transaction and relays them after commit. Translators can then stage integration events for reliable publication.

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
- Dispatches domain events only after a successful commit.
- Publishes pending integration events through a resilient background service.
- Supports configurable batching, locking, lease recovery, retry scheduling, and dead-lettering.
- Exposes an explicit retryability contract for publisher failures.
- Requires no broker dependency; transport packages implement `IIntegrationEventPublisher`.

All three wiring steps are required: map the table, register `AddTrellisOutboxInterceptor()` for capture, and register the relay. Let the unit-of-work pipeline own `SaveChangesAsync()`. Carry `OutboundIntegrationMessage.MessageId` unchanged as the transport message ID so inbox consumers can deduplicate end to end.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-outbox.html)
- [Outbox integration guide](https://xavierjohn.github.io/Trellis/articles/integration-outbox.html)
- [Azure Service Bus transport](https://www.nuget.org/packages/Trellis.Messaging.AzureServiceBus)
- [Inbox package](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Inbox)
