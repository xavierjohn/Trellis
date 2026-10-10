namespace Trellis.Mediator;

/// <summary>
/// Handles an <see cref="IIntegrationEvent"/>. Implementations are resolved via DI by the default
/// in-process <see cref="IIntegrationEventPublisher"/> or by an inbound <see cref="IInboxDispatcher"/>.
/// The invocation path, not this handler contract, determines delivery and failure semantics.
/// </summary>
/// <typeparam name="TEvent">
/// The concrete integration event type. Dispatch matches the runtime type of the event exactly;
/// base-type and interface-type handlers are <b>not</b> resolved automatically.
/// </typeparam>
/// <remarks>
/// <para>
/// The default publisher provides best-effort local notification: it logs and swallows ordinary
/// handler failures and continues with the remaining handlers. A handler's
/// <see cref="OperationCanceledException"/> propagates when the supplied token is canceled.
/// A swallowed failure does not reach the outbox relay, so the integration row is processed without
/// a failed attempt or a consumer retry.
/// </para>
/// <para>
/// Transactional inbox dispatch is non-swallowing. A handler failure propagates before the dedup row
/// and handler writes through the inbox's context are saved, allowing the transport to redeliver.
/// Stage local writes for that dispatcher to commit; do not call <c>SaveChanges</c> inside a handler.
/// External calls and writes through another context are outside that atomic boundary.
/// </para>
/// <para>
/// Handlers must be safe to re-run: a crash before outbox bookkeeping or a transport redelivery can
/// repeat an invocation. Inbox deduplication requires an adapter to call <see cref="IInboxDispatcher"/>
/// with the stable message id; registering an inbox does not change default local fan-out.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Integration event handler is a messaging term of art and is unrelated to System.EventHandler.")]
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>
    /// Handles the specified integration event.
    /// </summary>
    /// <param name="integrationEvent">The integration event being delivered.</param>
    /// <param name="cancellationToken">The publisher's or inbox dispatcher's token to observe while the handler runs.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the handler is done.</returns>
    ValueTask HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}