namespace Trellis.Mediator;

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IIntegrationEventPublisher"/> implementation that resolves
/// <see cref="IIntegrationEventHandler{TEvent}"/> instances from the request's DI scope using the
/// event's runtime type and invokes each in turn. This is the in-process consumer side; replace the
/// registration with a broker adapter to deliver to other services.
/// </summary>
/// <remarks>
/// Non-cancellation handler exceptions are logged at <see cref="LogLevel.Error"/> and swallowed so a
/// single misbehaving consumer does not block the others. <see cref="OperationCanceledException"/>
/// matching the supplied token propagates so the relay can abort cleanly.
/// </remarks>
internal sealed partial class MediatorIntegrationEventPublisher : IIntegrationEventPublisher
{
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> s_invokerCache = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MediatorIntegrationEventPublisher> _logger;

    public MediatorIntegrationEventPublisher(
        IServiceProvider serviceProvider,
        ILogger<MediatorIntegrationEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Reflection over IIntegrationEventHandler<TEvent> for the runtime event type. The handler types are reached via DI-based registration (AddIntegrationEventHandler<TEvent, THandler>) which preserves them through trimming; consumers needing strict NativeAOT guarantees can supply a custom IIntegrationEventPublisher implementation.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling",
        Justification = "Reflection over IIntegrationEventHandler<TEvent> for the runtime event type. The handler types are reached via DI-based registration (AddIntegrationEventHandler<TEvent, THandler>) which preserves them through trimming; consumers needing strict NativeAOT guarantees can supply a custom IIntegrationEventPublisher implementation.")]
    public async ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The id identifies the message on a wire. This publisher is the in-process path - there is no wire
        // and nothing to deduplicate - so only the event matters here.
        var integrationEvent = message.Event;
        var eventType = integrationEvent.GetType();
        var invoker = s_invokerCache.GetOrAdd(eventType, CreateInvoker);

        IEnumerable handlers;
        try
        {
            handlers = invoker.ResolveHandlers(_serviceProvider);
        }
        catch (Exception ex)
        {
            LogResolveFailure(_logger, ex, eventType.FullName ?? eventType.Name);
            return;
        }

        var hasHandler = false;
        foreach (var handler in handlers)
        {
            hasHandler = true;
            try
            {
                await invoker.InvokeAsync(handler, integrationEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogHandlerFailure(_logger, ex, handler.GetType().FullName ?? handler.GetType().Name, eventType.FullName ?? eventType.Name);
            }
        }

        if (!hasHandler && _logger.IsEnabled(LogLevel.Debug))
            LogNoHandlers(_logger, eventType.FullName ?? eventType.Name);
    }

    [RequiresUnreferencedCode("Constructs closed generic types via reflection.")]
    [RequiresDynamicCode("Constructs closed generic types via reflection.")]
    private static HandlerInvoker CreateInvoker(Type eventType)
    {
        var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerInterface);
        var handleAsync = handlerInterface.GetMethod(HandleAsyncMethodName)
            ?? throw new InvalidOperationException(
                $"IIntegrationEventHandler<{eventType.FullName}> is missing a {HandleAsyncMethodName} method.");
        return new HandlerInvoker(enumerableType, handleAsync);
    }

    private const string HandleAsyncMethodName = nameof(IIntegrationEventHandler<DummyIntegrationEvent>.HandleAsync);

    /// <summary>
    /// Sentinel type used solely for the <see cref="HandleAsyncMethodName"/> <c>nameof</c> lookup.
    /// </summary>
    private sealed record DummyIntegrationEvent : IIntegrationEvent
    {
        public DateTimeOffset OccurredAt => default;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to resolve handlers for integration event {EventType}.")]
    private static partial void LogResolveFailure(ILogger logger, Exception ex, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Integration event handler {HandlerType} threw for event {EventType}.")]
    private static partial void LogHandlerFailure(ILogger logger, Exception ex, string handlerType, string eventType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No IIntegrationEventHandler<{EventType}> registered; event ignored.")]
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

        public ValueTask InvokeAsync(object handler, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            object? result;
            try
            {
                result = _handleAsync.Invoke(handler, [integrationEvent, cancellationToken]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable
            }

            return (ValueTask)result!;
        }
    }
}
