namespace Trellis.EntityFrameworkCore;

/// <summary>
/// The inbound entry point for idempotent integration-event consumption. A transport adapter — a broker
/// consumer or the in-process path — builds an <see cref="IntegrationEnvelope"/> and calls
/// <see cref="DispatchAsync"/>; the dispatcher deduplicates on
/// <c>(ConsumerId, MessageId)</c> and invokes the event's integration-event handlers exactly once,
/// committing their side effects together with the dedup record.
/// </summary>
public interface IInboxDispatcher
{
    /// <summary>
    /// Dispatches the integration event in <paramref name="envelope"/> to its handlers, unless its
    /// <c>(ConsumerId, MessageId)</c> pair was already processed — in which case it is a no-op. The
    /// handlers' side effects and the dedup record commit atomically.
    /// </summary>
    /// <param name="envelope">The message envelope to dispatch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DispatchAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken = default);
}
