---
name: recipe-37-reconstitution
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows reconstituting an aggregate in a non-EF repository without re-running its factory. Use when rebuilding aggregates without re-validation or events."
---
# Recipe 37 — Reconstituting an aggregate without its factory (non-EF repositories)

**Requires:** `Trellis.Core`, plus `Trellis.Http.Abstractions`.

## Contents

- [1. The aggregate exposes a reconstitution factory (author-owned)](#1-the-aggregate-exposes-a-reconstitution-factory-author-owned)
- [2. The repository assembles domain state, then stamps infrastructure metadata](#2-the-repository-assembles-domain-state-then-stamps-infrastructure-metadata)

**Problem.** A non-EF repository (Dapper, raw ADO, a Cosmos SDK) must rebuild an aggregate from a stored row **without** re-running its `Create`/`TryCreate` factory: reconstitution must not re-validate invariants, mint a new identity, or raise creation events. EF Core does this in its materializer; outside EF you assemble the aggregate yourself. [Recipe 30](recipe-30-rehydration.md) covers turning stored primitives back into value objects; this recipe covers assembling the aggregate from them and restoring its persistence metadata via the `IReconstitutionStampable` seam.

## 1. The aggregate exposes a reconstitution factory (author-owned)

Add a private constructor that takes the **full** persisted domain state (including child collections) and a `Reconstitute(...)` factory that calls it — pure assignment, no `Create`, no behavior methods, no events. Make the factory `internal` (+ `[InternalsVisibleTo]` to the persistence assembly) to keep it off the public domain surface, or `public` when the adapter lives in another package.

```csharp
using Trellis;

public sealed class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    public CustomerId CustomerId { get; }
    public OrderStatus Status { get; }
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    private Order(OrderId id, CustomerId customerId) : base(id)   // create-time ctor
    {
        CustomerId = customerId;
        Status = OrderStatus.Draft;
    }

    public static Result<Order> Create(CustomerId customerId) => /* invariant checks */ ;

    private Order(OrderId id, CustomerId customerId, OrderStatus status, IEnumerable<OrderLine> lines)
        : base(id)                                                // reconstitution ctor — pure assignment
    {
        CustomerId = customerId;
        Status = status;
        _lines.AddRange(lines);
    }

    // Rebuilds an Order from stored domain state. Does NOT run Create or raise events.
    internal static Order Reconstitute(
        OrderId id, CustomerId customerId, OrderStatus status, IEnumerable<OrderLine> lines) =>
        new(id, customerId, status, lines);
}
```

## 2. The repository assembles domain state, then stamps infrastructure metadata

Rehydrate the value objects (fail-loud per [Recipe 30](recipe-30-rehydration.md)), call `Reconstitute`, then cast to `IReconstitutionStampable` to restore the audit timestamps and the concurrency token in one call. `StampReconstitutedState` validates the ETag and clears any uncommitted events, so the loaded aggregate has no uncommitted events (`IsChanged == false` under the default event-based change tracking).

```csharp
public sealed class DapperOrderRepository(IDbConnection db) : IOrderRepository
{
    public async Task<Result<Order>> FindByIdAsync(OrderId id, CancellationToken ct)
    {
        var row = await db.QuerySingleOrDefaultAsync<OrderRow>(/* ... */);
        if (row is null)
            return Result.Fail<Order>(new Error.NotFound(ResourceRef.For<Order>(id)));

        var lineRows = await db.QueryAsync<OrderLineRow>(/* ... */);
        var lines = lineRows.Select(r => OrderLine.Reconstitute(
            OrderLineId.TryCreate(r.Id).GetValueOrThrow($"Corrupt OrderLine.Id in row {r.Id}"),
            ProductId.TryCreate(r.ProductId).GetValueOrThrow($"Corrupt OrderLine.ProductId in row {r.Id}"),
            r.Quantity));

        var order = Order.Reconstitute(
            OrderId.TryCreate(row.Id).GetValueOrThrow($"Corrupt Order.Id in row {row.Id}"),
            CustomerId.TryCreate(row.CustomerId).GetValueOrThrow($"Corrupt Order.CustomerId in row {row.Id}"),
            OrderStatus.TryCreate(row.Status).GetValueOrThrow($"Corrupt Order.Status in row {row.Id}"),
            lines);

        // Restore the infrastructure metadata loaded from the row. Store-native tokens are commonly
        // quoted (a Cosmos _etag arrives with its quotes, e.g. "00000000-0000-0000-6fd5-8990b40501dd"; other
        // stores or proxies may add a W/ weak prefix), but StampReconstitutedState requires
        // the unquoted RFC 9110 opaque form and throws on '"'. Stores that already hand back an
        // unquoted token (a SQL rowversion, say) pass straight through.
        ((IReconstitutionStampable)order)
            .StampReconstitutedState(row.CreatedAt, row.LastModified, NormalizeStoredETag(row.ETag, row.Id));

        return Result.Ok(order);
    }

    private static string NormalizeStoredETag(string stored, Guid rowId)
    {
        if (!stored.StartsWith('"') && !stored.StartsWith("W/\"", StringComparison.Ordinal))
            return stored;

        if (!EntityTagValue.TryParse(stored).TryGetValue(out var parsed))
            throw new InvalidOperationException($"Corrupt Order.ETag in row {rowId}: {stored}");

        return parsed.OpaqueTag;
    }
}
```

On **save**, generate a fresh token and stamp it with `((IETagStampable)order).StampETag(Guid.NewGuid().ToString("N"))` (the aggregate implements the interface explicitly, so the cast is required), using the previous value in the optimistic `WHERE ETag = @original` clause — the non-EF equivalent of the EF concurrency-token interceptor. Do not use `StampReconstitutedState` on save: it calls `AcceptChanges()`, which clears the aggregate's pending domain events before they are published after the commit. Reserve it for loading. A null, empty or malformed stored token makes `StampReconstitutedState` throw (`ArgumentNullException` or `ArgumentException`), so backfill the column before loading.

**Semantics to remember.**
- The reconstitution constructor must be pure assignment: no `Create`, no behavior methods, no events. `StampReconstitutedState` clears any uncommitted events (it calls `AcceptChanges()`), which is a safety net for loading only; relying on it hides a modeling bug.
- Reconstitution does **not** re-validate domain invariants — correct for trusted outbound reads (see [Recipe 30](recipe-30-rehydration.md)). Value-object-level checks still run through each `TryCreate`.
- Child entities follow the same pattern: a private reconstitution constructor + `Reconstitute` factory; the parent copies them into its backing collection.
