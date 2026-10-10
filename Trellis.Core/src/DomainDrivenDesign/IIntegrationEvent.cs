namespace Trellis;

/// <summary>
/// Represents an integration event - the stable, published contract a bounded context emits to the
/// outside world (other services or bounded contexts), as distinct from an <see cref="IDomainEvent"/>
/// which stays inside the context and is raised by aggregates.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Domain events vs. integration events.</strong> A domain event is internal: it is raised by
/// an aggregate, dispatched in-process to <c>IDomainEventHandler&lt;T&gt;</c>, and free to expose the
/// domain's ubiquitous language because only the owning context observes it. An integration event is
/// external: it is a versioned wire contract other systems depend on, so it should be deliberately
/// shaped, stable, and free of internal domain types. Conflating the two leaks internal structure onto
/// the wire and couples external consumers to refactors of your domain model.
/// </para>
/// <para>
/// <strong>How they relate.</strong> Integration events are typically <em>translated</em> from domain
/// events: a domain-event handler observes a domain event and produces one or more integration events
/// describing the same business fact in contract terms. The transactional outbox captures the source
/// domain event atomically with the state change and stages translated integration events durably
/// before publication - see <c>Trellis.EntityFrameworkCore.Outbox</c>.
/// The outbox publishes through <c>IIntegrationEventPublisher</c>. Its default in-process fan-out is
/// best-effort: ordinary handler failures are logged and swallowed, so the relay processes the row
/// without retrying those consumers. Reliable broker publication requires an adapter that completes
/// only after broker acceptance and propagates publication failures to the relay. Broker acceptance
/// is not proof of downstream processing; inbox consumers provide a separate transactional boundary.
/// </para>
/// <para>
/// <strong>Best practices.</strong> Name events in the past tense (for example
/// <c>OrderPlacedIntegrationEvent</c>); make them immutable; include only contract-relevant data using
/// primitive or nullable transports rather than internal value objects; treat changes as a versioning
/// concern; and use <see cref="OccurredAt"/> as the single timestamp.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // The external contract - primitive members, no internal domain types.
/// public sealed record OrderPlacedIntegrationEvent(Guid OrderId, string CustomerEmail, decimal Total, DateTimeOffset OccurredAt)
///     : IIntegrationEvent;
///
/// // Translate a domain event into the integration contract and enqueue it for the outbox.
/// public sealed class OrderPlacedTranslator(IIntegrationEventCollector collector)
///     : IDomainEventHandler&lt;OrderPlaced&gt;
/// {
///     public ValueTask HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
///     {
///         collector.Add(new OrderPlacedIntegrationEvent(
///             domainEvent.OrderId.Value,
///             domainEvent.CustomerEmail.Value,
///             domainEvent.Total.Amount,
///             domainEvent.OccurredAt));
///         return ValueTask.CompletedTask;
///     }
/// }
/// </code>
/// </example>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the timestamp when the business fact this event describes occurred.
    /// </summary>
    /// <value>
    /// The instant the event was raised, as a <see cref="DateTimeOffset"/> with an explicit UTC offset.
    /// Author it from <see cref="TimeProvider.GetUtcNow"/> (typically injected) so the canonical UTC
    /// offset is recorded and tests can pin time deterministically. <see cref="DateTimeOffset"/> is
    /// preferred over <see cref="DateTime"/> because the offset round-trips unambiguously through the
    /// outbox table and the message bus.
    /// </value>
    DateTimeOffset OccurredAt { get; }
}