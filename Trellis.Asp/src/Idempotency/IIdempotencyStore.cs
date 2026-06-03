namespace Trellis.Asp.Idempotency;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Backing store for <c>IdempotencyMiddleware</c>. Implementations are responsible for
/// atomically reserving keys, persisting completed snapshots, and honouring TTL and
/// reservation-timeout expiry.
/// </summary>
/// <remarks>
/// <para>
/// All operations are scoped: the same <c>key</c> under two different <c>scope</c> values does
/// not collide. The scope is computed by <see cref="IIdempotencyScopeResolver"/> and typically
/// embeds an actor or tenant identifier so two clients cannot replay each other's responses by
/// guessing a key.
/// </para>
/// <para>
/// <see cref="CompleteAsync"/> and <see cref="AbandonAsync"/> must be conditional on the
/// <c>reservationId</c> returned by <see cref="TryReserveAsync"/>. If the reservation has since
/// been taken over by another request (because <see cref="IdempotencyOptions.ReservationTimeout"/>
/// elapsed), the late call is silently ignored.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically reserves <paramref name="key"/> for the request identified by
    /// <paramref name="fingerprint"/> under the given <paramref name="scope"/>, or surfaces the
    /// state of an existing reservation or completed snapshot.
    /// </summary>
    /// <param name="scope">Caller-resolved scope (for example an actor identifier).</param>
    /// <param name="key">Client-supplied idempotency key, post-validation.</param>
    /// <param name="fingerprint">SHA-256 URL-safe base64 (no padding) digest of the canonicalised request, as produced by <c>IdempotencyFingerprint.Compute</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IdempotencyReservationOutcome> TryReserveAsync(
        string scope,
        string key,
        string fingerprint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the captured <paramref name="snapshot"/> against the reservation identified by
    /// <paramref name="reservationId"/>. Silently ignored if the reservation has since been
    /// taken over.
    /// </summary>
    /// <param name="scope">Same scope passed to <see cref="TryReserveAsync"/>.</param>
    /// <param name="key">Same key passed to <see cref="TryReserveAsync"/>.</param>
    /// <param name="reservationId">Token returned in <see cref="IdempotencyReservationOutcome.Reserved"/>.</param>
    /// <param name="snapshot">Captured response to replay on subsequent matching requests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask CompleteAsync(
        string scope,
        string key,
        string reservationId,
        IdempotencyResponseSnapshot snapshot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases the reservation identified by <paramref name="reservationId"/> without storing
    /// a snapshot. Used when the handler throws, returns a 5xx response, or the post-handler
    /// finalization (snapshot capture or <see cref="CompleteAsync"/> call) fails.
    /// </summary>
    /// <remarks>
    /// <para>Silently ignored if the reservation has since been taken over by another request
    /// (because <see cref="IdempotencyOptions.ReservationTimeout"/> elapsed).</para>
    /// <para><b>Idempotency with <see cref="CompleteAsync"/>.</b> <see cref="IdempotencyMiddleware"/>
    /// may invoke this method <i>after</i> <see cref="CompleteAsync"/> partially succeeded — for
    /// example, a durable store that wrote the snapshot to the database but then threw during a
    /// secondary acknowledgement step. Implementations MUST NOT delete a snapshot that
    /// <see cref="CompleteAsync"/> already persisted under the same <paramref name="reservationId"/>;
    /// only an in-flight reservation may be released. A safe pattern is to short-circuit when the
    /// store-level record already has a non-null snapshot, leaving the completed entry intact for
    /// replay.</para>
    /// </remarks>
    /// <param name="scope">Same scope passed to <see cref="TryReserveAsync"/>.</param>
    /// <param name="key">Same key passed to <see cref="TryReserveAsync"/>.</param>
    /// <param name="reservationId">Token returned in <see cref="IdempotencyReservationOutcome.Reserved"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AbandonAsync(
        string scope,
        string key,
        string reservationId,
        CancellationToken cancellationToken);
}
