namespace Trellis.Mediator;

/// <summary>
/// Publishes a single <see cref="IDomainEvent"/> by resolving and invoking all
/// <see cref="IDomainEventHandler{TEvent}"/> registrations for the event's runtime type.
/// </summary>
/// <remarks>
/// <para>
/// The framework's <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>
/// uses this contract to dispatch events from an aggregate after a successful command.
/// Application code rarely needs to call this directly; injecting the publisher is
/// useful only for non-pipeline contexts such as background jobs or scheduled tasks
/// that want to fan out an event the same way the pipeline would.
/// </para>
/// <para>
/// Implementations are expected to be best-effort: non-cancellation handler
/// exceptions are logged and swallowed so that one handler's failure does not block
/// the others. <see cref="OperationCanceledException"/> matching the supplied
/// cancellation token is the one exception that propagates so the originating
/// request can abort cleanly.
/// </para>
/// </remarks>
public interface IDomainEventPublisher
{
    /// <summary>
    /// Publishes the specified domain event to all matching handlers.
    /// </summary>
    /// <param name="domainEvent">The event to publish. Resolution uses <c>domainEvent.GetType()</c>.</param>
    /// <param name="cancellationToken">A token to observe while waiting on the handlers.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when all handlers have run (or thrown).</returns>
    ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}
