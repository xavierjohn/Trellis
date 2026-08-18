namespace Trellis;

/// <summary>
/// Store seam for inbox deduplication — a service-provider interface (SPI) for the dedup record. The shipped
/// EF Core implementation records that a <c>(ConsumerId, MessageId)</c> pair has been processed by enrolling
/// the row in the consumer's unit of work, so it commits in the dispatcher's single transaction together with
/// the handler side effects (or not at all). An alternative store may replace it, but it can preserve that
/// all-or-nothing atomicity only by enrolling the dedup record in the same unit of work the dispatcher
/// commits; a store on a separate connection/transaction reduces the guarantee to best-effort.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Records the message described by <paramref name="record"/> as processed by
    /// <paramref name="consumerId"/>, enrolling the dedup record in the current unit of work. Returns
    /// <see langword="true"/> if it was newly recorded, or <see langword="false"/> if the
    /// <c>(ConsumerId, MessageId)</c> pair was already processed — a duplicate the caller should skip.
    /// </summary>
    /// <param name="consumerId">The stable subscriber identifier.</param>
    /// <param name="record">The dedup record describing the message being processed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<bool> TryRecordAsync(string consumerId, InboxRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the subset of <paramref name="messageIds"/> that <paramref name="consumerId"/> has not yet
    /// processed — those without a <c>(ConsumerId, MessageId)</c> dedup row — preserving the input order.
    /// </summary>
    /// <remarks>
    /// This powers the gap-free <b>inbox-as-cursor / anti-join</b> pull model: scan a window of the source
    /// feed and dispatch every row whose <c>MessageId</c> this query returns, rather than tracking a fragile
    /// high-water cursor that can skip a row committed out of sequence order. It is an optimization, not the
    /// correctness boundary — a row may be processed by another worker between this query and dispatch, which
    /// still deduplicates. It is a pure read and stages nothing in the unit of work.
    /// </remarks>
    /// <param name="consumerId">The stable subscriber identifier.</param>
    /// <param name="messageIds">The candidate message ids to test — typically a window of the source feed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The ids from <paramref name="messageIds"/> with no dedup row yet, in their original order.</returns>
    Task<IReadOnlyList<Guid>> FilterUnprocessedAsync(
        string consumerId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);
}