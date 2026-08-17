namespace Trellis.Testing.Idempotency;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Trellis.Asp.Idempotency;
using Xunit;

/// <summary>
/// Executable specification of the <see cref="IIdempotencyStore"/> contract. Inherit this class,
/// implement <see cref="CreateStoreAsync"/>, and the suite runs against your store.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>IdempotencyMiddleware</c> ships with only
/// <c>InMemoryIdempotencyStore</c>, which is explicitly not safe across instances or process
/// restarts, so every production deployment supplies its own store backed by Redis, Cosmos DB,
/// a relational database, or something bespoke. The rules those implementations must satisfy are
/// subtle, and getting one wrong fails silently — a replayed request executes twice and the
/// caller is charged twice, with no exception anywhere. This suite turns each rule into a test.
/// </para>
/// <para>
/// <b>Clocks.</b> Two tests need time to pass: reservation takeover and TTL expiry. A store built
/// on <see cref="TimeProvider"/> should override <see cref="AdvanceAsync"/> to advance its fake
/// clock, leaving <see cref="ReservationTimeout"/> and <see cref="Ttl"/> at their defaults. A
/// store whose expiry is enforced by a remote server (Redis <c>PX</c>, Cosmos DB <c>ttl</c>)
/// cannot have its clock faked, so it should instead override <see cref="ReservationTimeout"/>
/// and <see cref="Ttl"/> to values of a few seconds and let <see cref="AdvanceAsync"/> keep its
/// default real delay.
/// </para>
/// <para>
/// <b>Server-side expiry is not sufficient on its own.</b> Cosmos DB deletes expired items on a
/// best-effort background sweep, so an item can outlive its <c>ttl</c> and still be returned by a
/// read. Redis is prompt but may be configured with an eviction policy that removes a live entry
/// early. A conforming store therefore records its own expiry timestamp and re-checks it on read
/// rather than trusting the server to have deleted the row. <see cref="Reserve_after_the_ttl_elapses_treats_a_completed_entry_as_absent"/>
/// catches an implementation that trusts the sweep.
/// </para>
/// <para>
/// <b>Isolation.</b> Every test instance gets a fresh <see cref="Scope"/> containing a GUID, so a
/// suite may safely run against a shared Redis or Cosmos DB instance, in parallel, without one
/// test observing another's keys.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class RedisIdempotencyStoreConformanceTests : IdempotencyStoreConformance
/// {
///     // A remote server owns expiry, so use short real timeouts and a real delay.
///     protected override TimeSpan ReservationTimeout => TimeSpan.FromSeconds(2);
///     protected override TimeSpan Ttl => TimeSpan.FromSeconds(4);
///
///     protected override ValueTask&lt;IIdempotencyStore&gt; CreateStoreAsync(IdempotencyOptions options) =&gt;
///         new(new RedisIdempotencyStore(ConnectionMultiplexer, options));
/// }
/// </code>
/// </example>
public abstract class IdempotencyStoreConformance
{
    /// <summary>
    /// Creates the store under test, configured with <paramref name="options"/>. Called once per
    /// test. The <see cref="IdempotencyOptions.ReservationTimeout"/> and
    /// <see cref="IdempotencyOptions.Ttl"/> on <paramref name="options"/> are taken from
    /// <see cref="ReservationTimeout"/> and <see cref="Ttl"/> and must be honoured, or the
    /// expiry tests will not mean anything.
    /// </summary>
    /// <param name="options">Options the store must be configured with.</param>
    protected abstract ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options);

    /// <summary>
    /// How long a reservation is held before another request may take it over. Override with a
    /// value of a few seconds when expiry is enforced by a remote server and
    /// <see cref="AdvanceAsync"/> therefore has to delay for real.
    /// </summary>
    protected virtual TimeSpan ReservationTimeout => TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a completed snapshot remains replayable. Override with a value of a few seconds
    /// when expiry is enforced by a remote server and <see cref="AdvanceAsync"/> therefore has to
    /// delay for real.
    /// </summary>
    protected virtual TimeSpan Ttl => TimeSpan.FromHours(1);

    /// <summary>
    /// Number of callers used by <see cref="Concurrent_reservations_of_one_key_grant_exactly_one_winner"/>.
    /// Lower it if the store under test is a remote service with limited throughput.
    /// </summary>
    protected virtual int ConcurrentReservationAttempts => 32;

    /// <summary>
    /// Makes <paramref name="duration"/> elapse from the store's point of view. Defaults to a real
    /// delay, which is correct for a store whose expiry a remote server enforces. Override it to
    /// advance a <c>FakeTimeProvider</c> when the store reads the clock in-process, so the suite
    /// runs instantly.
    /// </summary>
    /// <param name="duration">How much time must appear to pass.</param>
    protected virtual Task AdvanceAsync(TimeSpan duration) => Task.Delay(duration);

    /// <summary>
    /// Scope used by every test on this instance. xUnit constructs one instance per test, so each
    /// test gets its own scope and cannot collide with another running against the same backing
    /// store.
    /// </summary>
    protected string Scope { get; } = $"trellis-conformance-{Guid.NewGuid():N}";

    /// <summary>
    /// Builds a snapshot with the given fingerprint and a one-byte body, for tests that only need
    /// to tell two snapshots apart.
    /// </summary>
    /// <param name="fingerprint">Fingerprint to record on the snapshot.</param>
    /// <param name="bodyMarker">Single body byte, used to identify which snapshot was stored.</param>
    protected static IdempotencyResponseSnapshot SnapshotFor(string fingerprint, byte bodyMarker = 0x7B) =>
        new(
            StatusCode: 201,
            Headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json"],
            },
            Body: [bodyMarker],
            Fingerprint: fingerprint);

    /// <summary>
    /// Asserts that <paramref name="actual"/> carries the same data as <paramref name="expected"/>.
    /// </summary>
    /// <remarks>
    /// Compared field by field rather than with record equality on purpose.
    /// <see cref="IdempotencyResponseSnapshot"/> is a record whose <c>Headers</c> and <c>Body</c>
    /// members compare by <em>reference</em>, so an equality assertion would pass only for a store
    /// that hands back the very instance it was given. Any store that serialises — which is every
    /// durable store — returns an equal-but-distinct instance and would fail for no good reason.
    /// </remarks>
    /// <param name="actual">Snapshot returned by the store.</param>
    /// <param name="expected">Snapshot originally handed to <see cref="IIdempotencyStore.CompleteAsync"/>.</param>
    protected static void ShouldMatch(IdempotencyResponseSnapshot actual, IdempotencyResponseSnapshot expected)
    {
        actual.StatusCode.Should().Be(expected.StatusCode);
        actual.Fingerprint.Should().Be(expected.Fingerprint);
        actual.Body.Should().Equal(expected.Body);

        // IdempotencyResponseSnapshot documents header names as case-insensitive, so a store that
        // round-trips them through a dictionary carrying an ordinal comparer, or that normalizes
        // casing on the way out, is still conforming. Compare through an explicit
        // OrdinalIgnoreCase view rather than inheriting whichever comparer the store happened to
        // use, so the suite does not reject a store for something the contract permits.
        var actualHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in actual.Headers)
            actualHeaders[name] = values;

        actualHeaders.Should().HaveCount(expected.Headers.Count);
        foreach (var (name, values) in expected.Headers)
        {
            actualHeaders.Should().ContainKey(name);
            actualHeaders[name].Should().Equal(values, "header '{0}' must replay verbatim", name);
        }
    }

    private async ValueTask<IIdempotencyStore> NewStoreAsync()
    {
        var options = new IdempotencyOptions
        {
            ReservationTimeout = ReservationTimeout,
            Ttl = Ttl,
        };

        return await CreateStoreAsync(options);
    }

    private async ValueTask<(IIdempotencyStore Store, string ReservationId)> ReserveAsync(
        string key, string fingerprint)
    {
        var store = await NewStoreAsync();
        var reservationId = await ReserveOnAsync(store, key, fingerprint);
        return (store, reservationId);
    }

    private static async ValueTask<string> ReserveOnAsync(
        IIdempotencyStore store, string scope, string key, string fingerprint)
    {
        var outcome = await store.TryReserveAsync(scope, key, fingerprint, CancellationToken.None);
        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>(
            "the suite expected this key to be free");
        return ((IdempotencyReservationOutcome.Reserved)outcome).ReservationId;
    }

    private ValueTask<string> ReserveOnAsync(IIdempotencyStore store, string key, string fingerprint) =>
        ReserveOnAsync(store, Scope, key, fingerprint);

    // ---- Reserving -------------------------------------------------------------------------

    /// <summary>
    /// A free key must be granted, and the reservation id must be usable as a token, so the store
    /// can later tell this request apart from one that took the slot over.
    /// </summary>
    [Fact]
    public async Task Reserve_on_a_free_key_returns_Reserved_with_a_non_empty_reservation_id()
    {
        var store = await NewStoreAsync();

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
        ((IdempotencyReservationOutcome.Reserved)outcome).ReservationId.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// While one request holds the slot, a second with the same fingerprint must be told to retry
    /// rather than being allowed to execute concurrently. The suggested wait must be positive and
    /// no longer than the configured timeout, since the slot cannot outlive it.
    /// </summary>
    [Fact]
    public async Task Reserve_while_another_request_holds_the_key_returns_AlreadyInFlight()
    {
        var (store, _) = await ReserveAsync("key-1", "fp-1");

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.AlreadyInFlight>();
        var retryAfter = ((IdempotencyReservationOutcome.AlreadyInFlight)outcome).RetryAfter;
        retryAfter.Should().BePositive();
        retryAfter.Should().BeLessThanOrEqualTo(ReservationTimeout,
            "the reservation expires after ReservationTimeout, so waiting longer than that is never required");
    }

    /// <summary>
    /// Scope keeps tenants and actors apart. Without this, one client could replay another's
    /// response — or block it — simply by guessing a key.
    /// </summary>
    [Fact]
    public async Task Reserve_under_a_different_scope_does_not_collide()
    {
        var store = await NewStoreAsync();
        await ReserveOnAsync(store, $"{Scope}-alice", "shared-key", "fp-1");

        var outcome = await store.TryReserveAsync(
            $"{Scope}-bob", "shared-key", "fp-1", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
    }

    /// <summary>
    /// The point of the whole mechanism: once a response is recorded, a retry replays it instead
    /// of running the handler again.
    /// </summary>
    [Fact]
    public async Task Reserve_after_Complete_replays_the_snapshot_for_a_matching_fingerprint()
    {
        var (store, reservationId) = await ReserveAsync("key-1", "fp-1");
        var snapshot = SnapshotFor("fp-1");
        await store.CompleteAsync(Scope, "key-1", reservationId, snapshot, CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.Replay>();
        ShouldMatch(((IdempotencyReservationOutcome.Replay)outcome).Snapshot, snapshot);
    }

    /// <summary>
    /// Reusing a key with a different body is a client bug. Replaying the old response would hide
    /// it and silently drop the new request, so the store must report the mismatch instead.
    /// </summary>
    [Fact]
    public async Task Reserve_after_Complete_with_a_different_fingerprint_returns_BodyHashMismatch()
    {
        var (store, reservationId) = await ReserveAsync("key-1", "fp-original");
        await store.CompleteAsync(
            Scope, "key-1", reservationId, SnapshotFor("fp-original"), CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-different", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.BodyHashMismatch>();
        ((IdempotencyReservationOutcome.BodyHashMismatch)outcome).StoredFingerprint
            .Should().Be("fp-original");
    }

    /// <summary>
    /// The same protection applies before the first request finishes: a different body must not be
    /// able to queue behind, or take over, an in-flight reservation.
    /// </summary>
    [Fact]
    public async Task Reserve_while_in_flight_with_a_different_fingerprint_returns_BodyHashMismatch()
    {
        var (store, _) = await ReserveAsync("key-1", "fp-1");

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-different", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.BodyHashMismatch>();
        ((IdempotencyReservationOutcome.BodyHashMismatch)outcome).StoredFingerprint.Should().Be("fp-1");
    }

    // ---- Expiry ----------------------------------------------------------------------------

    /// <summary>
    /// A crashed or hung handler must not hold a key forever. Once the timeout passes, a retry of
    /// the same request takes the slot over and receives a new reservation id — the old id is what
    /// lets the store reject the original request if it ever finishes.
    /// </summary>
    [Fact]
    public async Task Reserve_after_the_reservation_timeout_takes_over_with_a_new_reservation_id()
    {
        var (store, first) = await ReserveAsync("key-1", "fp-1");

        await AdvanceAsync(ReservationTimeout + TimeSpan.FromSeconds(1));

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
        ((IdempotencyReservationOutcome.Reserved)outcome).ReservationId.Should().NotBe(first,
            "a takeover must issue a new token so the displaced request's completion can be rejected");
    }

    /// <summary>
    /// Takeover is for retries of the same request. An expired reservation must still refuse a
    /// different body rather than letting it claim the key.
    /// </summary>
    [Fact]
    public async Task Reserve_after_the_reservation_timeout_with_a_different_fingerprint_returns_BodyHashMismatch()
    {
        var (store, _) = await ReserveAsync("key-1", "fp-1");

        await AdvanceAsync(ReservationTimeout + TimeSpan.FromSeconds(1));

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-different", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.BodyHashMismatch>();
        ((IdempotencyReservationOutcome.BodyHashMismatch)outcome).StoredFingerprint.Should().Be("fp-1");
    }

    /// <summary>
    /// Snapshots are retained for <see cref="Ttl"/> and no longer. Afterwards the key behaves as
    /// if it had never been used.
    /// </summary>
    /// <remarks>
    /// A store that leaves expiry entirely to a server-side sweep fails here, because Cosmos DB
    /// deletes expired items on a best-effort background pass and can still return one. Record an
    /// expiry timestamp and re-check it on read.
    /// </remarks>
    [Fact]
    public async Task Reserve_after_the_ttl_elapses_treats_a_completed_entry_as_absent()
    {
        var (store, reservationId) = await ReserveAsync("key-1", "fp-1");
        await store.CompleteAsync(
            Scope, "key-1", reservationId, SnapshotFor("fp-1"), CancellationToken.None);

        await AdvanceAsync(Ttl + TimeSpan.FromSeconds(1));

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);

        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
    }

    // ---- Completing and abandoning ----------------------------------------------------------

    /// <summary>
    /// The displaced request must not be able to publish its response after losing the slot.
    /// Otherwise the client replays the output of an execution that the takeover already
    /// superseded.
    /// </summary>
    [Fact]
    public async Task Complete_with_a_reservation_id_that_lost_the_slot_is_ignored()
    {
        var (store, first) = await ReserveAsync("key-1", "fp-1");

        await AdvanceAsync(ReservationTimeout + TimeSpan.FromSeconds(1));
        var takeover = await ReserveOnAsync(store, "key-1", "fp-1");

        // The displaced request finally finishes and tries to publish its response.
        await store.CompleteAsync(
            Scope, "key-1", first, SnapshotFor("fp-1", bodyMarker: 0x01), CancellationToken.None);

        // The request that actually owns the slot completes normally.
        var winning = SnapshotFor("fp-1", bodyMarker: 0x02);
        await store.CompleteAsync(Scope, "key-1", takeover, winning, CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);
        outcome.Should().BeOfType<IdempotencyReservationOutcome.Replay>();
        ShouldMatch(((IdempotencyReservationOutcome.Replay)outcome).Snapshot, winning);
    }

    /// <summary>
    /// A handler that fails must release the key immediately so the client can retry, rather than
    /// making it wait out the reservation timeout.
    /// </summary>
    [Fact]
    public async Task Abandon_releases_the_slot_for_an_immediate_retry()
    {
        var (store, reservationId) = await ReserveAsync("key-1", "fp-1");

        await store.AbandonAsync(Scope, "key-1", reservationId, CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);
        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
    }

    /// <summary>
    /// The mirror of the stale-completion rule: a displaced request failing must not release the
    /// slot out from under the request that took it over, which is still running.
    /// </summary>
    [Fact]
    public async Task Abandon_with_a_reservation_id_that_lost_the_slot_is_ignored()
    {
        var (store, first) = await ReserveAsync("key-1", "fp-1");

        await AdvanceAsync(ReservationTimeout + TimeSpan.FromSeconds(1));
        await ReserveOnAsync(store, "key-1", "fp-1");

        await store.AbandonAsync(Scope, "key-1", first, CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);
        outcome.Should().BeOfType<IdempotencyReservationOutcome.AlreadyInFlight>(
            "the takeover reservation is still running and must keep the slot");
    }

    /// <summary>
    /// The middleware calls <see cref="IIdempotencyStore.AbandonAsync"/> from the failure paths
    /// around <see cref="IIdempotencyStore.CompleteAsync"/>. A store that persisted the snapshot
    /// and then threw on a secondary step would, if abandon deleted unconditionally, destroy a
    /// response it had already durably recorded — and the retry would execute the handler again.
    /// </summary>
    [Fact]
    public async Task Abandon_after_Complete_must_not_delete_the_persisted_snapshot()
    {
        var (store, reservationId) = await ReserveAsync("key-1", "fp-1");
        var snapshot = SnapshotFor("fp-1");
        await store.CompleteAsync(Scope, "key-1", reservationId, snapshot, CancellationToken.None);

        await store.AbandonAsync(Scope, "key-1", reservationId, CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "key-1", "fp-1", CancellationToken.None);
        outcome.Should().BeOfType<IdempotencyReservationOutcome.Replay>(
            "AbandonAsync must not delete a snapshot CompleteAsync already persisted");
        ShouldMatch(((IdempotencyReservationOutcome.Replay)outcome).Snapshot, snapshot);
    }

    /// <summary>
    /// Both calls are best-effort cleanup that the middleware may make after the entry has already
    /// expired, so an unknown key is a no-op rather than an error.
    /// </summary>
    [Fact]
    public async Task Complete_on_a_key_that_was_never_reserved_is_ignored()
    {
        var store = await NewStoreAsync();

        await store.CompleteAsync(
            Scope, "never-reserved", "bogus", SnapshotFor("fp-1"), CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "never-reserved", "fp-1", CancellationToken.None);
        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>(
            "a completion for a key that was never reserved must not create an entry");
    }

    /// <inheritdoc cref="Complete_on_a_key_that_was_never_reserved_is_ignored"/>
    [Fact]
    public async Task Abandon_on_a_key_that_was_never_reserved_is_ignored()
    {
        var store = await NewStoreAsync();

        await store.AbandonAsync(Scope, "never-reserved", "bogus", CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "never-reserved", "fp-1", CancellationToken.None);
        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
    }

    // ---- Concurrency -----------------------------------------------------------------------

    /// <summary>
    /// The load-bearing guarantee. If reservation is not atomic, two racing callers both believe
    /// they own the key and the handler runs twice — precisely the double execution the middleware
    /// exists to prevent. A store must reserve with a single atomic operation such as Redis
    /// <c>SET NX</c>, a Cosmos DB insert that conflicts on duplicate id, or a unique-index insert.
    /// </summary>
    [Fact]
    public async Task Concurrent_reservations_of_one_key_grant_exactly_one_winner()
    {
        var store = await NewStoreAsync();
        var callers = ConcurrentReservationAttempts;
        callers.Should().BeGreaterThanOrEqualTo(2, "a race needs at least two callers");

        // Every caller blocks on one gate and is released together. Parallel.ForEachAsync would
        // instead cap concurrency at Environment.ProcessorCount, so on a single-CPU host or
        // container the calls would run serially and a read-then-write store would pass this rule
        // — leaving the suite's most important guarantee silently unenforced.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, callers).Select(async _ =>
        {
            await gate.Task;
            return await store.TryReserveAsync(Scope, "race-key", "fp-1", CancellationToken.None);
        }).ToArray();

        gate.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        outcomes.OfType<IdempotencyReservationOutcome.Reserved>().Should().ContainSingle(
            "exactly one caller may hold the slot; every other caller must be told it is in flight");
        outcomes.OfType<IdempotencyReservationOutcome.AlreadyInFlight>().Should().HaveCount(callers - 1,
            "nothing has been completed yet, so no caller may observe Replay or BodyHashMismatch");
    }

    /// <summary>
    /// Reservations belong to their key. Traffic on unrelated keys must not clear one — a store
    /// that sweeps expired entries too eagerly would orphan a slow handler and let its response be
    /// lost.
    /// </summary>
    [Fact]
    public async Task An_in_flight_reservation_survives_traffic_on_other_keys()
    {
        var (store, slow) = await ReserveAsync("slow", "fp-slow");

        await ReserveOnAsync(store, "unrelated", "fp-unrelated");

        var snapshot = SnapshotFor("fp-slow");
        await store.CompleteAsync(Scope, "slow", slow, snapshot, CancellationToken.None);

        var outcome = await store.TryReserveAsync(Scope, "slow", "fp-slow", CancellationToken.None);
        outcome.Should().BeOfType<IdempotencyReservationOutcome.Replay>(
            "the slow handler still owned its reservation, so its completion must have been accepted");
        ShouldMatch(((IdempotencyReservationOutcome.Replay)outcome).Snapshot, snapshot);
    }
}
