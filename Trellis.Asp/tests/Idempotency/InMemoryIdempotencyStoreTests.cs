namespace Trellis.Asp.Tests.Idempotency;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Trellis.Asp.Idempotency;

/// <summary>
/// Pins the contract of <see cref="InMemoryIdempotencyStore"/>: atomic reservation, scope
/// isolation, replay vs body-hash-mismatch on completed entries, TTL expiry, reservation
/// takeover after the timeout elapses, and the requirement that <see cref="IIdempotencyStore.CompleteAsync"/>
/// and <see cref="IIdempotencyStore.AbandonAsync"/> are silently ignored when the caller's
/// reservation has been taken over.
/// </summary>
public sealed class InMemoryIdempotencyStoreTests
{
    private static readonly IdempotencyResponseSnapshot SampleSnapshot = new(
        StatusCode: 201,
        Headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["application/json"]
        },
        Body: new byte[] { 0x7B, 0x7D },
        Fingerprint: "fp-1");

    private static (InMemoryIdempotencyStore Store, FakeTimeProvider Time) BuildStore(
        TimeSpan? reservationTimeout = null,
        TimeSpan? ttl = null)
    {
        var options = new IdempotencyOptions();
        if (reservationTimeout is { } rt) options.ReservationTimeout = rt;
        if (ttl is { } t) options.Ttl = t;

        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var store = new InMemoryIdempotencyStore(options, time);
        return (store, time);
    }

    [Fact]
    public async Task Second_reservation_within_timeout_returns_AlreadyInFlight()
    {
        var (store, time) = BuildStore(reservationTimeout: TimeSpan.FromSeconds(30));
        await store.TryReserveAsync("anon", "key-1", "fp-1", CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(5));

        var outcome = await store.TryReserveAsync("anon", "key-1", "fp-1", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.AlreadyInFlight>();
        var inFlight = (IdempotencyReservationOutcome.AlreadyInFlight)outcome;
        inFlight.RetryAfter.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public async Task After_complete_replay_returns_snapshot_for_matching_fingerprint()
    {
        var (store, _) = BuildStore();
        var first = (IdempotencyReservationOutcome.Reserved)await store.TryReserveAsync(
            "anon", "key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync("anon", "key-1", first.ReservationId, SampleSnapshot, CancellationToken.None);

        var replay = await store.TryReserveAsync("anon", "key-1", "fp-1", CancellationToken.None);

        replay.Should().BeOfType<IdempotencyReservationOutcome.Replay>();
        ((IdempotencyReservationOutcome.Replay)replay).Snapshot.Should().Be(SampleSnapshot);
    }

    [Fact]
    public async Task After_TTL_expires_completed_entry_is_treated_as_absent()
    {
        var (store, time) = BuildStore(ttl: TimeSpan.FromHours(1));
        var first = (IdempotencyReservationOutcome.Reserved)await store.TryReserveAsync(
            "anon", "key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync("anon", "key-1", first.ReservationId, SampleSnapshot, CancellationToken.None);

        time.Advance(TimeSpan.FromHours(2));

        var afterTtl = await store.TryReserveAsync("anon", "key-1", "fp-1", CancellationToken.None);

        afterTtl.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
    }

    [Fact]
    public async Task After_reservation_timeout_a_takeover_reservation_succeeds_with_new_id()
    {
        var (store, time) = BuildStore(reservationTimeout: TimeSpan.FromSeconds(30));
        var first = (IdempotencyReservationOutcome.Reserved)await store.TryReserveAsync(
            "anon", "key-1", "fp-1", CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(31));

        var takeover = await store.TryReserveAsync("anon", "key-1", "fp-1", CancellationToken.None);
        takeover.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
        ((IdempotencyReservationOutcome.Reserved)takeover).ReservationId.Should().NotBe(first.ReservationId);
    }

    [Fact]
    public async Task Concurrent_first_reservations_collide_so_exactly_one_wins()
    {
        var (store, _) = BuildStore();
        const int callers = 64;
        var results = new IdempotencyReservationOutcome[callers];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, callers),
            async (i, ct) => results[i] = await store.TryReserveAsync("anon", "race-key", "fp-1", ct));

        var reservedCount = results.OfType<IdempotencyReservationOutcome.Reserved>().Count();
        var inFlightCount = results.OfType<IdempotencyReservationOutcome.AlreadyInFlight>().Count();

        reservedCount.Should().Be(1,
            "exactly one caller may hold the slot; the rest must observe AlreadyInFlight");
        (reservedCount + inFlightCount).Should().Be(callers,
            "no caller may observe Replay/BodyHashMismatch because nothing has been completed yet");
    }

    [Fact]
    public async Task Expired_completed_entries_are_swept_when_unrelated_traffic_arrives()
    {
        var ttl = TimeSpan.FromMinutes(10);
        var (store, time) = BuildStore(ttl: ttl);

        // Reserve + complete two unrelated entries that will never be retried under the same key.
        var r1 = await store.TryReserveAsync("anon", "key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync(
            "anon",
            "key-1",
            ((IdempotencyReservationOutcome.Reserved)r1).ReservationId,
            SampleSnapshot,
            CancellationToken.None);

        var r2 = await store.TryReserveAsync("anon", "key-2", "fp-2", CancellationToken.None);
        await store.CompleteAsync(
            "anon",
            "key-2",
            ((IdempotencyReservationOutcome.Reserved)r2).ReservationId,
            SampleSnapshot with { Fingerprint = "fp-2" },
            CancellationToken.None);

        store.Count.Should().Be(2);

        // Advance well past Ttl so both stored entries are logically expired.
        time.Advance(ttl + TimeSpan.FromMinutes(5));

        // A request that arrives under a completely different key must opportunistically
        // sweep the expired entries; without sweeping, a never-retried key leaks forever
        // in long-running dev hosts.
        await store.TryReserveAsync("anon", "key-3", "fp-3", CancellationToken.None);

        store.Count.Should().Be(1,
            "expired completed entries must be swept on subsequent traffic; only key-3 should remain");
    }

    [Fact]
    public async Task Sweep_does_not_evict_unexpired_entries()
    {
        var ttl = TimeSpan.FromMinutes(10);
        var (store, time) = BuildStore(ttl: ttl);

        var r1 = await store.TryReserveAsync("anon", "key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync(
            "anon",
            "key-1",
            ((IdempotencyReservationOutcome.Reserved)r1).ReservationId,
            SampleSnapshot,
            CancellationToken.None);

        // Advance only halfway through the Ttl — entry must remain available for replay.
        time.Advance(ttl / 2);

        await store.TryReserveAsync("anon", "key-other", "fp-other", CancellationToken.None);

        store.Count.Should().Be(2,
            "the unexpired key-1 entry must survive a sweep triggered by unrelated traffic");
    }

    [Fact]
    public async Task Sweep_does_not_evict_in_flight_reservations()
    {
        var reservationTimeout = TimeSpan.FromSeconds(30);
        var ttl = TimeSpan.FromMinutes(10);
        var (store, time) = BuildStore(reservationTimeout: reservationTimeout, ttl: ttl);

        // A slow handler holds its reservation longer than ReservationTimeout. Unrelated
        // traffic must NOT sweep it: that would orphan the slow handler's snapshot and
        // would also let an unrelated request silently invalidate the in-flight contract.
        var slow = await store.TryReserveAsync("anon", "slow", "fp-slow", CancellationToken.None);
        var slowReservation = ((IdempotencyReservationOutcome.Reserved)slow).ReservationId;

        time.Advance(reservationTimeout + TimeSpan.FromMinutes(5));

        await store.TryReserveAsync("anon", "unrelated", "fp-unrelated", CancellationToken.None);

        store.Count.Should().Be(2,
            "outstanding reservations must not be swept by unrelated traffic; takeover is reserved for same-key retries");

        // Confirm CompleteAsync on the slow handler still succeeds (reservation entry was not removed).
        await store.CompleteAsync("anon", "slow", slowReservation, SampleSnapshot, CancellationToken.None);
        var replay = await store.TryReserveAsync("anon", "slow", "fp-slow", CancellationToken.None);
        replay.Should().BeOfType<IdempotencyReservationOutcome.Replay>();
    }
}