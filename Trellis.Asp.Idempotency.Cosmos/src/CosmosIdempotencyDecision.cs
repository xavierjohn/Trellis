namespace Trellis.Asp.Idempotency.Cosmos;

using System;

/// <summary>
/// What <see cref="CosmosIdempotencyStore"/> should do with an existing item.
/// </summary>
internal enum IdempotencyDocumentAction
{
    /// <summary>Return the stored snapshot; the handler must not run again.</summary>
    Replay,

    /// <summary>The key is in use by a request with a different body.</summary>
    BodyHashMismatch,

    /// <summary>A live reservation is held by another request.</summary>
    AlreadyInFlight,

    /// <summary>The reservation has timed out; a same-body retry may claim it.</summary>
    TakeOver,

    /// <summary>The completed entry has outlived its TTL and must behave as if it were gone.</summary>
    TreatAsAbsent,
}

/// <summary>Outcome of classifying an existing item against an incoming request.</summary>
/// <param name="Action">What the store should do next.</param>
/// <param name="RetryAfter">
/// How long the caller should wait, meaningful only for
/// <see cref="IdempotencyDocumentAction.AlreadyInFlight"/>, where it always falls within
/// <c>(0, ReservationTimeout]</c>.
/// </param>
internal readonly record struct IdempotencyDocumentDecision(
    IdempotencyDocumentAction Action,
    TimeSpan RetryAfter);

/// <summary>
/// The store's decision logic, kept free of Cosmos DB types so it can be tested exhaustively
/// without an emulator and so the ordering rules are reviewable in one place.
/// </summary>
internal static class CosmosIdempotencyDecision
{
    /// <summary>
    /// Decides how to treat <paramref name="document"/> for a request carrying
    /// <paramref name="fingerprint"/>.
    /// </summary>
    /// <remarks>
    /// The two branches check fingerprint at deliberately different points, matching the contract:
    /// a <em>completed</em> entry is tested for TTL expiry first, so an expired snapshot behaves as
    /// absent and a key may legitimately be reused with a new body; a <em>reserved</em> entry is
    /// tested for fingerprint first, so a different body is rejected even after the reservation has
    /// timed out and would otherwise be taken over.
    /// </remarks>
    /// <param name="document">The item read from Cosmos DB.</param>
    /// <param name="fingerprint">Fingerprint of the incoming request.</param>
    /// <param name="now">Current time.</param>
    /// <param name="options">Configured TTL and reservation timeout.</param>
    public static IdempotencyDocumentDecision Classify(
        CosmosIdempotencyDocument document,
        string fingerprint,
        DateTimeOffset now,
        IdempotencyOptions options)
    {
        if (document.Snapshot is not null)
        {
            var completedAt = DateTimeOffset.FromUnixTimeMilliseconds(document.CompletedAtUnixMs ?? 0);
            if (now - completedAt >= options.Ttl)
                return new(IdempotencyDocumentAction.TreatAsAbsent, TimeSpan.Zero);

            return string.Equals(document.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? new(IdempotencyDocumentAction.Replay, TimeSpan.Zero)
                : new(IdempotencyDocumentAction.BodyHashMismatch, TimeSpan.Zero);
        }

        if (!string.Equals(document.Fingerprint, fingerprint, StringComparison.Ordinal))
            return new(IdempotencyDocumentAction.BodyHashMismatch, TimeSpan.Zero);

        var elapsed = now - DateTimeOffset.FromUnixTimeMilliseconds(document.ReservedAtUnixMs);
        if (elapsed >= options.ReservationTimeout)
            return new(IdempotencyDocumentAction.TakeOver, TimeSpan.Zero);

        // A reservation written by a host whose clock runs ahead lands in the future, making
        // elapsed negative and the remainder longer than the timeout itself. Clamping keeps
        // RetryAfter within (0, ReservationTimeout] as the contract requires, so a client is never
        // told to wait longer than the reservation can possibly live.
        var retryAfter = options.ReservationTimeout - elapsed;
        return new(
            IdempotencyDocumentAction.AlreadyInFlight,
            retryAfter > options.ReservationTimeout ? options.ReservationTimeout : retryAfter);
    }
}
