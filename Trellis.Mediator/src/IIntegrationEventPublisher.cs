namespace Trellis.Mediator;

/// <summary>
/// Publishes a single <see cref="IIntegrationEvent"/>, together with the stable message identity a
/// transport must carry onto the wire, to its consumers. The transactional outbox relay resolves this
/// contract to deliver integration events durably after the producing transaction commits.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation fans out to in-process <see cref="IIntegrationEventHandler{TEvent}"/>
/// registrations for the event's runtime type - the right choice for a modular monolith and for tests.
/// To deliver to other services, replace this registration with a message-broker adapter (for example
/// Azure Service Bus or Kafka); the producing side - aggregates, translators, and the outbox - does not
/// change. This is the seam that keeps the outbox transport-agnostic.
/// </para>
/// <para>
/// <b>The message identity is part of the contract, not an optional extra.</b> The single method takes an
/// <see cref="OutboundIntegrationMessage"/> rather than a bare event so a transport cannot publish without
/// the id: relay delivery is at-least-once, and stamping
/// <see cref="OutboundIntegrationMessage.MessageId"/> verbatim onto the wire is what lets consumer-side
/// <c>(ConsumerId, MessageId)</c> deduplication recognize a redelivery. An adapter that minted its own id
/// per attempt would make every redelivery look like a new message and silently defeat the inbox.
/// In-process implementations may simply ignore the id - there is no wire and nothing to deduplicate.
/// </para>
/// <para>
/// Implementations are expected to be best-effort: non-cancellation handler exceptions are logged and
/// swallowed so one consumer's failure does not block the others.
/// <see cref="OperationCanceledException"/> matching the supplied token is the one exception that
/// propagates so the relay can abort cleanly.
/// </para>
/// </remarks>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes the specified message to all matching consumers.
    /// </summary>
    /// <param name="message">
    /// The message to publish, carrying the event and its stable id. Handler resolution uses
    /// <c>message.Event.GetType()</c>.
    /// </param>
    /// <param name="cancellationToken">A token to observe while waiting on the consumers.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when all consumers have run (or thrown).</returns>
    ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken);
}
