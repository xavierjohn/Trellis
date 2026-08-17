namespace Trellis.Testing.Idempotency.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Trellis.Asp.Idempotency;

/// <summary>
/// Defects that <see cref="ConfigurableIdempotencyStore"/> can be told to exhibit, so the
/// conformance suite can be proven to catch each one.
/// </summary>
[Flags]
public enum StoreDefects
{
    /// <summary>Behave correctly.</summary>
    None = 0,

    /// <summary>Accept any reservation id on complete and abandon, including a displaced one.</summary>
    IgnoreReservationId = 1,

    /// <summary>Delete the entry on abandon even after a snapshot has been persisted.</summary>
    DeleteSnapshotOnAbandon = 1 << 1,

    /// <summary>Key entries by key alone, letting one scope observe another's entries.</summary>
    IgnoreScope = 1 << 2,

    /// <summary>Check for an existing entry and then write, without holding a lock across both.</summary>
    NonAtomicReserve = 1 << 3,

    /// <summary>Keep serving a completed snapshot after its TTL has elapsed.</summary>
    IgnoreTtl = 1 << 4,

    /// <summary>Reissue the displaced reservation's id when taking a slot over.</summary>
    ReuseReservationIdOnTakeover = 1 << 5,

    /// <summary>
    /// Lower-case header names on the way out. Not a defect — <c>IdempotencyResponseSnapshot</c>
    /// documents header names as case-insensitive — so the suite must <em>accept</em> this. Used
    /// to prove the suite does not over-specify.
    /// </summary>
    NormalizeHeaderCasing = 1 << 6,
}

/// <summary>
/// A second, independent <see cref="IIdempotencyStore"/> implementation whose behaviour can be
/// selectively broken. With <see cref="StoreDefects.None"/> it is correct, which proves the
/// conformance suite is satisfiable by something other than the store it was extracted from.
/// </summary>
internal sealed class ConfigurableIdempotencyStore(
    IdempotencyOptions options, TimeProvider time, StoreDefects defects) : IIdempotencyStore
{
    private sealed class Entry
    {
        public required string Fingerprint { get; set; }
        public required string ReservationId { get; set; }
        public required DateTimeOffset ExpiresAt { get; set; }
        public IdempotencyResponseSnapshot? Snapshot { get; set; }
        public bool IsCompleted => Snapshot is not null;
    }

    private readonly Dictionary<string, Entry> _entries = [];
    private readonly Lock _gate = new();

    private bool Has(StoreDefects defect) => (defects & defect) != 0;

    private string KeyFor(string scope, string key) =>
        Has(StoreDefects.IgnoreScope) ? key : $"{scope}\u001f{key}";

    public async ValueTask<IdempotencyReservationOutcome> TryReserveAsync(
        string scope, string key, string fingerprint, CancellationToken cancellationToken)
    {
        if (!Has(StoreDefects.NonAtomicReserve))
            return Reserve(scope, key, fingerprint);

        var id = KeyFor(scope, key);
        lock (_gate)
        {
            if (_entries.ContainsKey(id))
                return new IdempotencyReservationOutcome.AlreadyInFlight(options.ReservationTimeout);
        }

        // The window every racing caller slips through when reserve is not one atomic operation.
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        var reservationId = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _entries[id] = new Entry
            {
                Fingerprint = fingerprint,
                ReservationId = reservationId,
                ExpiresAt = time.GetUtcNow() + options.ReservationTimeout,
            };
        }

        return new IdempotencyReservationOutcome.Reserved(reservationId);
    }

    private IdempotencyReservationOutcome Reserve(string scope, string key, string fingerprint)
    {
        var id = KeyFor(scope, key);
        var now = time.GetUtcNow();

        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                var completedAndExpired = entry.IsCompleted
                    && now >= entry.ExpiresAt
                    && !Has(StoreDefects.IgnoreTtl);

                if (completedAndExpired)
                {
                    _entries.Remove(id);
                }
                else
                {
                    if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
                        return new IdempotencyReservationOutcome.BodyHashMismatch(entry.Fingerprint);

                    if (entry.IsCompleted)
                        return new IdempotencyReservationOutcome.Replay(entry.Snapshot!);

                    if (now < entry.ExpiresAt)
                        return new IdempotencyReservationOutcome.AlreadyInFlight(entry.ExpiresAt - now);

                    var takeoverId = Has(StoreDefects.ReuseReservationIdOnTakeover)
                        ? entry.ReservationId
                        : Guid.NewGuid().ToString("N");
                    entry.ReservationId = takeoverId;
                    entry.ExpiresAt = now + options.ReservationTimeout;
                    return new IdempotencyReservationOutcome.Reserved(takeoverId);
                }
            }

            var reservationId = Guid.NewGuid().ToString("N");
            _entries[id] = new Entry
            {
                Fingerprint = fingerprint,
                ReservationId = reservationId,
                ExpiresAt = now + options.ReservationTimeout,
            };
            return new IdempotencyReservationOutcome.Reserved(reservationId);
        }
    }

    public ValueTask CompleteAsync(
        string scope,
        string key,
        string reservationId,
        IdempotencyResponseSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(KeyFor(scope, key), out var entry)
                && !entry.IsCompleted
                && Owns(entry, reservationId))
            {
                entry.Snapshot = Has(StoreDefects.NormalizeHeaderCasing)
                    ? WithLowerCaseHeaderNames(snapshot)
                    : snapshot;
                entry.ExpiresAt = time.GetUtcNow() + options.Ttl;
            }
        }

        return ValueTask.CompletedTask;
    }

    private static IdempotencyResponseSnapshot WithLowerCaseHeaderNames(
        IdempotencyResponseSnapshot snapshot)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (name, values) in snapshot.Headers)
            headers[name.ToLowerInvariant()] = values;

        return snapshot with { Headers = headers };
    }

    public ValueTask AbandonAsync(
        string scope, string key, string reservationId, CancellationToken cancellationToken)
    {
        var id = KeyFor(scope, key);
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                var mayRemove = entry.IsCompleted
                    ? Has(StoreDefects.DeleteSnapshotOnAbandon)
                    : Owns(entry, reservationId);

                if (mayRemove)
                    _entries.Remove(id);
            }
        }

        return ValueTask.CompletedTask;
    }

    private bool Owns(Entry entry, string reservationId) =>
        Has(StoreDefects.IgnoreReservationId)
        || string.Equals(entry.ReservationId, reservationId, StringComparison.Ordinal);
}

/// <summary>
/// Binds <see cref="ConfigurableIdempotencyStore"/> to the conformance suite.
/// </summary>
/// <remarks>
/// Deliberately <c>internal</c> and nested-free but non-public so xUnit does not discover it as a
/// test class — the meta-tests invoke individual rules on it by hand and assert that broken stores
/// make them fail.
/// </remarks>
internal sealed class DefectiveStoreSuite(StoreDefects defects) : IdempotencyStoreConformance
{
    private readonly FakeTimeProvider _time = new();

    protected override ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options) =>
        new(new ConfigurableIdempotencyStore(options, _time, defects));

    protected override Task AdvanceAsync(TimeSpan duration)
    {
        _time.Advance(duration);
        return Task.CompletedTask;
    }

    // Task.Delay in the non-atomic reserve path is real time, so keep the racing caller count
    // modest while still making a non-atomic store fail every run.
    protected override int ConcurrentReservationAttempts => 8;
}
