namespace Trellis.Asp.Idempotency.Cosmos;

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Cosmos DB-backed <see cref="IIdempotencyStore"/>, safe across instances and process restarts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity.</b> A reservation is claimed with a single <c>CreateItem</c>: Cosmos DB rejects a
/// duplicate id within a partition with <c>409 Conflict</c>, executed on the partition's primary
/// replica, so exactly one concurrent caller can win. Every subsequent mutation — taking over a
/// timed-out reservation, recording a response, releasing a slot — is an ETag-conditional replace
/// or delete, so a caller whose view of the item is stale is rejected with <c>412</c> and retries
/// rather than clobbering a newer state.
/// </para>
/// <para>
/// <b>Session consistency is safe here.</b> Reads on a Session-consistency account may be served
/// by a replica that has not yet seen another instance's write, so the read following a <c>409</c>
/// can return a stale item — or, briefly, <c>404</c>. Neither can cause a double execution,
/// because the store never grants a reservation on the strength of a read: it grants only when an
/// atomic create succeeds, or when an ETag-conditional replace succeeds. A stale read simply
/// produces a <c>412</c> or <c>404</c> on the follow-up write, and the operation retries. The
/// worst observable effect is a spurious <c>AlreadyInFlight</c>, which the caller retries.
/// </para>
/// <para>
/// <b>Expiry is enforced here, not by Cosmos DB.</b> Per-item <c>ttl</c> is set as a storage
/// reclamation backstop only. Cosmos DB deletes expired items on a best-effort background sweep,
/// so an item can outlive its <c>ttl</c> and still be returned by a read; the store therefore
/// re-checks the stored timestamps on every read and treats a TTL-expired snapshot as absent.
/// Deletion is only ever allowed to fall on a document the store's own rules have already made
/// unreachable, so a <em>reserved</em> document never expires — see
/// <see cref="CosmosIdempotencyDocument.Ttl"/>. Reservations are removed by
/// <see cref="AbandonAsync"/> or superseded on completion, so the only documents that accumulate
/// are those whose process was killed between reserving and responding.
/// </para>
/// <para>
/// <b>Reservation timeout depends on host clocks.</b> Takeover compares this instance's clock
/// against a <c>reservedAt</c> written by another instance, so the effective timeout is the
/// configured <see cref="IdempotencyOptions.ReservationTimeout"/> shifted by the clock skew
/// between the two hosts. This does not add a failure mode — the timeout is a liveness bound that
/// already permits taking over a handler that is merely slow rather than dead — but it does move
/// the boundary. Set <see cref="IdempotencyOptions.ReservationTimeout"/> comfortably above the
/// slowest expected handler <em>plus</em> the skew tolerated across the fleet, and keep hosts
/// NTP-synchronised. A single-instance store such as <c>InMemoryIdempotencyStore</c> reads one
/// clock and is not exposed to this.
/// </para>
/// <para>
/// <b>Cost.</b> Request-unit charge scales with item size, so an idempotency entry costs roughly
/// what its captured response body costs to write. With
/// <see cref="IdempotencyOptions.MaxResponseBodyBytes"/> at its 1 MiB default a single completion
/// can exceed 100 RU. Lower the cap, or keep large payloads out of the snapshot.
/// </para>
/// </remarks>
public sealed class CosmosIdempotencyStore : IIdempotencyStore
{
    private const int MaxAttempts = 8;

    private readonly Container _container;
    private readonly IdempotencyOptions _options;
    private readonly TimeProvider _time;
    private readonly int _ttlSeconds;

    /// <summary>
    /// Creates a store over an existing Cosmos DB container.
    /// </summary>
    /// <param name="container">
    /// Container whose partition key path is <c>/scope</c> and whose <c>DefaultTimeToLive</c> is
    /// enabled, so per-item <c>ttl</c> takes effect. See
    /// <see cref="CosmosIdempotencyContainer.CreateIfNotExistsAsync"/>.
    /// </param>
    /// <param name="options">Idempotency options supplying TTL and reservation timeout.</param>
    /// <param name="timeProvider">Clock, defaulting to <see cref="TimeProvider.System"/>.</param>
    public CosmosIdempotencyStore(
        Container container, IdempotencyOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(options);

        _container = container;
        _options = options;
        _time = timeProvider ?? TimeProvider.System;

        // Applied only to completed documents, whose logical lifetime is bounded: Classify treats
        // one as absent once it outlives Ttl measured from completedAt, so deleting it after that
        // cannot change an answer. A minute of slack absorbs clock skew between the app and the
        // service. Reserved documents are answerable indefinitely and so never expire; see
        // CosmosIdempotencyDocument.Ttl.
        _ttlSeconds = (int)Math.Min(int.MaxValue, Math.Ceiling(_options.Ttl.TotalSeconds) + 60);
    }

