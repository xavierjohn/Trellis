namespace Trellis.Mediator;

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IDomainEventPublisher"/> implementation that resolves
/// <see cref="IDomainEventHandler{TEvent}"/> instances from the request's DI scope
/// using the event's runtime type and invokes each handler in turn.
/// </summary>
/// <remarks>
/// <para>
/// Non-cancellation handler exceptions are logged at <see cref="LogLevel.Error"/>
/// and swallowed; the publisher continues with the next handler so a single
/// misbehaving handler does not block other side effects.
/// <see cref="OperationCanceledException"/> matching the supplied cancellation
/// token is the one exception that propagates so the originating request can
/// abort cleanly.
/// </para>
/// <para>
/// Event-to-handler matching uses <c>domainEvent.GetType()</c> exactly. Handlers registered
/// against a base class or interface of the runtime type are not invoked.
/// </para>
/// </remarks>
internal sealed partial class MediatorDomainEventPublisher : IDomainEventPublisher, IReportingDomainEventPublisher
{
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> s_invokerCache = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MediatorDomainEventPublisher> _logger;

    public MediatorDomainEventPublisher(
        IServiceProvider serviceProvider,
        ILogger<MediatorDomainEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    // Best-effort by contract: the report is discarded because this path has no retry mechanism — it runs
    // post-commit. Failures are already logged by the shared dispatch below.
    public async ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) =>
        await PublishReportingAsync(domainEvent, null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Reflection over IDomainEventHandler<TEvent> for the runtime event type. The handler types are reached via DI-based registration (AddDomainEventHandler<TEvent, THandler>) which preserves them through trimming; consumers needing strict NativeAOT guarantees can supply a custom IDomainEventPublisher implementation.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling",
        Justification = "Reflection over IDomainEventHandler<TEvent> for the runtime event type. The handler types are reached via DI-based registration (AddDomainEventHandler<TEvent, THandler>) which preserves them through trimming; consumers needing strict NativeAOT guarantees can supply a custom IDomainEventPublisher implementation.")]
    public async ValueTask<DomainEventDispatchReport> PublishReportingAsync(
        IDomainEvent domainEvent,
        IReadOnlySet<string>? completedHandlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        var eventTypeName = eventType.FullName ?? eventType.Name;
        var invoker = s_invokerCache.GetOrAdd(eventType, CreateInvoker);

        IEnumerable handlers;
        try
        {
            handlers = invoker.ResolveHandlers(_serviceProvider);
        }
        catch (Exception ex)
        {
            LogResolveFailure(_logger, ex, eventTypeName);
            return new DomainEventDispatchReport([], [], ex);
        }

        // Cumulative: a handler the caller already completed is carried into the report unchanged, so the
        // caller can overwrite its persisted set rather than merge.
        List<string> completed = [];
        List<DomainEventHandlerFailure>? failures = null;
        var hasHandler = false;
        foreach (var handler in handlers)
        {
            hasHandler = true;
            var handlerType = handler.GetType();
            var handlerIdentity = DomainEventDispatchReport.HandlerIdentity(handlerType);

            if (completedHandlers?.Contains(handlerIdentity) == true)
            {
                completed.Add(handlerIdentity);
                continue;
            }

            try
            {
                await invoker.InvokeAsync(handler, domainEvent, cancellationToken).ConfigureAwait(false);
                completed.Add(handlerIdentity);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log the plain type name (unchanged from before) for readability; the report carries the
                // collision-resistant identity, which is what retry bookkeeping matches on.
                LogHandlerFailure(_logger, ex, handlerType.FullName ?? handlerType.Name, eventTypeName);
                (failures ??= []).Add(new DomainEventHandlerFailure(handlerIdentity, ex));
            }
        }

        if (!hasHandler)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogNoHandlers(_logger, eventTypeName);
            return DomainEventDispatchReport.Empty;
        }

        return new DomainEventDispatchReport(completed, (IReadOnlyList<DomainEventHandlerFailure>?)failures ?? [], null);
    }

    [RequiresUnreferencedCode("Constructs closed generic types via reflection.")]
    [RequiresDynamicCode("Constructs closed generic types via reflection.")]
    private static HandlerInvoker CreateInvoker(Type eventType)
    {
        var handlerInterface = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerInterface);
        // The interface declares HandleAsync; reach for it by name. Using the bare string
        // (rather than a closed-generic nameof) avoids the speculative IDomainEventHandler<IDomainEvent>
        // instantiation that was in the original lookup expression.
        var handleAsync = handlerInterface.GetMethod(HandleAsyncMethodName)
            ?? throw new InvalidOperationException(
                $"IDomainEventHandler<{eventType.FullName}> is missing a {HandleAsyncMethodName} method.");
        return new HandlerInvoker(enumerableType, handleAsync);
    }

    private const string HandleAsyncMethodName = nameof(IDomainEventHandler<DummyDomainEvent>.HandleAsync);

    /// <summary>
    /// Sentinel type used solely for the <see cref="HandleAsyncMethodName"/> <c>nameof</c> lookup.
    /// Avoids instantiating <c>IDomainEventHandler&lt;IDomainEvent&gt;</c> just for a method name.
    /// </summary>
    private sealed record DummyDomainEvent : IDomainEvent
    {
        public DateTimeOffset OccurredAt => default;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to resolve handlers for domain event {EventType}.")]
    private static partial void LogResolveFailure(ILogger logger, Exception ex, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Domain event handler {HandlerType} threw for event {EventType}.")]
    private static partial void LogHandlerFailure(ILogger logger, Exception ex, string handlerType, string eventType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No IDomainEventHandler<{EventType}> registered; event ignored.")]
    private static partial void LogNoHandlers(ILogger logger, string eventType);

    private sealed class HandlerInvoker
    {
        private readonly Type _enumerableType;
        private readonly MethodInfo _handleAsync;

        public HandlerInvoker(Type enumerableType, MethodInfo handleAsync)
        {
            _enumerableType = enumerableType;
            _handleAsync = handleAsync;
        }

        public IEnumerable ResolveHandlers(IServiceProvider provider)
            => (IEnumerable)provider.GetRequiredService(_enumerableType);

        public ValueTask InvokeAsync(object handler, IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            object? result;
            try
            {
                result = _handleAsync.Invoke(handler, [domainEvent, cancellationToken]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                // MethodInfo.Invoke wraps synchronous handler exceptions in TargetInvocationException.
                // Unwrap so OperationCanceledException can be matched by the caller's filter and
                // other exceptions are logged with their actual type and stack trace.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable
            }

            // IDomainEventHandler<TEvent>.HandleAsync returns ValueTask by contract; null or
            // any other shape would mean the contract is violated. Direct cast surfaces the
            // violation immediately rather than masking it with a CompletedTask fallback.
            return (ValueTask)result!;
        }
    }
}