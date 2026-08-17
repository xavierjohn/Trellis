# Trellis.Testing.Idempotency

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Testing.Idempotency.svg)](https://www.nuget.org/packages/Trellis.Testing.Idempotency)

Executable conformance suite for Trellis `IIdempotencyStore` implementations.

## Installation
```bash
dotnet add package Trellis.Testing.Idempotency
```

## Quick Example
```csharp
using Trellis.Testing.Idempotency;

public sealed class RedisIdempotencyStoreConformanceTests : IdempotencyStoreConformance
{
    // Redis enforces expiry server-side, so use short real timeouts.
    protected override TimeSpan ReservationTimeout => TimeSpan.FromSeconds(2);
    protected override TimeSpan Ttl => TimeSpan.FromSeconds(4);

    protected override ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options) =>
        new(new RedisIdempotencyStore(_multiplexer, options));
}
```

That one class runs the full contract: reserve, replay, fingerprint mismatch, reservation
takeover, TTL expiry, abandon semantics, and atomicity under concurrent load.

## Why

`Trellis.Asp` ships only `InMemoryIdempotencyStore`, which is not safe across instances or process
restarts, so production deployments write their own over Redis, Cosmos DB, or a database. A
violation of the contract fails **silently** — a non-atomic reserve lets two racing callers both
execute the handler, and an unconditional `AbandonAsync` destroys a response `CompleteAsync`
already persisted. Nothing throws. The symptom is a customer charged twice.

## Key Features
- **One class per store** — implement `CreateStoreAsync` and inherit 17 contract tests
- **Works with fake or real clocks** — override `AdvanceAsync` for `TimeProvider`-based stores, or
  shorten `Ttl`/`ReservationTimeout` when a remote server owns expiry
- **Safe against shared infrastructure** — each test instance gets a unique `Scope`, so suites can
  run in parallel against one Redis or Cosmos DB instance
- **Structural snapshot comparison** — `ShouldMatch` avoids the reference-equality trap in
  `IdempotencyResponseSnapshot` that would fail every serialising store

## Documentation
See the [API reference](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-testing-idempotency.md)
for the full rule list and the implementation traps the suite catches.
