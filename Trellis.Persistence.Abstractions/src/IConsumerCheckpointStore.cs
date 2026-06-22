namespace Trellis;

/// <summary>
/// Store seam for a pull consumer's durable resume cursor — a service-provider interface (SPI) for the
/// per-<c>ConsumerId</c> position in a source feed, so a consumer resumes where it left off instead of
/// rescanning the whole log on every poll or restart. The shipped EF Core implementation keeps one row per
/// consumer; an alternative store (Redis, a key-value table, the broker's own offset API) may replace it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Performance, not correctness.</b> The checkpoint is an optimization that narrows the scan window; it is
/// NOT the deduplication boundary. A high-water cursor can skip a row that was assigned a low position but
/// committed late — after the cursor had already advanced past it — which a cursor alone can never recover.
/// Correctness stays with the inbox anti-join (<see cref="IInboxStore.FilterUnprocessedAsync"/>) plus the
/// dedup row: scan a window that <i>overlaps</i> the checkpoint (re-read a visibility-lag margin behind it)
/// and let the anti-join skip whatever is already processed. Advance the checkpoint only to a position whose
/// predecessors are all known processed.
/// </para>
/// <para>
/// <b>Opaque position.</b> Trellis does not interpret the <c>position</c> — it is whatever cursor the
/// source feed uses (a sequence number, a UUIDv7 high-water mark, a timestamp, a composite token) serialized
/// to a string. The store persists and returns it verbatim.
/// </para>
/// <para>
/// <b>Last-writer-wins.</b> The cursor is single-valued per <c>ConsumerId</c>. <see cref="SetAsync"/> is an
/// upsert that absorbs a concurrent first write — it retries as an update on a duplicate-key race — so
/// concurrent advancers resolve to last-writer-wins rather than throwing. A shared cursor is still not a
/// coordination primitive (concurrent advances can lose an update); the typical shape is one logical
/// advancer per consumer.
/// </para>
/// </remarks>
public interface IConsumerCheckpointStore
{
    /// <summary>
    /// Returns the last persisted resume position for <paramref name="consumerId"/>, or
    /// <see cref="Maybe{T}.None"/> if the consumer has never checkpointed (start from the beginning of the
    /// feed).
    /// </summary>
    /// <param name="consumerId">The stable subscriber identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resume cursor, or <see cref="Maybe{T}.None"/> if none has been recorded.</returns>
    Task<Maybe<string>> GetAsync(string consumerId, CancellationToken cancellationToken);

    /// <summary>
    /// Durably records <paramref name="position"/> as the resume cursor for <paramref name="consumerId"/>,
    /// overwriting any previous value. Call this only once every row at or before <paramref name="position"/>
    /// (minus the overlap margin) is known processed — see the type remarks.
    /// </summary>
    /// <param name="consumerId">The stable subscriber identifier.</param>
    /// <param name="position">The opaque resume cursor to persist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetAsync(string consumerId, string position, CancellationToken cancellationToken);
}
