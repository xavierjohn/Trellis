# Trellis.Asp.Idempotency.Cosmos API Reference

Cosmos DB-backed `IIdempotencyStore` for the `Trellis.Asp` idempotency middleware.

- **Package:** `Trellis.Asp.Idempotency.Cosmos`
- **Namespace:** `Trellis.Asp.Idempotency.Cosmos`
- **Depends on:** `Trellis.Asp`, `Microsoft.Azure.Cosmos`, `Newtonsoft.Json`

## Why Cosmos DB

`InMemoryIdempotencyStore` is documented as unsafe across instances and process restarts, so any
multi-replica service needs a shared store. Cosmos DB fits the `IIdempotencyStore` contract
unusually well:

| Contract requirement | Cosmos DB primitive |
| --- | --- |
| Atomic reserve | `CreateItem` returns `409 Conflict` on a duplicate id within a partition, decided on the primary replica |
| Conditional complete / abandon | Native ETag with `IfMatchEtag`, no scripting required |
| Expiry | Per-item `ttl` reclaims storage without a sweeper process |
| No silent eviction | Unlike a Redis cache under `allkeys-lru`, Cosmos DB never drops a live entry under memory pressure |
| Diagnosis | Entries are queryable in Data Explorer |

## Quick start

```csharp
// Startup: provision the container once. It must be partitioned on /scope with TTL enabled.
var database = (await cosmosClient.CreateDatabaseIfNotExistsAsync("billing")).Database;
await CosmosIdempotencyContainer.CreateIfNotExistsAsync(database);

// Composition root.
builder.Services.AddSingleton(cosmosClient);
builder.Services.AddTrellisIdempotency(o => o.MaxResponseBodyBytes = 64 * 1024);
builder.Services.AddCosmosIdempotencyStore("billing");

// Pipeline: after UseRouting and UseAuthentication, so scope resolves to the authenticated actor.
app.UseTrellisIdempotency();
```

## `CosmosIdempotencyStore`

`public sealed class CosmosIdempotencyStore : IIdempotencyStore`

| Member | Notes |
| --- | --- |
| `CosmosIdempotencyStore(Container container, IdempotencyOptions options, TimeProvider? timeProvider = null)` | `container` must use partition key path `/scope` with TTL enabled. `timeProvider` defaults to `TimeProvider.System`. |

### How it stays correct

**Reserve is one atomic operation.** A reservation is claimed with `CreateItem`, keyed by
`id = Base64Url(key)` within partition `scope`. Cosmos DB resolves duplicates on the partition's
primary replica, so exactly one concurrent caller wins. The store *never* grants a reservation on
the strength of a read.

**Every other mutation is ETag-conditional.** Taking over a timed-out reservation, recording a
response, and releasing a slot are all `IfMatchEtag` replaces or deletes. A caller whose view is
stale gets `412` and retries, so it cannot clobber newer state.

**Session consistency is safe.** On a Session-consistency account, the read that follows a `409`
may hit a replica that has not yet seen another instance's write, returning a stale item or a
transient `404`. Neither can cause a double execution, because reservations are granted only by an
atomic create or an ETag-conditional replace. A stale read simply produces `412`/`404` on the
follow-up write and the operation retries. The worst observable effect is a spurious
`AlreadyInFlight`, which the caller retries. **Strong consistency is not required.**

**Expiry is enforced in-process, not by Cosmos DB.** Cosmos DB deletes expired items on a
best-effort background sweep, so an item can outlive its `ttl` and still be returned by a read. The
store re-checks `reservedAt` / `completedAt` on every read and treats a TTL-expired snapshot as
absent. Per-item `ttl` is a storage-reclamation backstop only, and deletion is therefore only ever
allowed to fall on a document the store's own rules have already made unreachable:

| Document state | `ttl` | Why |
|---|---|---|
| Reserved | `-1` (never expires) | A reserved entry stays answerable indefinitely, because a request reusing the key with a different body must keep being rejected. A finite `ttl` would make that answer depend on whether a background sweep had happened to run. |
| Completed | `Ttl + 60s` | Unreachable once the store treats it as absent, so deleting it cannot change an answer. |

Reservations are removed by `AbandonAsync` or superseded on completion, so the only documents that
accumulate are those whose process was killed between reserving and responding.

**Reservation timeout depends on host clocks.** Takeover compares the reading instance's clock
against a `reservedAt` written by another instance, so the effective timeout is the configured
`ReservationTimeout` shifted by the clock skew between the two hosts. This is not a new failure
mode — the timeout is a liveness bound that already permits taking over a handler that is merely
slow rather than dead — but it moves the boundary. Set `ReservationTimeout` comfortably above the
slowest expected handler *plus* the skew tolerated across the fleet, and keep hosts NTP-synchronised.
`InMemoryIdempotencyStore` reads one clock and is not exposed to this.

**Abandon never deletes a completed entry.** The middleware calls `AbandonAsync` from the failure
paths around `CompleteAsync`, so an unconditional delete would destroy a response that was already
durably recorded and let the retry re-run the handler.

### Stored document

