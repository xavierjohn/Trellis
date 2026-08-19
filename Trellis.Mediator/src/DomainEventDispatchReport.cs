namespace Trellis.Mediator;

/// <summary>
/// A single handler's failure during a reported domain-event dispatch.
/// </summary>
/// <param name="HandlerType">
/// The handler's stable identity, as produced by <see cref="DomainEventDispatchReport.HandlerIdentity(Type)"/>.
/// </param>
/// <param name="Error">The exception the handler threw.</param>
public sealed record DomainEventHandlerFailure(string HandlerType, Exception Error);

/// <summary>
/// The per-handler outcome of a dispatch performed through
/// <see cref="IReportingDomainEventPublisher.PublishReportingAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the non-swallowing counterpart to <see cref="IDomainEventPublisher.PublishAsync"/>. It exists
/// for callers that own a <b>durable retry mechanism</b> — principally the transactional outbox relay —
/// and therefore need to know which handlers succeeded and which must be retried. Callers without such a
/// mechanism (notably the in-pipeline <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>,
/// which runs post-commit and cannot retry) should keep using <see cref="IDomainEventPublisher"/>.
/// </para>
/// <para>
/// Every handler is always attempted: a failing handler never short-circuits its siblings, so one bad
/// handler cannot starve the others of their side effects.
/// </para>
/// </remarks>
public sealed class DomainEventDispatchReport
{
    /// <summary>A report for a dispatch that invoked no handlers and observed no failures.</summary>
    public static readonly DomainEventDispatchReport Empty = new([], [], null);

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainEventDispatchReport"/> class.
    /// </summary>
    /// <param name="completedHandlers">The handlers that are now complete. See <see cref="CompletedHandlers"/>.</param>
    /// <param name="failures">The handlers that threw during this dispatch.</param>
    /// <param name="resolutionFailure">The exception raised while resolving handlers, if any.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="completedHandlers"/> or <paramref name="failures"/> is null.</exception>
    public DomainEventDispatchReport(
        IReadOnlyList<string> completedHandlers,
        IReadOnlyList<DomainEventHandlerFailure> failures,
        Exception? resolutionFailure)
    {
        ArgumentNullException.ThrowIfNull(completedHandlers);
        ArgumentNullException.ThrowIfNull(failures);

        CompletedHandlers = completedHandlers;
        Failures = failures;
        ResolutionFailure = resolutionFailure;
    }

    /// <summary>
    /// The stable identity used to name a handler in <see cref="CompletedHandlers"/> and
    /// <see cref="DomainEventHandlerFailure.HandlerType"/>: the declaring assembly's <i>simple</i> name
    /// and the type's full name, separated by <c>:</c>.
    /// </summary>
    /// <param name="handlerType">The concrete handler type.</param>
    /// <returns>The handler's stable identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handlerType"/> is null.</exception>
    /// <remarks>
    /// <see cref="Type.FullName"/> alone is not sufficient: two assemblies can declare distinct handlers
    /// with the same namespace-qualified name, and skipping one because the other succeeded would
    /// silently drop work. The assembly's <i>simple</i> name is used rather than its fully qualified name
    /// so the identity survives a version bump — otherwise a rolling deploy would re-run handlers that
    /// had already completed.
    /// </remarks>
    public static string HandlerIdentity(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        return $"{handlerType.Assembly.GetName().Name}:{handlerType.FullName ?? handlerType.Name}";
    }

    /// <summary>
    /// Every handler that is now complete for this event, by <see cref="HandlerIdentity(Type)"/> — both
    /// the ones that succeeded during this dispatch and the ones the caller reported as already complete
    /// and that were therefore skipped. The list is <i>cumulative</i>, so a caller persisting it can
    /// overwrite its previous value rather than merging.
    /// </summary>
    public IReadOnlyList<string> CompletedHandlers { get; }

    /// <summary>The handlers that threw during this dispatch, in invocation order.</summary>
    public IReadOnlyList<DomainEventHandlerFailure> Failures { get; }

    /// <summary>
    /// The exception raised while resolving the event's handlers from the container, or <see langword="null"/>
    /// when resolution succeeded. A resolution failure means <i>no</i> handler ran, so nothing can be marked
    /// complete; the whole event must be retried.
    /// </summary>
    public Exception? ResolutionFailure { get; }

    /// <summary>
    /// <see langword="true"/> when handler resolution succeeded and every resolved handler completed —
    /// i.e. the event needs no retry.
    /// </summary>
    public bool IsComplete => ResolutionFailure is null && Failures.Count == 0;

    /// <summary>
    /// The first error observed, preferring <see cref="ResolutionFailure"/>, or <see langword="null"/> when
    /// <see cref="IsComplete"/>. Convenience for callers that record a single error string per attempt.
    /// </summary>
    public Exception? FirstError => ResolutionFailure ?? (Failures.Count > 0 ? Failures[0].Error : null);
}
