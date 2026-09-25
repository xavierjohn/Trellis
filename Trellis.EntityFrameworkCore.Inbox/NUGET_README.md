# Trellis.EntityFrameworkCore.Inbox

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Inbox.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Inbox)

Transactional inbox for idempotent integration-event consumption with EF Core.

The inbox records `(ConsumerId, MessageId)` in the same unit of work as a handler's local writes, turning transport redelivery into effectively-once application of those effects.

> This package opts out of NativeAOT and trimming because it builds on EF Core and resolves handlers by event type at dispatch time.

## Installation

```bash
dotnet add package Trellis.EntityFrameworkCore.Inbox
```

## Quick Example

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.AddTrellisInbox();

services.AddTrellis(trellis => trellis
    .UseIntegrationEvents(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseInbox<AppDbContext>(options =>
        options.ConsumerId = "orders-service"));
```

Your transport adapter passes each received event to `IInboxDispatcher.DispatchAsync(...)` using the producer's stable message ID.

## Key Features

- Commits the deduplication row and handler writes atomically in one `DbContext`.
- Returns `Processed` or `SkippedDuplicate` so transports can settle both outcomes.
- Propagates handler failures so the transaction rolls back and the transport redelivers.
- Restores inbound business correlation and W3C trace context during handlers and their atomic commit.
- Uses a composite `(ConsumerId, MessageId)` key as the concurrency guard.
- Supports anti-join filtering and optional pull-consumer checkpoints.

Keep `ConsumerId` stable across deployments and carry the producer's outbox row ID unchanged as `MessageId`. External calls are outside the inbox transaction and require their own idempotency.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-inbox.html)
- [Inbox integration guide](https://xavierjohn.github.io/Trellis/articles/integration-inbox.html)
- [Outbox package](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox)
