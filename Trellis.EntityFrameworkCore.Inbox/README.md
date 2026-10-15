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
- Restores inbound business correlation and starts a consumer activity from valid W3C trace context while handlers and their atomic commit run.
- Uses a composite `(ConsumerId, MessageId)` key as the concurrency guard.
- Exposes a store-neutral `IInboxStore` contract and an EF Core implementation.
- Supports anti-join filtering and optional pull-consumer checkpoints.

## Delivery Contract

- Keep `ConsumerId` stable across deployments.
- Carry the producer's outbox row ID unchanged as `MessageId`.
- The guarantee covers writes through the inbox's `DbContext`; external calls need their own idempotency.
- The built-in EF dispatcher requires its matching EF store. Pair a custom `IInboxStore` with a custom dispatcher that owns the same unit of work.
- The checkpoint is a performance cursor, not a replacement for inbox deduplication.
- Map the table with `modelBuilder.AddTrellisInbox()` and register a transport adapter that invokes the dispatcher.

## Operations

The inbox stores committed dedup metadata, not payloads or failed-message history.
Trellis ships no inbox maintenance facade, automatic expiry job, or replay coordinator.
Inspection is application-owned and must be read-only, bounded, and scoped to the
authorized storage and consumer; a missing row is not proof that a message never ran.

Retention is opt-in with no automatic expiry. Before pruning, bound every redelivery
and replay source, including dead-letter/manual redrive and pull-feed rewind; removing
a row removes its dedup protection. Transport redrive preserves the original identity.

Controlled replay of consumed messages means an isolated rebuild: a separate, stable
`ConsumerId`, isolated storage and effects, and the original `MessageId` and versioned
payload from an application-owned durable log/archive. Live dedup history stays intact.
See the [operational requirements and acceptance criteria](../docs/docfx_project/api_reference/trellis-api-efcore-inbox.md#operational-requirements-and-transport-boundary).

No inbox-table health check is shipped: receipts and checkpoint timestamps cannot
establish source lag or consumer liveness. The outbox package's optional readiness stage
accepts explicitly enabled, fresh `IConsumerLagProvider` evidence scoped to storage,
consumer, source, and required partitions; unconfigured is disabled, not observed
healthy. See the [consumer-lag requirements](../docs/docfx_project/api_reference/trellis-api-efcore-inbox.md#consumer-lag-health-requirements)
and linked opt-in readiness API.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-efcore-inbox.md)
- [Inbox integration guide](https://xavierjohn.github.io/Trellis/articles/integration-inbox.html)
- [Outbox package](../Trellis.EntityFrameworkCore.Outbox/README.md)

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.EntityFrameworkCore.Inbox\tests\Trellis.EntityFrameworkCore.Inbox.Tests.csproj -c Release
```
