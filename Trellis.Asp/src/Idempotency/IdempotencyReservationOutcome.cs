namespace Trellis.Asp.Idempotency;

using System;

/// <summary>
/// Closed hierarchy of outcomes from <see cref="IIdempotencyStore.TryReserveAsync"/>. Exactly
/// one of <see cref="Reserved"/>, <see cref="AlreadyInFlight"/>, <see cref="Replay"/>, or
/// <see cref="BodyHashMismatch"/> is returned for every call.
/// </summary>
public abstract record IdempotencyReservationOutcome
{
    private IdempotencyReservationOutcome()
    {
    }

    /// <summary>
    /// The key was free and has now been reserved for the calling request. The caller must
    /// proceed to execute the handler and then call either
    /// <see cref="IIdempotencyStore.CompleteAsync"/> with the captured snapshot or
    /// <see cref="IIdempotencyStore.AbandonAsync"/> on failure, passing the
    /// <see cref="ReservationId"/> so the store can reject a stale completion from a request
    /// whose reservation has since been taken over.
    /// </summary>
    /// <param name="ReservationId">
    /// Opaque token that identifies this specific reservation. Required to complete or abandon
    /// the entry; mismatched tokens are silently ignored by the store.
    /// </param>
    public sealed record Reserved(string ReservationId) : IdempotencyReservationOutcome;

    /// <summary>
    /// Another request with the same key and scope is currently being processed and the
    /// caller's reservation request was rejected. The middleware translates this into a
    /// <c>409 Conflict</c> with a <c>Retry-After</c> header derived from
    /// <see cref="RetryAfter"/>.
    /// </summary>
    /// <param name="RetryAfter">Recommended client wait before retrying.</param>
    public sealed record AlreadyInFlight(TimeSpan RetryAfter) : IdempotencyReservationOutcome;

    /// <summary>
    /// A completed snapshot exists for this key+scope+fingerprint. The caller replays
    /// <paramref name="Snapshot"/> verbatim to the client and skips handler execution.
    /// </summary>
    /// <param name="Snapshot">The previously captured response.</param>
    public sealed record Replay(IdempotencyResponseSnapshot Snapshot) : IdempotencyReservationOutcome;

    /// <summary>
    /// A snapshot or in-flight reservation exists for this key+scope but the request's
    /// fingerprint does not match the stored one. The middleware translates this into the
    /// configured <see cref="IdempotencyOptions.MismatchStatusCode"/> (default 422). Returned
    /// both for completed snapshots (replay would not match) and for an in-flight reservation
    /// whose fingerprint differs from the new request — the latter prevents the second caller
    /// from taking over the slot with a different body.
    /// </summary>
    /// <param name="StoredFingerprint">
    /// Fingerprint of the request that originally created the stored snapshot or holds the
    /// in-flight reservation, exposed for diagnostic logging only. Never echoed to clients.
    /// </param>
    public sealed record BodyHashMismatch(string StoredFingerprint) : IdempotencyReservationOutcome;
}