namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Store seam for inbox deduplication — a service-provider interface (SPI) so non-EF persistence can supply
/// the same idempotency guarantee. It records that a <c>(ConsumerId, MessageId)</c> pair has been processed
/// <b>within the caller's current unit of work</b>, so the dedup record and the handler side effects commit
/// together (or not at all).
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Records the message carried by <paramref name="envelope"/> as processed by
    /// <paramref name="consumerId"/>, enrolling the dedup record in the current unit of work. Returns
    /// <see langword="true"/> if it was newly recorded, or <see langword="false"/> if the
    /// <c>(ConsumerId, MessageId)</c> pair was already processed — a duplicate the caller should skip.
    /// </summary>
    /// <param name="consumerId">The stable subscriber identifier (see <see cref="InboxOptions.ConsumerId"/>).</param>
    /// <param name="envelope">The message envelope being processed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<bool> TryRecordAsync(string consumerId, IntegrationEnvelope envelope, CancellationToken cancellationToken);
}
