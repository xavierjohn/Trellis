namespace Trellis.Mediator;

/// <summary>
/// Publishes a domain event and <b>reports</b> each handler's outcome instead of swallowing failures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IDomainEventPublisher"/> is best-effort by design: it is used by the in-pipeline
/// <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>, which runs <i>after</i> the unit of work
/// has committed and therefore has nothing to retry with — failing the request there would report an error
/// for a write that is already durable. Swallowing is the right behavior in that context.
/// </para>
/// <para>
/// It is the wrong behavior for a caller that <i>does</i> own a durable retry mechanism. The transactional
/// outbox relay can re-drain a row on a later poll with exponential backoff, so a handler failure there
/// should leave the message pending rather than silently marking it delivered. This contract is that
/// non-swallowing path: the caller receives a <see cref="DomainEventDispatchReport"/> and decides the
/// retry policy itself.
/// </para>
/// <para>
/// Implementations must attempt <i>every</i> handler and collect the failures rather than stopping at the
/// first one, so a single failing handler cannot starve its siblings of their side effects.
/// <see cref="OperationCanceledException"/> matching the supplied token propagates rather than being
/// reported, so the caller can abort cleanly.
/// </para>
/// <para>
/// Replacing the default <see cref="IDomainEventPublisher"/> does <i>not</i> replace this contract; a
/// consumer substituting their own dispatch implementation should register both if they use the outbox.
/// </para>
/// </remarks>
public interface IReportingDomainEventPublisher
{
    /// <summary>
    /// Publishes the specified domain event to all matching handlers, skipping any the caller has already
    /// recorded as complete, and reports the per-handler outcome.
    /// </summary>
    /// <param name="domainEvent">The event to publish. Resolution uses <c>domainEvent.GetType()</c>.</param>
    /// <param name="completedHandlers">
    /// Handlers (by <see cref="DomainEventDispatchReport.HandlerIdentity(Type)"/>) that a previous attempt
    /// already completed, and that must therefore <b>not</b> be invoked again; pass <see langword="null"/>
    /// on a first attempt. This is what makes a retry re-run only the handlers that actually failed,
    /// rather than re-running successful siblings and duplicating their side effects.
    /// </param>
    /// <param name="cancellationToken">A token to observe while waiting on the handlers.</param>
    /// <returns>A report naming the handlers that are now complete and the ones that failed.</returns>
    ValueTask<DomainEventDispatchReport> PublishReportingAsync(
        IDomainEvent domainEvent,
        IReadOnlySet<string>? completedHandlers,
        CancellationToken cancellationToken);
}
