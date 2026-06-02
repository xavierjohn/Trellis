namespace Trellis.Mediator;

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
/// Dispatch only runs when the command response is a successful <c>IResult&lt;TAggregate&gt;</c>
/// (typically <c>Result&lt;TAggregate&gt;</c>) where <c>TAggregate</c> implements <see cref="IAggregate"/>.
/// Other shapes (<c>Result&lt;Unit&gt;</c>, <c>Result&lt;TDto&gt;</c>, <c>Result&lt;(A,B)&gt;</c>) are
/// passed through untouched in v1; manual dispatch remains the option for those flows.
/// </para>
/// <para>
/// Events are dispatched sequentially from a defensive snapshot captured before the first
/// publish. v1 expects single-wave dispatch: handlers should be side-effect-only and must
/// not raise additional events on the same aggregate. If the aggregate's pending-event list
/// no longer exactly matches the snapshot after dispatch, <see cref="DomainEventHandlerCascadedException"/>
/// is thrown and <see cref="IChangeTracking.AcceptChanges"/> is not called so the current
/// aggregate state remains available for inspection. Cancellation propagates above the
/// <see cref="IChangeTracking.AcceptChanges"/> call so undispatched events stay on the aggregate.
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
    /// Per-closed-generic extractor. Computed once per <c>(TMessage, TResponse)</c> instantiation
    /// using <c>typeof(TResponse)</c> so the hot path avoids <see cref="object.GetType"/> on
    /// the value-type <c>Result&lt;T&gt;</c> (which would box) and avoids any per-request
    /// dictionary lookup.
    /// </summary>
    private static readonly Func<TResponse, IAggregate?> s_extractor = CreateExtractor();

    [UnconditionalSuppressMessage(
        "Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Same justification as the Handle method — see the type-level remarks.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling",
        Justification = "Same justification as the Handle method — see the type-level remarks.")]
    private static Func<TResponse, IAggregate?> CreateExtractor() => BuildExtractorOrNoop(typeof(TResponse));

    private readonly IDomainEventPublisher _publisher;

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="publisher">The publisher used to fan out events to registered handlers.</param>
    /// <param name="logger">A logger parameter retained for constructor compatibility with the mediator behavior registrations.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="publisher"/> or <paramref name="logger"/> is null.</exception>
    public DomainEventDispatchBehavior(
        IDomainEventPublisher publisher,
        ILogger<DomainEventDispatchBehavior<TMessage, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(logger);
        _publisher = publisher;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Reflection on IResult<T>.TryGetValue against the closed generic response type. The consumer's Result<TAggregate> remains reachable through the static command handler signature, so trimming preserves it; consumers needing strict NativeAOT guarantees can supply a custom IPipelineBehavior implementation.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling",
        Justification = "Reflection on IResult<T>.TryGetValue against the closed generic response type. The consumer's Result<TAggregate> remains reachable through the static command handler signature, so trimming preserves it; consumers needing strict NativeAOT guarantees can supply a custom IPipelineBehavior implementation.")]
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

        var snapshot = aggregate.UncommittedEvents().ToArray();
        foreach (var domainEvent in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _publisher.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }

        var offender = DomainEventCascadeDetector.Detect(aggregate, snapshot);
        if (offender is { } cascadeOffender)
            throw new DomainEventHandlerCascadedException([cascadeOffender]);

        aggregate.AcceptChanges();
        return response;
    }

    [RequiresUnreferencedCode("Reflects on IResult<T>.TryGetValue to extract the aggregate. Use explicit handler registration for AOT.")]
    [RequiresDynamicCode("Reflects on IResult<T>.TryGetValue to extract the aggregate.")]
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Same justification as the Handle method — see the type-level remarks.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050",
        Justification = "Same justification as the Handle method — see the type-level remarks.")]
    private static IAggregate? ExtractAggregate(TResponse response) => s_extractor(response);

    [RequiresUnreferencedCode("Reflects on IResult<T>.TryGetValue via the runtime response type.")]
    [RequiresDynamicCode("Reflects on IResult<T>.TryGetValue via the runtime response type.")]
    private static Func<TResponse, IAggregate?> BuildExtractorOrNoop(Type responseType)
    {
        // Walk the interfaces looking for IResult<TValue> where TValue : IAggregate. This handles:
        //   * the canonical case Result<TAggregate> (TResponse is itself the closed generic)
        //   * non-generic concrete types implementing IResult<TAggregate> (e.g. a custom envelope)
        //   * generics where the aggregate is not the first type argument
        // Reported by GPT-5.5 review: the previous shape extracted via
        // typeof(TResponse).GetGenericArguments()[0] which silently failed on those alternative shapes.
        Type? aggregateType = null;
        Type? closedIResult = null;

        // Check the response type itself first (it can directly implement IResult<TAggregate>).
        // GetInterfaces returns implemented and inherited interfaces; if responseType is itself
        // an interface, we still need to consider it explicitly.
        var candidates = responseType.IsInterface
            ? responseType.GetInterfaces().Concat([responseType])
            : (IEnumerable<Type>)responseType.GetInterfaces();

        foreach (var iface in candidates)
        {
            if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(IResult<>))
                continue;

            var valueType = iface.GetGenericArguments()[0];
            if (!typeof(IAggregate).IsAssignableFrom(valueType))
                continue;

            if (aggregateType is not null && aggregateType != valueType)
            {
                // Multiple aggregate-valued IResult<> interfaces: ambiguous. Fail fast at
                // generic-instantiation time so the misconfiguration is visible at startup
                // rather than silently picking one and dropping the other's events.
                throw new InvalidOperationException(
                    $"Response type {responseType.FullName ?? responseType.Name} implements multiple " +
                    $"IResult<TAggregate> interfaces with distinct TAggregate type arguments " +
                    $"({aggregateType.FullName ?? aggregateType.Name} and {valueType.FullName ?? valueType.Name}). " +
                    "DomainEventDispatchBehavior cannot disambiguate which aggregate to extract events from.");
            }

            aggregateType = valueType;
            closedIResult = iface;
        }

        if (aggregateType is null || closedIResult is null)
            return static _ => null;

        // IResult<TValue>.TryGetValue(out TValue value). Pin the overload by parameter
        // signature so a future second TryGetValue overload on the interface would not
        // cause this lookup to throw AmbiguousMatchException.
        var tryGetValue = closedIResult.GetMethod(
            nameof(IResult<int>.TryGetValue),
            [aggregateType.MakeByRefType()])
            ?? throw new InvalidOperationException($"IResult<{aggregateType.FullName}> is missing TryGetValue(out {aggregateType.Name}).");

        return response =>
        {
            var args = new object?[] { null };
            var ok = (bool)tryGetValue.Invoke(response, args)!;
            return ok ? (IAggregate?)args[0] : null;
        };
    }
}
