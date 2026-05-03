namespace Trellis.Mediator;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using global::Mediator;
using Microsoft.Extensions.Logging;

/// <summary>
/// Pipeline behavior that dispatches domain events accumulated on the success-value
/// aggregate after the command handler returns. Events fire after any inner
/// <c>TransactionalCommandBehavior</c> commits, so handlers see committed state.
/// </summary>
/// <remarks>
/// <para>
/// Constrained to <see cref="ICommand{TResponse}"/> so that queries returning the same
/// aggregate types do not trigger dispatch.
/// </para>
/// <para>
/// Dispatch only runs when the command response is a successful <c>Result&lt;TAggregate&gt;</c>
/// where <c>TAggregate</c> implements <see cref="IAggregate"/>. Other shapes
/// (<c>Result&lt;Unit&gt;</c>, <c>Result&lt;TDto&gt;</c>, <c>Result&lt;(A,B)&gt;</c>) are
/// passed through untouched in v1; manual dispatch remains the option for those flows.
/// </para>
/// <para>
/// Events are dispatched sequentially by index, so events raised by a handler on
/// the same aggregate are picked up on the next wave. The wave count is capped to
/// prevent runaway loops; if the cap is exceeded an error is logged and the remaining
/// events are abandoned. <see cref="IChangeTracking.AcceptChanges"/> only runs once
/// after the loop completes successfully — cancellation propagates above the
/// <see cref="IChangeTracking.AcceptChanges"/> call so undispatched events stay on
/// the aggregate.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The command type. Must implement <see cref="ICommand{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The command response. Must implement <see cref="IResult"/>.</typeparam>
public sealed partial class DomainEventDispatchBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult
{
    /// <summary>
    /// Maximum number of dispatch waves. Caps cascading event scenarios where a handler
    /// raises new events on the same aggregate. v1 expects single-wave dispatch; this
    /// cap exists to surface accidental re-entry without hanging the pipeline.
    /// </summary>
    public const int MaxDispatchWaves = 8;

    private static readonly ConcurrentDictionary<Type, Func<TResponse, IAggregate?>> s_aggregateExtractor = new();

    private readonly IDomainEventPublisher _publisher;
    private readonly ILogger<DomainEventDispatchBehavior<TMessage, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="publisher">The publisher used to fan out events to registered handlers.</param>
    /// <param name="logger">The logger used to record dispatch diagnostics.</param>
    public DomainEventDispatchBehavior(
        IDomainEventPublisher publisher,
        ILogger<DomainEventDispatchBehavior<TMessage, TResponse>> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Reflection on Result<T>.Value is the documented behavior of the default dispatch behavior; AOT consumers must register custom IPipelineBehavior implementations.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling",
        Justification = "Reflection on Result<T>.Value is the documented behavior of the default dispatch behavior; AOT consumers must register custom IPipelineBehavior implementations.")]
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return response;

        var aggregate = ExtractAggregate(response);
        if (aggregate is null)
            return response;

        // Track how many events have been published. UncommittedEvents() returns a fresh
        // snapshot each call; the underlying DomainEvents list is append-only until
        // AcceptChanges() runs, so handler-raised events appear at successive indices.
        // Holding off AcceptChanges() until the loop completes preserves not-yet-dispatched
        // events on the aggregate when cancellation propagates mid-loop.
        var dispatched = 0;
        for (var wave = 0; wave < MaxDispatchWaves; wave++)
        {
            var events = aggregate.UncommittedEvents();
            if (events.Count <= dispatched)
                break;

            for (var i = dispatched; i < events.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _publisher.PublishAsync(events[i], cancellationToken).ConfigureAwait(false);
                dispatched = i + 1;
            }
        }

        var pendingAfterLoop = aggregate.UncommittedEvents().Count - dispatched;
        if (pendingAfterLoop > 0)
        {
            LogDispatchCapExceeded(_logger, MaxDispatchWaves, aggregate.GetType().FullName ?? aggregate.GetType().Name, pendingAfterLoop);
        }

        // Only reach here on the full-success path: cancellation propagates above and
        // skips this clear, leaving undispatched events on the aggregate.
        aggregate.AcceptChanges();
        return response;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Domain event dispatch exceeded {MaxWaves} waves for {AggregateType}; abandoning {Remaining} event(s). Domain event handlers must not raise events on the same aggregate.")]
    private static partial void LogDispatchCapExceeded(ILogger logger, int maxWaves, string aggregateType, int remaining);

    [RequiresUnreferencedCode("Reflects on Result<T>.Value to extract the aggregate. Use explicit handler registration for AOT.")]
    [RequiresDynamicCode("Reflects on Result<T>.Value to extract the aggregate.")]
    private static IAggregate? ExtractAggregate(TResponse response)
    {
        var extractor = s_aggregateExtractor.GetOrAdd(response.GetType(), BuildExtractor);
        return extractor(response);
    }

    [RequiresUnreferencedCode("Reflects on Result<T>.Value via the runtime response type.")]
    [RequiresDynamicCode("Reflects on Result<T>.Value via the runtime response type.")]
    private static Func<TResponse, IAggregate?> BuildExtractor(Type responseType)
    {
        // Result<T> exposes Value (not throwing on success — but to stay safe we route
        // through TryGetValue which never throws and returns false if the result is a failure.
        // We've already verified IsSuccess at the call site so Value would be safe, but
        // TryGetValue keeps us decoupled from the public/non-public state of Value.
        if (!responseType.IsGenericType)
            return static _ => null;

        var valueType = responseType.GetGenericArguments()[0];
        if (!typeof(IAggregate).IsAssignableFrom(valueType))
            return static _ => null;

        // IResult<TValue>.TryGetValue(out TValue value)
        var iResultGeneric = typeof(IResult<>).MakeGenericType(valueType);
        if (!iResultGeneric.IsAssignableFrom(responseType))
            return static _ => null;

        var tryGetValue = iResultGeneric.GetMethod(nameof(IResult<int>.TryGetValue))
            ?? throw new InvalidOperationException($"IResult<{valueType.FullName}> is missing TryGetValue.");

        return response =>
        {
            var args = new object?[] { null };
            var ok = (bool)tryGetValue.Invoke(response, args)!;
            return ok ? (IAggregate?)args[0] : null;
        };
    }
}
