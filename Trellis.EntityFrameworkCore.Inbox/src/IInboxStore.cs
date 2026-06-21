namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Store seam for inbox deduplication — a service-provider interface (SPI) for the dedup record. The shipped
/// EF Core implementation records that a <c>(ConsumerId, MessageId)</c> pair has been processed by enrolling
/// the row in the consumer's <c>DbContext</c>, so it commits in the dispatcher's single
/// <c>SaveChanges</c> together with the handler side effects (or not at all). An alternative store may
/// replace it, but it can preserve that all-or-nothing atomicity only by enrolling the dedup record in the
/// same unit of work the dispatcher commits; a store on a separate connection/transaction reduces the
/// guarantee to best-effort.
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
