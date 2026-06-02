namespace Trellis.Mediator;

/// <summary>
/// Handles a domain event raised by an <see cref="IAggregate"/>. Implementations are
/// resolved via DI by the <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>
/// after a successful command and invoked once per matching event.
/// </summary>
/// <typeparam name="TEvent">
/// The concrete event type. Dispatch matches the runtime type of the event exactly;
/// base-type and interface-type handlers are <b>not</b> resolved automatically.
/// </typeparam>
/// <remarks>
/// <para>
/// Domain event handlers run after the command's transaction commits (when
/// <c>TransactionalCommandBehavior</c> is registered) or after the command handler
/// returns success (when no transactional behavior is in the pipeline).
/// </para>
/// <para>
/// Handlers must be idempotent and treat their work as a best-effort side effect:
/// non-cancellation exceptions thrown by a handler are logged at error level and
/// swallowed so that other handlers, other events, and the originating command
/// still complete. <see cref="OperationCanceledException"/> matching the request's
/// cancellation token is the one exception that propagates — handlers that observe
/// cancellation should throw it; the dispatcher will let it abort the remaining work.
/// If a side effect must block command completion, do that work inside the command
/// handler instead.
/// </para>
/// <para>
/// Handlers should treat themselves as side-effect-only. The dispatcher publishes only
/// the entry snapshot and then validates by length + reference equality that handlers
/// did not change the participating aggregate's pending-event list — raising new events,
/// clearing via <c>AcceptChanges</c>, replacing, or reordering all trip cascade detection.
/// If a handler changes the pending list, dispatch throws
/// <see cref="DomainEventHandlerCascadedException"/> and does not call <c>AcceptChanges()</c>.
/// The originating command's transaction has already committed, so persist-and-emit chains
/// belong inside command handlers or separate top-level commands, not domain-event handlers.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class OrderConfirmationEmailHandler : IDomainEventHandler&lt;OrderSubmitted&gt;
/// {
///     private readonly IEmailService _email;
///
///     public OrderConfirmationEmailHandler(IEmailService email) =&gt; _email = email;
///
///     public async ValueTask HandleAsync(OrderSubmitted domainEvent, CancellationToken cancellationToken)
///     {
///         await _email.SendOrderConfirmationAsync(domainEvent.OrderId, cancellationToken).ConfigureAwait(false);
///     }
/// }
/// </code>
/// </example>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Domain event handler is a DDD term of art and is unrelated to System.EventHandler.")]
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the specified domain event.
    /// </summary>
    /// <param name="domainEvent">The event raised by an aggregate during command execution.</param>
    /// <param name="cancellationToken">Propagated from the originating command pipeline.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the handler is done.</returns>
    ValueTask HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
