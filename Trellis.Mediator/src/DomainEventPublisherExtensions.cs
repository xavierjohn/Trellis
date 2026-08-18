namespace Trellis.Mediator;

using System.ComponentModel;

/// <summary>
/// Extension methods over <see cref="IDomainEventPublisher"/> for call sites that need to dispatch
/// an aggregate's <see cref="IAggregate.UncommittedEvents"/> manually (outside the
/// <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/> pipeline).
/// </summary>
public static class DomainEventPublisherExtensions
{
    /// <summary>
    /// <b>POST-COMMIT ONLY.</b> Publishes a defensive snapshot of <paramref name="aggregate"/>'s
    /// uncommitted domain events and then calls <see cref="IChangeTracking.AcceptChanges"/> when
    /// the aggregate's pending-event list still exactly matches that snapshot after dispatch.
    /// Provided as the manual counterpart to <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>
    /// for handlers whose response type is not an aggregate-valued result (e.g., <c>Result&lt;Unit&gt;</c>,
    /// <c>Result&lt;TDto&gt;</c>, <c>Result&lt;(A,B)&gt;</c>) and for non-Mediator call sites such as
    /// <c>BackgroundService</c> workers.
    /// </summary>
    /// <param name="publisher">The publisher used to fan out each event to its registered handlers.</param>
    /// <param name="aggregate">The aggregate whose <see cref="IAggregate.UncommittedEvents"/> are dispatched.</param>
    /// <param name="cancellationToken">Accepted for signature compatibility but deliberately not
    /// observed. This helper is <b>post-commit only</b>, so honoring cancellation here would strand
    /// an already-durable write with a partially published event set. Retained rather than removed
    /// to avoid a source-breaking change for existing callers.</param>
    /// <returns>A <see cref="Task"/> that completes once every snapshotted event has been published and
    /// <see cref="IChangeTracking.AcceptChanges"/> has cleared the aggregate's pending list.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="publisher"/> or <paramref name="aggregate"/> is null.</exception>
    /// <exception cref="DomainEventHandlerCascadedException">Thrown when the aggregate's pending-event
    /// list no longer matches the entry snapshot at the end of dispatch — i.e. a handler raised,
    /// cleared, replaced, or reordered events on the aggregate. Validation is strict (length +
    /// reference equality), so any mutation of the pending list during dispatch trips it.
    /// <see cref="IChangeTracking.AcceptChanges"/> is not called in this case so the caller can
    /// inspect the aggregate's current pending events.</exception>
    /// <remarks>
    /// <para>
    /// <b>POST-COMMIT ONLY.</b> Domain events must be published only after the underlying unit of
    /// work has committed. Calling this helper from inside a command handler that relies on
    /// <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>'s sibling
    /// <c>TransactionalCommandBehavior</c> for its commit will publish events before the database
    /// transaction is durable; if the commit then fails, the events have already escaped to their
    /// handlers and <see cref="IChangeTracking.AcceptChanges"/> has cleared them off the aggregate
    /// — making the failure non-replayable.
    /// </para>
    /// <para>
    /// Safe call sites:
    /// <list type="bullet">
    ///   <item>After a manual <c>IUnitOfWork.CommitAsync</c> in a handler that does not chain the
    ///     transactional behavior.</item>
    ///   <item>From an outer <c>IPipelineBehavior</c> that runs after
    ///     <c>TransactionalCommandBehavior</c> (i.e., registered earlier in the pipeline so its
    ///     post-await section executes later).</item>
    ///   <item>From a <c>BackgroundService</c> tick after the underlying <c>DbContext.SaveChangesAsync</c>
    ///     call has succeeded.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Strict snapshot semantics match <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>:
    /// events are dispatched sequentially from the entry snapshot only. Handlers must be
    /// side-effect-only and must not change the aggregate's pending-event list during dispatch —
    /// no raising new events, no clearing via <c>AcceptChanges</c>, no replacing or reordering.
    /// If the pending-event list differs from the entry snapshot at the end of dispatch (length
    /// or reference equality), the helper throws
    /// <see cref="DomainEventHandlerCascadedException"/> and leaves the aggregate unchanged.
    /// </para>
    /// <para>
    /// Re-entrant calls on the same aggregate are not supported. Treat domain event handlers as
    /// side-effect-only and dispatch is owned by exactly one outer call.
    /// </para>
    /// </remarks>
    public static async Task DispatchAggregateEventsAsync(
        this IDomainEventPublisher publisher,
        IAggregate aggregate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(aggregate);

        var snapshot = aggregate.UncommittedEvents().ToArray();
        foreach (var domainEvent in snapshot)
        {
            await publisher.PublishAsync(domainEvent, CancellationToken.None).ConfigureAwait(false);
        }

        var offender = DomainEventCascadeDetector.Detect(aggregate, snapshot);
        if (offender is { } cascadeOffender)
            throw new DomainEventHandlerCascadedException([cascadeOffender]);

        aggregate.AcceptChanges();
    }
}