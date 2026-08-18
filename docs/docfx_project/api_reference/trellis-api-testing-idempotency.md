---
package: Trellis.Testing.Idempotency
namespaces: [Trellis.Testing.Idempotency]
types: [IdempotencyStoreConformance]
version: v3
last_verified: 2026-08-16
audience: [llm]
---
# Trellis.Testing.Idempotency API Reference

Executable conformance suite for `IIdempotencyStore` implementations.

- **Package:** `Trellis.Testing.Idempotency`
- **Namespace:** `Trellis.Testing.Idempotency`
- **Depends on:** `Trellis.Asp` (for the contract types), `xunit.v3`, `FluentAssertions`

## Use this file when

- You are writing an `IIdempotencyStore` over Redis, Cosmos DB, a relational database, or anything bespoke, and need to prove it satisfies the contract.
- You are reviewing an existing store and want the exact rules it must obey — atomicity of reserve, expiry ownership, conditional complete/abandon.
- A production incident suggests duplicate execution or a lost response, and you need a reproducible test rather than a theory.

## Patterns Index

| Goal | Use this | See |
|---|---|---|
| Add conformance coverage to your store | Derive a test class from `IdempotencyStoreConformance` | [Quick start](#quick-start), [`IdempotencyStoreConformance`](#idempotencystoreconformance) |
| Supply your store to the suite | The single required member | [Required member](#required-member) |
| Adapt the suite to a backend with different timing or cleanup needs | Optional overrides and the protected helpers | [Optional overrides](#optional-overrides), [Helpers available to derived classes](#helpers-available-to-derived-classes) |
| Learn the contract itself before implementing | The reserve / expiry / complete / abandon / concurrency rules | [The rules pinned by the suite](#the-rules-pinned-by-the-suite) |
| Check your design against the failures that fail silently | Non-atomic reserve, server-side expiry, eviction, unconditional abandon, snapshot cost | [Implementation traps the suite catches](#implementation-traps-the-suite-catches) |

## Why this package exists

`Trellis.Asp` ships exactly one `IIdempotencyStore`: `InMemoryIdempotencyStore`, which its own
documentation describes as *"not safe across multiple instances or process restarts"*. Every
production deployment therefore writes its own store over Redis, Cosmos DB, a relational database,
or something bespoke.

The contract those stores must satisfy is subtle, and a violation fails **silently**. A store that
reserves non-atomically lets two racing callers both execute the handler; a store whose
`AbandonAsync` deletes unconditionally destroys a response that `CompleteAsync` already persisted.
Neither throws. The symptom is a customer charged twice, weeks later.

This package turns each rule into a test you inherit.

## Quick start

Add a single test class per store:

```csharp
namespace Contoso.Billing.Tests;

using Trellis.Asp.Idempotency;
using Trellis.Testing.Idempotency;

public sealed class RedisIdempotencyStoreConformanceTests : IdempotencyStoreConformance
{
    // Expiry is enforced by the Redis server, whose clock cannot be faked, so use short real
    // timeouts and let AdvanceAsync keep its default real delay.
    protected override TimeSpan ReservationTimeout => TimeSpan.FromSeconds(2);
    protected override TimeSpan Ttl => TimeSpan.FromSeconds(4);

    protected override ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options) =>
        new(new RedisIdempotencyStore(_multiplexer, options));
}
```

For a store that reads the clock in-process through `TimeProvider`, override `AdvanceAsync`
instead and keep the default timeouts, so the suite runs instantly:

```csharp
public sealed class InMemoryIdempotencyStoreConformanceTests : IdempotencyStoreConformance
{
    private readonly FakeTimeProvider _time = new();

    protected override ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options) =>
        new(new InMemoryIdempotencyStore(options, _time));

    protected override Task AdvanceAsync(TimeSpan duration)
    {
        _time.Advance(duration);
        return Task.CompletedTask;
    }
}
```

## `IdempotencyStoreConformance`

`public abstract class IdempotencyStoreConformance`

### Required member

| Member | Notes |
| --- | --- |
| `protected abstract ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options)` | Called once per test. Must honour `options.Ttl` and `options.ReservationTimeout`, or the expiry tests are vacuous. |

### Optional overrides

| Member | Default | Override when |
| --- | --- | --- |
| `protected virtual TimeSpan ReservationTimeout` | 30 s | Expiry is server-enforced — drop to a few seconds. |
| `protected virtual TimeSpan Ttl` | 1 h | Expiry is server-enforced — drop to a few seconds. |
| `protected virtual Task AdvanceAsync(TimeSpan duration)` | `Task.Delay(duration)` | The store reads an injectable clock — advance the fake clock and return `Task.CompletedTask`. |
| `protected virtual int ConcurrentReservationAttempts` | 32 | The store is a remote service with limited throughput. |

### Helpers available to derived classes

| Member | Notes |
| --- | --- |
| `protected string Scope { get; }` | Unique per test instance (xUnit constructs one per `[Fact]`), so a suite is safe against a **shared** Redis or Cosmos DB instance, in parallel. |
| `protected static IdempotencyResponseSnapshot SnapshotFor(string fingerprint, byte bodyMarker = 0x7B)` | Builds a minimal distinguishable snapshot. |
| `protected static void ShouldMatch(IdempotencyResponseSnapshot actual, IdempotencyResponseSnapshot expected)` | Field-by-field comparison. See the warning below. |

> **Do not assert snapshot equality with `Should().Be(...)`.** `IdempotencyResponseSnapshot` is a
> record whose `Headers` and `Body` members compare by **reference**. Record equality therefore
> passes only for a store that returns the very instance it was handed — that is, only for an
> in-memory store. Every durable store serialises and returns an equal-but-distinct instance. Use
> `ShouldMatch`, which compares field by field and matches header names case-insensitively, as the
> snapshot contract specifies.

## The rules pinned by the suite

### Reserving

| Test | Rule |
| --- | --- |
| `Reserve_on_a_free_key_returns_Reserved_with_a_non_empty_reservation_id` | A free key is granted with a usable token. |
| `Reserve_while_another_request_holds_the_key_returns_AlreadyInFlight` | Concurrent duplicate is told to retry; `RetryAfter` is positive and `<= ReservationTimeout`. |
| `Reserve_under_a_different_scope_does_not_collide` | Scope isolates tenants and actors. |
| `Reserve_after_Complete_replays_the_snapshot_for_a_matching_fingerprint` | The core replay guarantee. |
| `Reserve_after_Complete_with_a_different_fingerprint_returns_BodyHashMismatch` | Key reuse with a new body is surfaced, not silently swallowed. `StoredFingerprint` carries the original. |
| `Reserve_while_in_flight_with_a_different_fingerprint_returns_BodyHashMismatch` | Same protection before the first request finishes. |

### Expiry

| Test | Rule |
| --- | --- |
| `Reserve_after_the_reservation_timeout_takes_over_with_a_new_reservation_id` | A crashed handler cannot hold a key forever; takeover issues a **new** id. |
| `Reserve_after_the_reservation_timeout_with_a_different_fingerprint_returns_BodyHashMismatch` | Takeover is for retries of the *same* request only. |
| `Reserve_after_the_ttl_elapses_treats_a_completed_entry_as_absent` | Snapshots are retained for `Ttl` and no longer. |

### Completing and abandoning

| Test | Rule |
| --- | --- |
| `Complete_with_a_reservation_id_that_lost_the_slot_is_ignored` | A displaced request cannot publish its response over the winner's. |
| `Abandon_releases_the_slot_for_an_immediate_retry` | A failed handler frees the key at once. |
| `Abandon_with_a_reservation_id_that_lost_the_slot_is_ignored` | A displaced request cannot free the winner's slot. |
| `Abandon_after_Complete_must_not_delete_the_persisted_snapshot` | The subtle one — see below. |
| `Complete_on_a_key_that_was_never_reserved_is_ignored` | Best-effort cleanup, no throw, no entry created. |
| `Abandon_on_a_key_that_was_never_reserved_is_ignored` | Best-effort cleanup, no throw. |

### Concurrency

| Test | Rule |
| --- | --- |
| `Concurrent_reservations_of_one_key_grant_exactly_one_winner` | Reservation must be a **single atomic operation**. The load-bearing guarantee. Callers are released together from a shared gate rather than through `Parallel.ForEachAsync`, so the race is exercised even on a single-CPU host. |
| `An_in_flight_reservation_survives_traffic_on_other_keys` | Unrelated traffic must not orphan a slow handler's reservation. |

## Implementation traps the suite catches

### Non-atomic reserve

A read-then-write reserve (`GetAsync` then `SetAsync`) passes every single-threaded test and fails
under load, executing the handler twice. Reserve with one atomic primitive:

| Backing store | Atomic reserve |
| --- | --- |
| Redis | `SET key value NX PX <reservationTimeout>` (or a Lua script when takeover logic is needed) |
| Cosmos DB | `CreateItemAsync` — a duplicate id in the same partition returns HTTP 409 |
| Relational | `INSERT` against a unique index on `(scope, key)` — catch the duplicate-key violation |

`IDistributedCache` **cannot** back a conforming store: it has no atomic set-if-not-exists, so
`TryReserveAsync` is not implementable on it. Use `StackExchange.Redis` directly.

### Trusting server-side expiry

Cosmos DB deletes expired items on a best-effort background sweep, so an item can outlive its
`ttl` and still be returned by a read. Record your own expiry timestamp on the item and re-check it
when reading, rather than assuming a returned item is live.

### Eviction

A Redis instance configured with `allkeys-lru` can evict a live reservation under memory pressure,
silently permitting a double execution. Use `noeviction` for the idempotency database, and consider
asserting the policy at startup with `CONFIG GET maxmemory-policy`.

### Unconditional abandon

`IdempotencyMiddleware` calls `AbandonAsync` from the failure paths around `CompleteAsync`. If
`CompleteAsync` persisted the snapshot and then threw on a later step, an `AbandonAsync` that
deletes unconditionally destroys a durably recorded response, and the retry re-executes the
handler. Delete only when the entry is still in the reserved state.

### Snapshot size and cost

`IdempotencyOptions.MaxResponseBodyBytes` defaults to 1 MiB. On Cosmos DB, request-unit cost scales
with item size, so a 1 MiB snapshot costs upwards of 100 RU to write. Lower the cap, or store large
bodies in blob storage and keep a reference in the snapshot.

## See also

- `docs/docfx_project/api_reference/trellis-api-asp.md` — the idempotency middleware and the
  `IIdempotencyStore` contract itself.
- `docs/docfx_project/api_reference/trellis-api-testing-aspnetcore.md` — ASP.NET Core integration
  test helpers.