    /// <inheritdoc/>
    public async ValueTask<IdempotencyReservationOutcome> TryReserveAsync(
        string scope, string key, string fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fingerprint);

        var id = EncodeId(key);
        var partitionKey = new PartitionKey(scope);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reservation = NewReservation(id, scope, key, fingerprint);
            using (var created = await _container.CreateItemStreamAsync(
                Serialize(reservation), partitionKey, NoContentResponse, cancellationToken)
                .ConfigureAwait(false))
            {
                if (created.IsSuccessStatusCode)
                    return new IdempotencyReservationOutcome.Reserved(reservation.ReservationId!);

                if (created.StatusCode != HttpStatusCode.Conflict)
                    created.EnsureSuccessStatusCode();
            }

            var existing = await ReadAsync(id, partitionKey, cancellationToken).ConfigureAwait(false);
            if (existing is null)
                continue; // Swept or not yet visible on this replica; try to claim it again.

            var (document, etag) = existing.Value;
            var decision = CosmosIdempotencyDecision.Classify(
                document, fingerprint, _time.GetUtcNow(), _options);

            switch (decision.Action)
            {
                case IdempotencyDocumentAction.Replay:
                    return new IdempotencyReservationOutcome.Replay(ToSnapshot(document.Snapshot!));

                case IdempotencyDocumentAction.BodyHashMismatch:
                    return new IdempotencyReservationOutcome.BodyHashMismatch(document.Fingerprint);

                case IdempotencyDocumentAction.AlreadyInFlight:
                    return new IdempotencyReservationOutcome.AlreadyInFlight(decision.RetryAfter);

                case IdempotencyDocumentAction.TakeOver:
                case IdempotencyDocumentAction.TreatAsAbsent:
                    var claim = NewReservation(id, scope, key, fingerprint);
                    if (await TryReplaceAsync(claim, id, partitionKey, etag, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        return new IdempotencyReservationOutcome.Reserved(claim.ReservationId!);
                    }

                    continue; // Lost the race; re-read and re-decide.

                default:
                    throw new InvalidOperationException($"Unhandled decision '{decision.Action}'.");
            }
        }

