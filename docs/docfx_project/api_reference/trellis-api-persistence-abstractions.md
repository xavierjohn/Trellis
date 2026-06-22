---
package: Trellis.Persistence.Abstractions
namespaces: [Trellis]
types: [IUnitOfWork, IInboxStore, IConsumerCheckpointStore, InboxRecord]
version: v3
last_verified: 2026-06-22
audience: [llm]
---
# Trellis.Persistence.Abstractions &mdash; API Reference

**Package:** `Trellis.Persistence.Abstractions`
**Namespace:** `Trellis`
**Purpose:** Store-agnostic persistence contracts — the commit boundary and the idempotent-consumer inbox store SPIs — so an adapter can persist Trellis state over EF Core (the shipped default), Dapper, ADO, Cosmos DB, or any other store without taking a dependency on a specific persistence technology.

## Use this file when

- You are implementing the commit boundary (`IUnitOfWork`) for a non-EF store.
- You are implementing the inbox dedup record store (`IInboxStore`) or the pull-consumer resume cursor (`IConsumerCheckpointStore`) for a non-EF store.
- You want to install the standard transactional command pipeline over your own `IUnitOfWork` — see `AddTransactionalCommandBehavior()` in `Trellis.Mediator`.
- All types in this package live in the `Trellis` namespace. After adding the `Trellis.Persistence.Abstractions` package reference, the existing `using Trellis;` (or implicit usings) brings them into scope; no new `using` directive is required.

## Package role

- The package depends only on `Trellis.Core`. It names no EF, Dapper, or vendor type, so any adapter can implement the contracts without inheriting a persistence dependency.
- `Trellis.EntityFrameworkCore` is the shipped adapter: `EfUnitOfWork<TContext>` realizes `IUnitOfWork`, and `Trellis.EntityFrameworkCore.Inbox` realizes `IInboxStore` / `IConsumerCheckpointStore`.
- The commit pipeline (`TransactionalCommandBehavior`) and the consume-side dispatch contracts (`IInboxDispatcher`, `IntegrationEnvelope`, `InboxDispatchOutcome`) live in `Trellis.Mediator`, since the inbox dispatcher invokes integration-event handlers through the mediator.

## `IUnitOfWork`

The commit boundary for staged changes. Repositories stage changes; `CommitAsync` persists them.

```csharp
public interface IUnitOfWork
{
    Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default);
    IDisposable BeginScope();
}
```

- `CommitAsync` returns `Result<Unit>` so concurrency, duplicate-key, and foreign-key failures surface as a typed `Error` instead of an exception.
- `BeginScope` makes nested commits depth-aware: a `CommitAsync` call inside a nested scope (depth &gt; 1) must defer (return success without writing); only the outermost scope's `CommitAsync` persists. This lets the standard `TransactionalCommandBehavior` wrap every command without a successful inner command committing a partially-completed outer command's staged changes.

## `IInboxStore` and `InboxRecord`

The dedup record store SPI for idempotent integration-event consumption. The dispatcher records a processed `(ConsumerId, MessageId)` pair inside the consumer's unit of work, so the dedup record and the handler side effects commit together — or not at all.

```csharp
public sealed record InboxRecord(
    Guid MessageId,
    string EventType,
    DateTimeOffset OccurredAt,
    string? MessageSource = null,
    Guid? CausationId = null,
    string? CorrelationId = null);

public interface IInboxStore
{
    Task<bool> TryRecordAsync(string consumerId, InboxRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> FilterUnprocessedAsync(
        string consumerId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);
}
```

- `InboxRecord` is persistence-native: it carries the dedup identity (`MessageId`) plus optional lineage / observability metadata, and names no messaging type, so the store contract depends only on `Trellis.Core`. A dispatcher maps its transport envelope to an `InboxRecord`.
- `TryRecordAsync` returns `true` if the message was newly recorded, or `false` if the `(ConsumerId, MessageId)` pair was already processed — a duplicate the caller should skip. An implementation preserves all-or-nothing atomicity only by enrolling the dedup record in the same unit of work the dispatcher commits.
- `FilterUnprocessedAsync` powers the gap-free anti-join pull model: it returns the subset of candidate ids with no dedup row yet, preserving input order. It is a pure read and stages nothing.

## `IConsumerCheckpointStore`

A pull consumer's durable resume cursor — the per-`ConsumerId` position in a source feed, so a consumer resumes where it left off instead of rescanning the whole log on every poll or restart.

```csharp
public interface IConsumerCheckpointStore
{
    Task<Maybe<string>> GetAsync(string consumerId, CancellationToken cancellationToken);
    Task SetAsync(string consumerId, string position, CancellationToken cancellationToken);
}
```

- **Performance, not correctness.** The checkpoint narrows the scan window; it is not the deduplication boundary. Correctness stays with the inbox anti-join (`IInboxStore.FilterUnprocessedAsync`) plus the dedup row. Scan a window that overlaps the checkpoint and let the anti-join skip whatever is already processed.
- **Opaque position.** Trellis does not interpret `position` — it is whatever cursor the source feed uses (a sequence number, a UUIDv7 high-water mark, a timestamp), serialized to a string. The store persists and returns it verbatim.
- **Last-writer-wins.** The cursor is single-valued per `ConsumerId`; `SetAsync` is an upsert. A shared cursor is not a coordination primitive — the typical shape is one logical advancer per consumer.
