# Trellis.Asp.Idempotency.Cosmos

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.Idempotency.Cosmos.svg)](https://www.nuget.org/packages/Trellis.Asp.Idempotency.Cosmos)

Cosmos DB-backed `IIdempotencyStore` for the Trellis idempotency middleware — safe across
instances and process restarts.

## Installation
```bash
dotnet add package Trellis.Asp.Idempotency.Cosmos
```

## Quick Example
```csharp
// Provision once at startup: partitioned on /scope, TTL enabled.
var database = (await cosmosClient.CreateDatabaseIfNotExistsAsync("billing")).Database;
await CosmosIdempotencyContainer.CreateIfNotExistsAsync(database);

builder.Services.AddSingleton(cosmosClient);
builder.Services.AddTrellisIdempotency(o => o.MaxResponseBodyBytes = 64 * 1024);
builder.Services.AddCosmosIdempotencyStore("billing");

app.UseTrellisIdempotency();
```

## Why Cosmos DB

| Contract requirement | Cosmos DB primitive |
| --- | --- |
| Atomic reserve | `CreateItem` → `409 Conflict` on duplicate id, decided on the primary replica |
| Conditional complete / abandon | Native ETag `IfMatchEtag`, no scripting |
| Expiry | Per-item `ttl`, no sweeper process |
| No silent eviction | Never drops a live entry under memory pressure, unlike a Redis cache under `allkeys-lru` |

## Key Features
- **Correct under Session consistency** — reservations are granted only by an atomic create or an
  ETag-conditional replace, never on the strength of a read, so a stale replica cannot cause a
  double execution. Strong consistency is not required.
- **Expiry enforced in-process** — Cosmos DB's TTL sweep is best-effort and can return an item past
  its `ttl`, so the store re-checks its own timestamps rather than trusting the service.
- **No exceptions on the hot path** — uses the stream APIs, because a `409` is the *normal* outcome
  of every replay.
- **Verified, not asserted** — passes all 17 rules of the `Trellis.Testing.Idempotency` conformance
  suite against a real Cosmos DB emulator.

## Documentation
See the [API reference](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-asp-idempotency-cosmos.md)
for the document model, partitioning and RU-cost guidance, and the concurrency protocol.
