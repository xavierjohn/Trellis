namespace Trellis.Asp.Idempotency;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// In-memory <see cref="IIdempotencyStore"/> implementation backed by a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Intended for unit / integration tests and
/// for single-instance development hosts; not safe across multiple instances or process
/// restarts. Production hosts that need cross-instance persistence should supply their own
/// <see cref="IIdempotencyStore"/> implementation backed by shared storage.
/// </summary>
/// <remarks>
/// <para>
/// All mutations use compare-and-swap (CAS) loops on the underlying dictionary so concurrent
/// reservers contend cleanly: exactly one observes <see cref="IdempotencyReservationOutcome.Reserved"/>;
/// the rest observe <see cref="IdempotencyReservationOutcome.AlreadyInFlight"/>.
/// </para>
/// <para>
/// Time is provided by <see cref="TimeProvider"/> so tests can advance the clock deterministically
/// to exercise TTL and reservation-timeout transitions.
/// </para>
/// <para>
/// To bound memory in long-running hosts, the store performs an opportunistic sweep at the
/// start of every <see cref="TryReserveAsync"/> call: at most once per <c>Ttl/8</c>, one
/// caller walks the dictionary and removes completed entries past
/// <see cref="IdempotencyOptions.Ttl"/>. Never-retried keys are therefore evicted within
/// ~ <c>Ttl + Ttl/8</c> of expiry as long as other traffic continues to arrive. A
/// completely idle store retains expired entries indefinitely; restart the host or call
/// <see cref="TryReserveAsync"/> to drain. Outstanding reservations are not swept so
/// unrelated traffic cannot orphan a still-running slow handler; same-key retries continue
/// to take over reservations past <see cref="IdempotencyOptions.ReservationTimeout"/>.
/// </para>
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private sealed class Entry
    {
        public required string Fingerprint { get; init; }

        // Non-null while a reservation is outstanding; cleared to null when CompleteAsync stores
        // a Snapshot or when the entry transitions to the completed state.
        public string? ReservationId { get; init; }

        public DateTimeOffset ReservedAt { get; init; }

        public IdempotencyResponseSnapshot? Snapshot { get; init; }

        public DateTimeOffset CompletedAt { get; init; }
    }

    private readonly ConcurrentDictionary<(string Scope, string Key), Entry> _store = new();
    private readonly IdempotencyOptions _options;
    private readonly TimeProvider _time;
    private long _lastSweepTicks;

    /// <summary>
    /// Creates a new <see cref="InMemoryIdempotencyStore"/>.
    /// </summary>
    /// <param name="options">Idempotency options (used for <see cref="IdempotencyOptions.Ttl"/> and <see cref="IdempotencyOptions.ReservationTimeout"/>).</param>
    /// <param name="timeProvider">Optional time provider for deterministic testing; defaults to <see cref="TimeProvider.System"/>.</param>
    public InMemoryIdempotencyStore(IdempotencyOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _lastSweepTicks = _time.GetUtcNow().Ticks;
    }

    /// <summary>
    /// Number of entries currently held by the store (completed snapshots plus outstanding
    /// reservations). Exposed for tests so the opportunistic sweep can be observed.
    /// </summary>
    internal int Count => _store.Count;

    /// <inheritdoc/>
    public ValueTask<IdempotencyReservationOutcome> TryReserveAsync(
        string scope,
        string key,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fingerprint);

        MaybeSweepExpired();

        var compositeKey = (scope, key);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = _time.GetUtcNow();

            if (_store.TryGetValue(compositeKey, out var existing))
            {
                if (existing.Snapshot is not null)
                {
                    // Completed entry: honour TTL.
                    var age = now - existing.CompletedAt;
                    if (age >= _options.Ttl)
                    {
                        // Try to atomically remove the expired entry so the next loop iteration
                        // observes an empty slot and adds a fresh reservation. If removal fails
                        // because another caller already mutated the entry, retry.
                        var kvp = new KeyValuePair<(string, string), Entry>(compositeKey, existing);
                        ((ICollection<KeyValuePair<(string, string), Entry>>)_store).Remove(kvp);
                        continue;
                    }

                    if (string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        return ValueTask.FromResult<IdempotencyReservationOutcome>(
                            new IdempotencyReservationOutcome.Replay(existing.Snapshot));
                    }

                    return ValueTask.FromResult<IdempotencyReservationOutcome>(
                        new IdempotencyReservationOutcome.BodyHashMismatch(existing.Fingerprint));
                }

                // Reserved entry: same-key, different-body must hard-fail per IETF semantics.
                if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return ValueTask.FromResult<IdempotencyReservationOutcome>(
                        new IdempotencyReservationOutcome.BodyHashMismatch(existing.Fingerprint));
                }

                // Reserved entry: honour reservation timeout.
                var elapsed = now - existing.ReservedAt;
                if (elapsed >= _options.ReservationTimeout)
                {
                    // Take over by replacing the stale reservation atomically. Fingerprint
                    // already matches so the takeover is a clean retry of the original request.
                    var takeover = new Entry
                    {
                        Fingerprint = fingerprint,
                        ReservationId = Guid.NewGuid().ToString("N"),
                        ReservedAt = now,
                    };
                    if (_store.TryUpdate(compositeKey, takeover, existing))
                    {
                        return ValueTask.FromResult<IdempotencyReservationOutcome>(
                            new IdempotencyReservationOutcome.Reserved(takeover.ReservationId!));
                    }

                    continue;
                }

                var retryAfter = _options.ReservationTimeout - elapsed;
                return ValueTask.FromResult<IdempotencyReservationOutcome>(
                    new IdempotencyReservationOutcome.AlreadyInFlight(retryAfter));
            }

            var fresh = new Entry
            {
                Fingerprint = fingerprint,
                ReservationId = Guid.NewGuid().ToString("N"),
                ReservedAt = now,
            };
            if (_store.TryAdd(compositeKey, fresh))
            {
                return ValueTask.FromResult<IdempotencyReservationOutcome>(
                    new IdempotencyReservationOutcome.Reserved(fresh.ReservationId!));
            }
            // Another caller added between TryGetValue and TryAdd; retry.
        }
    }

    /// <inheritdoc/>
    public ValueTask CompleteAsync(
        string scope,
        string key,
        string reservationId,
        IdempotencyResponseSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reservationId);
        ArgumentNullException.ThrowIfNull(snapshot);

        var compositeKey = (scope, key);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_store.TryGetValue(compositeKey, out var existing))
                return ValueTask.CompletedTask;

            if (existing.Snapshot is not null ||
                !string.Equals(existing.ReservationId, reservationId, StringComparison.Ordinal))
            {
                // Either already completed (another finisher won), or the reservation has been
                // taken over by a different request. Late callers are silently ignored so they
                // cannot corrupt the active state.
                return ValueTask.CompletedTask;
            }

            var completed = new Entry
            {
                Fingerprint = existing.Fingerprint,
                ReservationId = null,
                ReservedAt = existing.ReservedAt,
                Snapshot = snapshot,
                CompletedAt = _time.GetUtcNow(),
            };

            if (_store.TryUpdate(compositeKey, completed, existing))
                return ValueTask.CompletedTask;
            // Another caller mutated the entry between TryGetValue and TryUpdate; retry.
        }
    }

    /// <inheritdoc/>
    public ValueTask AbandonAsync(
        string scope,
        string key,
        string reservationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reservationId);

        var compositeKey = (scope, key);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_store.TryGetValue(compositeKey, out var existing))
                return ValueTask.CompletedTask;

            if (existing.Snapshot is not null ||
                !string.Equals(existing.ReservationId, reservationId, StringComparison.Ordinal))
            {
                // Completed entries and stale-reservation abandons are silently ignored.
                return ValueTask.CompletedTask;
            }

            var kvp = new KeyValuePair<(string, string), Entry>(compositeKey, existing);
            if (((ICollection<KeyValuePair<(string, string), Entry>>)_store).Remove(kvp))
                return ValueTask.CompletedTask;
            // Another caller mutated the entry between TryGetValue and Remove; retry.
        }
    }

    /// <summary>
    /// Cadence at which the opportunistic sweep walks the store to evict expired entries.
    /// Tied to <see cref="IdempotencyOptions.Ttl"/> (one eighth) so even an aggressively
    /// short TTL still gets several sweep opportunities per TTL window; the worst-case
    /// extra residency for a never-retried entry is therefore ~ <c>Ttl + Ttl/8</c>.
    /// </summary>
    private TimeSpan SweepInterval => TimeSpan.FromTicks(Math.Max(1L, _options.Ttl.Ticks / 8));

    private void MaybeSweepExpired()
    {
        var nowTicks = _time.GetUtcNow().Ticks;
        var lastTicks = Interlocked.Read(ref _lastSweepTicks);
        if (nowTicks - lastTicks < SweepInterval.Ticks)
            return;

        // Claim the sweep slot atomically: at most one caller per cadence does the walk.
        if (Interlocked.CompareExchange(ref _lastSweepTicks, nowTicks, lastTicks) != lastTicks)
            return;

        SweepExpired();
    }

    private void SweepExpired()
    {
        var now = _time.GetUtcNow();
        var ttl = _options.Ttl;
        var collection = (ICollection<KeyValuePair<(string, string), Entry>>)_store;
        foreach (var kvp in _store)
        {
            var entry = kvp.Value;
            // Only sweep completed snapshots. Outstanding reservations are deliberately left
            // alone so unrelated traffic cannot orphan a slow handler whose run is still
            // legitimate: same-key retries continue to take over stuck reservations past
            // ReservationTimeout. The reservation population is bounded by concurrent
            // in-flight idempotent requests and does not grow unboundedly.
            if (entry.Snapshot is not null && (now - entry.CompletedAt) >= ttl)
            {
                // Snapshot-bound Remove: leaves the entry alone if another caller replaced it
                // (for example a takeover or a retry that re-reserved the same key).
                collection.Remove(kvp);
            }
        }
    }
}