        throw new InvalidOperationException(
            $"Could not reserve idempotency key after {MaxAttempts} attempts because the entry was " +
            "concurrently modified on every attempt. This indicates extreme contention on a single key.");
    }

    /// <inheritdoc/>
    public async ValueTask CompleteAsync(
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

        var id = EncodeId(key);
        var partitionKey = new PartitionKey(scope);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await ReadAsync(id, partitionKey, cancellationToken).ConfigureAwait(false);
            if (existing is null)
                return; // Never reserved, or already expired: best-effort, so nothing to do.

            var (document, etag) = existing.Value;
            if (!OwnsReservation(document, reservationId))
                return; // Already completed, or this reservation was taken over.

            document.ReservationId = null;
            document.Snapshot = FromSnapshot(snapshot);
            document.CompletedAtUnixMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
            document.Ttl = _ttlSeconds;

            if (await TryReplaceAsync(document, id, partitionKey, etag, cancellationToken)
                .ConfigureAwait(false))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Could not record the idempotent response after {MaxAttempts} attempts because the " +
            "entry was concurrently modified on every attempt. Failing loudly rather than silently " +
            "dropping the snapshot, which would let a retry re-execute the handler.");
    }

    /// <inheritdoc/>
    public async ValueTask AbandonAsync(
        string scope, string key, string reservationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reservationId);

        var id = EncodeId(key);
        var partitionKey = new PartitionKey(scope);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await ReadAsync(id, partitionKey, cancellationToken).ConfigureAwait(false);
            if (existing is null)
                return;

            var (document, etag) = existing.Value;

            // A completed entry is never deleted here. The middleware calls AbandonAsync from the
            // failure paths around CompleteAsync, so an unconditional delete would destroy a
            // response that was already durably recorded and let the retry re-run the handler.
            if (!OwnsReservation(document, reservationId))
                return;

            using var deleted = await _container.DeleteItemStreamAsync(
                id,
                partitionKey,
                new ItemRequestOptions { IfMatchEtag = etag },
                cancellationToken).ConfigureAwait(false);

            if (deleted.IsSuccessStatusCode || deleted.StatusCode == HttpStatusCode.NotFound)
                return;

            if (deleted.StatusCode != HttpStatusCode.PreconditionFailed)
                deleted.EnsureSuccessStatusCode();
        }

        // Deliberately does not throw. The middleware calls AbandonAsync from its failure paths,
        // so throwing here would mask the error that caused the abandon. Losing the release is
        // self-healing: the reservation is taken over by the next same-key retry once
        // ReservationTimeout elapses.
    }

    private static bool OwnsReservation(CosmosIdempotencyDocument document, string reservationId) =>
        document.Snapshot is null
        && string.Equals(document.ReservationId, reservationId, StringComparison.Ordinal);

    private CosmosIdempotencyDocument NewReservation(
        string id, string scope, string key, string fingerprint) =>
        new()
        {
            Id = id,
            Scope = scope,
            Key = key,
            Fingerprint = fingerprint,
            ReservationId = Guid.NewGuid().ToString("N"),
            ReservedAtUnixMs = _time.GetUtcNow().ToUnixTimeMilliseconds(),
            Ttl = CosmosIdempotencyDocument.NeverExpires,
        };

    private async ValueTask<(CosmosIdempotencyDocument Document, string ETag)?> ReadAsync(
        string id, PartitionKey partitionKey, CancellationToken cancellationToken)
    {
        using var response = await _container
            .ReadItemStreamAsync(id, partitionKey, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var document = JsonSerializer.Deserialize(
            response.Content, CosmosIdempotencyJsonContext.Default.CosmosIdempotencyDocument);

        return document is null ? null : (document, response.Headers.ETag);
    }

    private async ValueTask<bool> TryReplaceAsync(
        CosmosIdempotencyDocument document,
        string id,
        PartitionKey partitionKey,
        string etag,
        CancellationToken cancellationToken)
    {
        using var response = await _container.ReplaceItemStreamAsync(
            Serialize(document),
            id,
            partitionKey,
            new ItemRequestOptions { IfMatchEtag = etag, EnableContentResponseOnWrite = false },
            cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return true;

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        return false;
    }

    private static ItemRequestOptions NoContentResponse { get; } =
        new() { EnableContentResponseOnWrite = false };

    private static MemoryStream Serialize(CosmosIdempotencyDocument document)
    {
        var stream = new MemoryStream();
        JsonSerializer.Serialize(
            stream, document, CosmosIdempotencyJsonContext.Default.CosmosIdempotencyDocument);
        stream.Position = 0;
        return stream;
    }

    private static IdempotencyResponseSnapshot ToSnapshot(CosmosResponseSnapshotDocument stored)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in stored.Headers)
            headers[name] = values;

        return new IdempotencyResponseSnapshot(
            stored.StatusCode, headers, stored.Body, stored.Fingerprint);
    }

    private static CosmosResponseSnapshotDocument FromSnapshot(IdempotencyResponseSnapshot snapshot)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in snapshot.Headers)
            headers[name] = values;

        return new CosmosResponseSnapshotDocument
        {
            StatusCode = snapshot.StatusCode,
            Headers = headers,
            Body = snapshot.Body,
            Fingerprint = snapshot.Fingerprint,
        };
    }

    /// <summary>
    /// Encodes an idempotency key into a legal Cosmos DB item id. Keys are client-supplied and
    /// may contain <c>/</c>, <c>\</c>, <c>?</c>, or <c>#</c>, none of which are permitted in an id.
    /// Base64Url is used rather than a hash so the mapping stays collision-free and reversible.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    internal static string EncodeId(string key) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(key));

    /// <summary>Reverses <see cref="EncodeId"/>.</summary>
    /// <param name="id">A Cosmos DB item id produced by <see cref="EncodeId"/>.</param>
    internal static string DecodeId(string id) =>
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(id.ToCharArray()));
}
