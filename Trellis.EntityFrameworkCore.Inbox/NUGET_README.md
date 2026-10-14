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

`UseIntegrationEvents(...)` supplies handler bindings shared by the inbox and the default **best-effort local** publisher. `UseInbox<TContext>()` does not route that publisher through the inbox: its swallowed consumer failures still process integration outbox rows without retry. The adapter must actually invoke the non-swallowing inbox dispatcher and expose failures for transport redelivery. Broker publication acceptance alone is not consumer processing.

## Key Features

- Commits the deduplication row and handler writes atomically in one `DbContext`.
- Returns `Processed` or `SkippedDuplicate` so transports can settle both outcomes.
- Propagates handler failures so the transaction rolls back and the transport redelivers.
- Rejects new messages with no registered handler without committing their dedup row.
- Restores inbound business correlation and W3C trace context during handlers and their atomic commit.
- Uses a composite `(ConsumerId, MessageId)` key as the concurrency guard.
- Supports anti-join filtering and optional pull-consumer checkpoints.

Keep `ConsumerId` stable across deployments and carry the producer's outbox row ID unchanged as `MessageId`. External calls are outside the inbox transaction and require their own idempotency. The built-in EF dispatcher requires its matching EF store; pair a custom `IInboxStore` with a custom dispatcher that owns the same unit of work.

## Operations

The inbox retains committed metadata, not payloads or failed attempts. Trellis ships
no inbox maintenance facade, automatic expiry job, or replay coordinator. Inspection
is application-owned, bounded, and storage/consumer-scoped; a missing row is not proof
that a message never ran.

Pruning is opt-in only after bounding all redelivery/replay sources. Transport redrive
keeps the original identity. Replaying consumed messages requires a separate stable
`ConsumerId` and isolated rebuild storage/effects, with original message IDs and
versioned payloads supplied by an application-owned durable log/archive; live dedup
rows remain intact. See the [operational requirements](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-inbox.html#operational-requirements-and-transport-boundary).

**Health checks are not implemented.** Inbox receipts and checkpoint timestamps cannot
establish source lag or consumer liveness. Optional lag requires explicitly enabled,
fresh application/transport evidence scoped to storage, consumer, source, and partitions;
unconfigured is disabled, not observed healthy. See the [consumer-lag requirements](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-inbox.html#consumer-lag-health-requirements)
and linked opt-in readiness profile.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-inbox.html)
- [Inbox integration guide](https://xavierjohn.github.io/Trellis/articles/integration-inbox.html)
- [Outbox package](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox)
