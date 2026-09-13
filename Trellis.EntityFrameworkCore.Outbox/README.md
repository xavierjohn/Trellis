# Trellis.EntityFrameworkCore.Outbox

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Outbox.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox)

Transactional outbox and post-commit domain-event dispatch for EF Core applications built with Trellis.

It atomically stores integration events with aggregate changes, then publishes them from a background service so transient broker failures do not lose messages.

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

services.AddTrellis(trellis => trellis
    .UseDomainEvents(typeof(Program).Assembly)
    .UseIntegrationEvents(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseOutbox<AppDbContext>());
```

Publish an integration event from a domain-event handler by appending it to the current outbox scope:

```csharp
public sealed class OrderPlacedTranslator(
    IIntegrationEventCollector collector)
    : IDomainEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(
        OrderPlaced domainEvent,
        CancellationToken cancellationToken)
    {
        collector.Add(OrderPlacedIntegrationEvent.From(domainEvent));

        return ValueTask.CompletedTask;
    }
}
```

## Key Features

- Persists outbox rows in the same transaction as aggregate changes.
- Dispatches domain events only after a successful commit.
- Publishes pending integration events through a resilient background service.
- Supports configurable batching, locking, lease recovery, retry scheduling, and dead-lettering.
- Exposes an explicit retryability contract for publisher failures.
- Requires no broker dependency; transport packages implement `IIntegrationEventPublisher`.

## Important Setup

- `UseOutbox<TContext>()` registers the relay only; register domain-event dispatch and the EF unit of work separately.
- Map the table with `modelBuilder.AddTrellisOutbox()`.
- Register capture with `AddTrellisOutboxInterceptor()` on the context options.
- Do not manually call `SaveChangesAsync()` inside a transactional command handler; the unit-of-work pipeline owns the commit.
- Make integration events immutable records with unique `[IntegrationEventType]` values and stable names.
- Carry `OutboundIntegrationMessage.MessageId` unchanged as the transport message ID so inbox consumers can deduplicate end to end.

The package suppresses recursive domain-event generation while appending outbox rows, preventing outbox persistence from creating more domain events.

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