```json
{
  "id": "aWRlbXBvdGVuY3kta2V5",
  "scope": "actor:user-1",
  "key": "idempotency-key",
  "fingerprint": "sha256:...",
  "reservationId": "3f2a...",
  "reservedAt": 1767268800000,
  "completedAt": 1767268801000,
  "snapshot": { "statusCode": 201, "headers": { }, "body": "eyJ9", "fingerprint": "sha256:..." },
  "ttl": 86460
}
```

The example shows a completed entry; while reserved, `reservationId` is set, `completedAt` and
`snapshot` are absent, and `ttl` is `-1`.

Item ids are Base64Url-encoded because idempotency keys are client-supplied and may contain
`/`, `\`, `?`, or `#`, none of which Cosmos DB permits in an id. Encoding rather than hashing keeps
the mapping collision-free and reversible. The longest key `IdempotencyOptions.MaxKeyLength`
permits (200) encodes well inside the 1023-byte id limit.

## `CosmosIdempotencyContainer`

| Member | Notes |
| --- | --- |
| `const string PartitionKeyPath` | `"/scope"` |
| `static Task<Container> CreateIfNotExistsAsync(Database database, string containerId = "idempotency", int? throughput = null, CancellationToken ct = default)` | Creates the container with `DefaultTimeToLive = -1`, which enables TTL while expiring nothing by default, leaving each item's own `ttl` in control. |

> Per-item `ttl` is **ignored** unless the container enables TTL. A container provisioned without
> `DefaultTimeToLive` accumulates idempotency entries forever.

## Registration

`public static class CosmosIdempotencyServiceCollectionExtensions`

| Signature | Notes |
| --- | --- |
| `AddCosmosIdempotencyStore(this IServiceCollection services, Func<IServiceProvider, Container> containerFactory)` | Use when the container is provisioned at startup or shared with other components. |
| `AddCosmosIdempotencyStore(this IServiceCollection services, string databaseId, string containerId = "idempotency")` | Resolves `CosmosClient` from the container. Addresses the container; does not create it. |

> **No `TrellisServiceBuilder.UseXxx()` slot**, by design. These are store registrations, in the
> same category as `AddInMemoryIdempotencyStore()`: they do not participate in pipeline ordering,
> so a builder slot would add surface without removing a decision.

## Operational notes

**Partition design.** Partitioning by scope keeps every operation for one key inside a single
logical partition, which is what makes the create-or-conditionally-replace protocol atomic. Because
scope derives from the actor or tenant, keys spread evenly in multi-tenant hosts. A host that
mounts `UseTrellisIdempotency()` *before* authentication resolves every request to the shared
`anonymous` scope, concentrating all traffic on one partition and capping it at that partition's
throughput.

**Request-unit cost.** RU charge scales with item size, so an entry costs roughly what its captured
response body costs to write. With `MaxResponseBodyBytes` at its 1 MiB default, one completion can
exceed 100 RU. Lower the cap, or keep large payloads out of the snapshot.

**Newtonsoft.Json.** The Cosmos DB SDK v3 uses it internally but omits it from its package
dependencies so consumers pin the version, and its build target fails without an explicit
reference. This package therefore has a transitive `Newtonsoft.Json` dependency.

**Stream APIs.** The store uses `CreateItemStreamAsync` / `ReadItemStreamAsync` /
`ReplaceItemStreamAsync` / `DeleteItemStreamAsync` rather than the typed overloads, because a `409`
is a *normal* outcome on the replay path — every retry produces one. The typed overloads raise
`CosmosException` for it, which would put exception throwing on the hot path.

## Verification

The store is covered by the `Trellis.Testing.Idempotency` conformance suite running against a real
Cosmos DB emulator — all 17 contract rules, not a substitute. Because expiry is enforced against an
injected `TimeProvider`, the reservation-takeover and TTL rules run against real Cosmos DB with a
fake clock and complete in the time of ordinary round trips.

The suite skips, visibly, when no emulator is reachable. The decision-ordering rules and key
encoding are additionally covered by tests that need no emulator.

Tests that require the emulator are marked `[Trait("Category", "Integration")]`, matching the
repository convention for SQL Server-backed tests, so CI excludes them via
`--filter-not-trait "Category=Integration"`. To run them:

```powershell
# Requires the Azure Cosmos DB emulator on https://localhost:8081/
dotnet test Trellis.Asp.Idempotency.Cosmos/tests/Trellis.Asp.Idempotency.Cosmos.Tests.csproj `
  --filter-trait "Category=Integration"
```

| Run | Tests |
|---|---|
| Default / CI | 36 — decision ordering, document model, key encoding, service registration; no emulator needed |
| `Category=Integration` | 19 — the 17 conformance rules plus the two `ttl` tests, against real Cosmos DB |

Because CI cannot see the integration run, `CosmosIdempotencyStore.cs` and
`CosmosIdempotencyContainer.cs` are exempted in `codecov.yml`. The exemption is per-file, not
per-package: everything reachable without a live service — decision ordering, the document model,
and service registration — stays gated by the coverage target.

## See also

- [`trellis-api-asp.md`](trellis-api-asp.md#iidempotencystore) — the middleware and the contract.
- [`trellis-api-testing-idempotency.md`](trellis-api-testing-idempotency.md#quick-start) — the
  conformance suite, for authoring a different store.
