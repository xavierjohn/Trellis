namespace Trellis.Mediator;

/// <summary>
/// Publishes a single <see cref="IIntegrationEvent"/>, together with the stable message identity a
/// transport must carry onto the wire. The transactional outbox relay resolves this contract after the
/// producing transaction commits; the implementation defines the publication completion boundary.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation fans out to in-process <see cref="IIntegrationEventHandler{TEvent}"/>
/// registrations for the event's runtime type. This is best-effort local notification, suitable when a
/// modular monolith or test does not require durable consumer retries. Ordinary handler failures and
/// handler-resolution failures are logged and swallowed; a handler's <see cref="OperationCanceledException"/>
/// propagates when the supplied token is canceled. Normal completion makes the relay process the
/// integration row even if a consumer failed; it does not report per-consumer progress.
/// To deliver to other services, replace this registration with a message-broker adapter (for example
/// Azure Service Bus or Kafka); the producing side - aggregates, translators, and the outbox - does not
/// change. This is the seam that keeps the outbox transport-agnostic.
/// </para>
/// <para>
/// A broker adapter must complete only after the broker accepts publication and must propagate
/// publication failures rather than log and swallow them. The relay owns durable retries, but can
/// react only to failures the publisher exposes. Broker acceptance does not mean downstream consumers
/// have processed the message. The post-commit/no-retry rationale for the default
/// <see cref="IDomainEventPublisher"/> does not apply to this relay-owned publication step.
/// </para>
/// <para>
/// <b>The message identity is part of the contract, not an optional extra.</b> The single method takes an
/// <see cref="OutboundIntegrationMessage"/> rather than a bare event so a transport cannot publish without
/// the id: the relay can publish the same row again after a failure or a crash, and stamping
/// <see cref="OutboundIntegrationMessage.MessageId"/> verbatim onto the wire is what lets consumer-side
/// <c>(ConsumerId, MessageId)</c> deduplication recognize a redelivery. An adapter that minted its own id
/// per attempt would make every redelivery look like a new message and silently defeat the inbox.
/// The default in-process fan-out ignores the id. An adapter routing through <see cref="IInboxDispatcher"/>
/// must preserve it even within one process; registration alone does not route publication through an inbox.
/// </para>
/// </remarks>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes the specified message according to the implementation's completion boundary.
    /// </summary>
    /// <param name="message">
    /// The message to publish, carrying the event and its stable id. The default in-process publisher
    /// resolves handlers using <c>message.Event.GetType()</c>.
    /// </param>
    /// <param name="cancellationToken">A token to observe while publishing.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that completes after the implementation's publication step: best-effort
    /// local fan-out for the default publisher, or broker acceptance for a broker adapter. Normal
    /// completion is not a guarantee that every consumer succeeded.
    /// </returns>
    ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken);
}