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
internal sealed partial class MediatorDomainEventPublisher : IDomainEventPublisher
{
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> s_invokerCache = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MediatorDomainEventPublisher> _logger;

    public MediatorDomainEventPublisher(
        IServiceProvider serviceProvider,
        ILogger<MediatorDomainEventPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Reflection over IDomainEventHandler<TEvent> for the runtime event type. The handler types are reached via DI-based registration (AddDomainEventHandler<TEvent, THandler>) which preserves them through trimming; consumers needing strict NativeAOT guarantees can supply a custom IDomainEventPublisher implementation.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling",
        Justification = "Reflection over IDomainEventHandler<TEvent> for the runtime event type. The handler types are reached via DI-based registration (AddDomainEventHandler<TEvent, THandler>) which preserves them through trimming; consumers needing strict NativeAOT guarantees can supply a custom IDomainEventPublisher implementation.")]
    public async ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
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
                await invoker.InvokeAsync(handler, domainEvent, cancellationToken).ConfigureAwait(false);
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
        var handlerInterface = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerInterface);
        var handleAsync = handlerInterface.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))
            ?? throw new InvalidOperationException(
                $"IDomainEventHandler<{eventType.FullName}> is missing a HandleAsync method.");
        return new HandlerInvoker(enumerableType, handleAsync);
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

            return result is ValueTask vt ? vt : ValueTask.CompletedTask;
        }
    }
}
