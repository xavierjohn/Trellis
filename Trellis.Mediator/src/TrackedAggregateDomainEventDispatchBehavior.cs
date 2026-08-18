namespace Trellis.Mediator;

using System.Threading;
using global::Mediator;
using Microsoft.Extensions.Logging;

/// <summary>
/// Opt-in pipeline behavior that, after a successful command, dispatches domain events from
/// every aggregate the unit of work tracked at commit time — regardless of the command's
/// response shape. Companion to <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>,
/// which only fires for <c>Result&lt;TAggregate&gt;</c> responses.
/// </summary>
/// <remarks>
/// <para>
/// Registered via
/// <see cref="TrackedAggregateDomainEventDispatchServiceCollectionExtensions.AddTrackedAggregateDomainEventDispatch(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// The opt-in extension removes any registered <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>
/// to avoid double-dispatch for <c>Result&lt;TAggregate&gt;</c> handlers — auto-dispatch happens
/// here, sourced from <see cref="ITrackedAggregateSource.CommittedAggregates"/>.
/// </para>
/// <para>
/// <b>Ordering.</b> Runs OUTSIDE <c>TransactionalCommandBehavior</c> so it sees the post-commit
/// world. The opt-in registration handles the yank-and-reappend so this invariant holds
/// regardless of the consumer's call order for
/// <c>AddTrellisUnitOfWork&lt;TContext&gt;()</c>.
/// </para>
/// <para>
/// <b>Skips dispatch when:</b>
/// <list type="bullet">
/// <item><see cref="IResult.IsFailure"/> is <see langword="true"/> — including
/// <c>Result.FailAfterCommit</c>. Persisted failures must dispatch events manually
/// (typically by calling <see cref="DomainEventPublisherExtensions.DispatchAggregateEventsAsync"/>
/// before returning <c>FailAfterCommit</c>) or call <c>AcceptChanges()</c> to discard them;
/// otherwise a later successful command in the same scope will auto-dispatch them.</item>
/// <item>The call is re-entrant — another tracked dispatch is already in progress on the
/// same async flow. Domain-event handlers that send nested commands trigger this guard so
/// the snapshot held by the outer behavior is not overwritten and outer events are not
/// republished.</item>
/// </list>
/// </para>
/// <para>
/// <b>Re-entrant nested commands.</b> A domain-event handler dispatched from this behavior is
/// free to call <c>IMediator.Send</c>. The nested command runs its own pipeline (including its
/// own <c>TransactionalCommandBehavior</c>) but the nested invocation of this behavior detects
/// re-entrancy and short-circuits. Callers that need fan-out from nested commands must dispatch
/// manually via <see cref="DomainEventPublisherExtensions.DispatchAggregateEventsAsync"/>.
/// </para>
/// <para>
/// <b>Cascade detection.</b> The behavior snapshots every tracked aggregate's pending events at
/// entry, dispatches only those snapshotted events, and then validates that each aggregate's
/// pending-event list still exactly matches its snapshot by length AND per-position reference
/// equality. If any handler changed the pending list (raised new events, cleared via
/// <c>AcceptChanges</c>, replaced, or reordered) on the originating aggregate or on any other
/// aggregate participating in the snapshot, <see cref="DomainEventHandlerCascadedException"/>
/// is thrown with every offending aggregate and <c>AcceptChanges()</c> is not called on any
/// aggregate.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The command type. Must implement <see cref="ICommand{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The command response. Must implement <see cref="IResult"/>.</typeparam>
public sealed partial class TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult
{
    private readonly ITrackedAggregateSource _trackedAggregateSource;
    private readonly IDomainEventPublisher _publisher;
    private readonly ILogger<TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="TrackedAggregateDomainEventDispatchBehavior{TMessage, TResponse}"/>.
    /// </summary>
    /// <param name="trackedAggregateSource">The unit-of-work sidecar that exposes the aggregates
    /// tracked at the most recent successful commit.</param>
    /// <param name="publisher">The publisher used to fan out events to registered handlers.</param>
    /// <param name="logger">The logger used to record dispatch diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when any constructor argument is null.</exception>
    public TrackedAggregateDomainEventDispatchBehavior(
        ITrackedAggregateSource trackedAggregateSource,
        IDomainEventPublisher publisher,
        ILogger<TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(trackedAggregateSource);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(logger);
        _trackedAggregateSource = trackedAggregateSource;
        _publisher = publisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return response;

        if (TrackedAggregateDispatchReentrancyGuard.IsInDispatch)
        {
            // Re-entrant call from a domain-event handler's nested command. Outer dispatch already
            // owns the snapshot; running the loop here would overwrite CommittedAggregates and could
            // republish outer events. Handlers that need fan-out from nested commands must call
            // DispatchAggregateEventsAsync manually.
            LogNestedDispatchSkipped(_logger);
            return response;
        }

        // Copy by reference into a local so a nested command's commit, which writes to
        // ITrackedAggregateSource.CommittedAggregates, cannot mutate this iteration's set.
        var committed = _trackedAggregateSource.CommittedAggregates;
        if (committed.Count == 0)
            return response;

        var aggregates = new IAggregate[committed.Count];
        for (var i = 0; i < committed.Count; i++)
            aggregates[i] = committed[i];

        TrackedAggregateDispatchReentrancyGuard.IsInDispatch = true;
        try
        {
            await DispatchAllAsync(aggregates).ConfigureAwait(false);
        }
        finally
        {
            TrackedAggregateDispatchReentrancyGuard.IsInDispatch = false;
        }

        return response;
    }

    private async Task DispatchAllAsync(IAggregate[] aggregates)
    {
        var snapshots = new (IAggregate Aggregate, IDomainEvent[] Events)[aggregates.Length];
        for (var i = 0; i < aggregates.Length; i++)
            snapshots[i] = (aggregates[i], aggregates[i].UncommittedEvents().ToArray());

        foreach (var (_, events) in snapshots)
        {
            foreach (var domainEvent in events)
            {
                // Post-commit: the registration re-appends TransactionalCommandBehavior as the
                // innermost behavior, so these aggregates are already durable. Passing the
                // caller's token would let a client disconnect abandon part of the fan-out.
                await _publisher.PublishAsync(domainEvent, CancellationToken.None).ConfigureAwait(false);
            }
        }

        List<CascadeOffender>? offenders = null;
        foreach (var (aggregate, events) in snapshots)
        {
            var offender = DomainEventCascadeDetector.Detect(aggregate, events);
            if (offender is not { } cascadeOffender)
                continue;

            offenders ??= [];
            offenders.Add(cascadeOffender);
        }

        if (offenders is not null)
            throw new DomainEventHandlerCascadedException(offenders);

        foreach (var (aggregate, _) in snapshots)
            aggregate.AcceptChanges();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping nested tracked-aggregate domain event dispatch (re-entrant invocation). Outer dispatch retains ownership of the snapshot; nested commands must dispatch manually if their aggregates raise events.")]
    private static partial void LogNestedDispatchSkipped(ILogger logger);
}