namespace Trellis.Mediator;

/// <summary>
/// The inbound entry point for idempotent integration-event consumption. A transport adapter — a broker
/// consumer or the in-process path — builds an <see cref="IntegrationEnvelope"/> and calls
/// <see cref="DispatchAsync"/>; the dispatcher deduplicates on
/// <c>(ConsumerId, MessageId)</c> so the event's integration-event handlers' side effects commit exactly
/// once (effectively-once), together with the dedup record.
/// </summary>
public interface IInboxDispatcher
{
    /// <summary>
    /// Dispatches the integration event in <paramref name="envelope"/> to its handlers, unless its
    /// <c>(ConsumerId, MessageId)</c> pair was already processed — in which case it commits nothing. The
    /// handlers' side effects and the dedup record commit atomically.
    /// </summary>
    /// <param name="envelope">The message envelope to dispatch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="InboxDispatchOutcome.Processed"/> when this call's handlers committed their side effects, or
    /// <see cref="InboxDispatchOutcome.SkippedDuplicate"/> when the message had already been processed so this
    /// call committed nothing (on the fast path no handler runs; in a lost duplicate-key race the handlers ran
    /// but rolled back). Both outcomes mean the message is durably accounted for, so a pull consumer can
    /// advance its checkpoint on either.
    /// </returns>
    Task<InboxDispatchOutcome> DispatchAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken = default);
}