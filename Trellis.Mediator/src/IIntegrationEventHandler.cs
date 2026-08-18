namespace Trellis.Mediator;

/// <summary>
/// Handles an <see cref="IIntegrationEvent"/> - the in-process consumer side of the external contract.
/// Implementations are resolved via DI and invoked once per matching event by the default
/// <see cref="IIntegrationEventPublisher"/>.
/// </summary>
/// <typeparam name="TEvent">
/// The concrete integration event type. Dispatch matches the runtime type of the event exactly;
/// base-type and interface-type handlers are <b>not</b> resolved automatically.
/// </typeparam>
/// <remarks>
/// <para>
/// This is the framework's default, in-process consumer for integration events: it lets a modular
/// monolith react to its own published contracts without a message broker, and it makes integration
/// events testable. When you move a consumer to a separate service, replace the default publisher with
/// a broker adapter and the producing side is unchanged.
/// </para>
/// <para>
/// Like domain-event handlers, integration-event handlers are best-effort side effects: the default
/// publisher logs and swallows non-cancellation exceptions so one handler's failure does not block the
/// others. Handlers must be idempotent - the transactional outbox delivers at least once.
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
    /// <param name="cancellationToken">A token to observe while the handler runs.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the handler is done.</returns>
    ValueTask HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}