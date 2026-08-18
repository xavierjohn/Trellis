namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Discriminates the two kinds of row the transactional outbox carries, so the relay routes each to the
/// correct publisher.
/// </summary>
public enum OutboxMessageKind
{
    /// <summary>
    /// An <see cref="IDomainEvent"/> captured from an aggregate. The relay re-dispatches it in-process
    /// through <see cref="Trellis.Mediator.IDomainEventPublisher"/>; its handlers may translate it into
    /// integration events via <see cref="Trellis.Mediator.IIntegrationEventCollector"/>.
    /// </summary>
    Domain = 0,

    /// <summary>
    /// An <see cref="IIntegrationEvent"/> produced by translation and staged for external delivery. The
    /// relay publishes it through <see cref="Trellis.Mediator.IIntegrationEventPublisher"/>.
    /// </summary>
    Integration = 1,
}