namespace Trellis.Mediator;

/// <summary>
/// Publishes a single <see cref="IIntegrationEvent"/> to its consumers. The transactional outbox relay
/// resolves this contract to deliver integration events durably after the producing transaction commits.
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
/// Implementations are expected to be best-effort: non-cancellation handler exceptions are logged and
/// swallowed so one consumer's failure does not block the others.
/// <see cref="OperationCanceledException"/> matching the supplied token is the one exception that
/// propagates so the relay can abort cleanly.
/// </para>
/// </remarks>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes the specified integration event to all matching consumers.
    /// </summary>
    /// <param name="integrationEvent">The event to publish. Resolution uses <c>integrationEvent.GetType()</c>.</param>
    /// <param name="cancellationToken">A token to observe while waiting on the consumers.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when all consumers have run (or thrown).</returns>
    ValueTask PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes an integration event together with the stable message identity a transport must carry
    /// onto the wire. This is the overload the outbox relay calls.
    /// </summary>
    /// <remarks>
    /// The default implementation discards <see cref="OutboundIntegrationMessage.MessageId"/> and forwards
    /// to <see cref="PublishAsync(IIntegrationEvent, CancellationToken)"/>, which is correct for in-process
    /// fan-out: handlers run in the producer's own process, so there is no wire and nothing to deduplicate.
    /// A broker adapter overrides this to map the id onto its transport (for example
    /// <c>ServiceBusMessage.MessageId</c>) so consumer-side <c>(ConsumerId, MessageId)</c> deduplication
    /// survives the relay's at-least-once redelivery. Existing implementations keep compiling and running
    /// unchanged.
    /// </remarks>
    /// <param name="message">The message to publish, carrying the event and its stable id.</param>
    /// <param name="cancellationToken">A token to observe while waiting on the consumers.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when all consumers have run (or thrown).</returns>
    ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken) =>
        PublishAsync(message.Event, cancellationToken);
}
