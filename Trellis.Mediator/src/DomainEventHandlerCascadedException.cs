namespace Trellis.Mediator;

/// <summary>
/// Thrown by the domain event dispatch behaviors when a domain event handler raises new
/// events on the same aggregate during dispatch. Handlers MUST be side-effect-only.
/// <para>
/// Note: this exception fires AFTER the inner <c>TransactionalCommandBehavior</c> commits.
/// The database state is durable, but the dispatch-stage response is failure-shaped.
/// Consumers retrying the same command will hit "already committed" semantics; durable
/// at-least-once dispatch requires the outbox pattern (planned, not shipped).
/// </para>
/// </summary>
public sealed class DomainEventHandlerCascadedException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DomainEventHandlerCascadedException"/> class.
    /// </summary>
    public DomainEventHandlerCascadedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainEventHandlerCascadedException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public DomainEventHandlerCascadedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainEventHandlerCascadedException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DomainEventHandlerCascadedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainEventHandlerCascadedException"/> class.
    /// </summary>
    /// <param name="offenders">The aggregates whose handlers raised additional events during dispatch.</param>
    public DomainEventHandlerCascadedException(IReadOnlyList<CascadeOffender> offenders)
        : base(BuildMessage(offenders))
        => Offenders = offenders.ToArray();

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainEventHandlerCascadedException"/> class for one aggregate.
    /// </summary>
    /// <param name="aggregateType">The offending aggregate type.</param>
    /// <param name="cascadedEventTypeNames">The cascaded event type names detected after dispatch.</param>
    public DomainEventHandlerCascadedException(Type aggregateType, IReadOnlyList<string> cascadedEventTypeNames)
        : this([new CascadeOffender(aggregateType, cascadedEventTypeNames)])
    {
    }

    /// <summary>
    /// Gets the aggregates whose pending-event list changed during dispatch.
    /// </summary>
    public IReadOnlyList<CascadeOffender> Offenders { get; } = [];

    private static string BuildMessage(IReadOnlyList<CascadeOffender> offenders)
    {
        ArgumentNullException.ThrowIfNull(offenders);

        return $"Domain event handler(s) raised additional events on {offenders.Count} aggregate(s) during dispatch. " +
            "Handlers must be side-effect-only. " +
            $"Offenders: {string.Join("; ", offenders.Select(o => $"{o.AggregateType.FullName}[{string.Join(',', o.CascadedEventTypeNames)}]"))}";
    }
}

/// <summary>
/// Identifies an aggregate whose pending-event list changed during domain event dispatch.
/// </summary>
/// <param name="AggregateType">The offending aggregate type.</param>
/// <param name="CascadedEventTypeNames">The cascaded event type names detected after dispatch.</param>
public readonly record struct CascadeOffender(Type AggregateType, IReadOnlyList<string> CascadedEventTypeNames);