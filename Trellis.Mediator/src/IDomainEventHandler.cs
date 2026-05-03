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
/// exceptions thrown by a handler are logged at error level and swallowed so that
/// other handlers, other events, and the originating command still complete. If a
/// side effect must block command completion (e.g., write a saga step), do that work
/// inside the command handler instead.
/// </para>
/// <para>
/// Handlers are also expected <b>not</b> to mutate the aggregate or raise additional
/// events on it. The dispatcher drains events in waves to surface accidental
/// re-entry, but the supported v1 contract is single-pass dispatch with no nested
/// event raising.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class MatchCreatedEmailHandler : IDomainEventHandler&lt;MatchCreated&gt;
/// {
///     private readonly IEmailService _email;
///
///     public MatchCreatedEmailHandler(IEmailService email) =&gt; _email = email;
///
///     public async ValueTask HandleAsync(MatchCreated domainEvent, CancellationToken cancellationToken)
///     {
///         await _email.SendMatchInviteAsync(domainEvent.MatchId, cancellationToken).ConfigureAwait(false);
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
