namespace Trellis.EntityFrameworkCore;

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis.Mediator;

/// <summary>
/// EF Core <see cref="IInboxDispatcher"/>: deduplicates on <c>(ConsumerId, MessageId)</c> and invokes the
/// event's <see cref="IIntegrationEventHandler{TEvent}"/>s, staging the dedup record and the handlers' side
/// effects into a single <c>SaveChangesAsync</c> so they commit atomically under EF Core's implicit
/// transaction. Handlers run before that save, so a handler throw propagates with nothing persisted and the
/// transport redelivers — delivery stays at-least-once while processing is made effectively-once.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the default <see cref="IIntegrationEventPublisher"/> (which logs and swallows handler exceptions),
/// the inbox is <b>non-swallowing</b>: a failed handler must not leave the message marked processed.
/// </para>
/// <para>
/// No user-initiated transaction is opened, so the inbox composes with a retrying execution strategy
/// (<c>EnableRetryOnFailure</c>), just like the rest of Trellis. Handlers participate in the dispatcher's
/// unit of work and must not call <c>SaveChanges</c> themselves; they stage changes the dispatcher commits.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that owns the inbox table.</typeparam>
internal sealed class InboxDispatcher<TContext> : IInboxDispatcher
    where TContext : DbContext
{
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> s_invokerCache = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InboxOptions _options;
    private readonly ILogger<InboxDispatcher<TContext>> _logger;

    public InboxDispatcher(
        IServiceScopeFactory scopeFactory,
        InboxOptions options,
        ILogger<InboxDispatcher<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InboxDispatchOutcome> DispatchAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<TContext>();
        var store = provider.GetRequiredService<IInboxStore>();

        // Fast path: the dedup row already exists, so this is a redelivery we have processed. Nothing is
        // staged, so there is nothing to save.
        if (!await store.TryRecordAsync(_options.ConsumerId, ToInboxRecord(envelope), cancellationToken).ConfigureAwait(false))
        {
            InboxDispatcherLog.DuplicateSkipped(_logger, envelope.MessageId, _options.ConsumerId);
            return InboxDispatchOutcome.SkippedDuplicate;
        }

        // Run the handlers BEFORE saving: a handler throw then propagates with nothing persisted (no dedup
        // row, no side effects) and the transport redelivers. Their writes and the dedup row share the one
        // SaveChanges below, so they commit atomically under EF Core's implicit transaction. No
        // user-initiated transaction is opened, so a retrying execution strategy can wrap the save.
        await InvokeHandlersAsync(provider, envelope.Event, cancellationToken).ConfigureAwait(false);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return InboxDispatchOutcome.Processed;
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsDuplicateKey(ex))
        {
            // The duplicate key is either a concurrent dispatch that recorded this (ConsumerId, MessageId)
            // first (already processed elsewhere -> this call's handlers ran, but their staged writes rolled
            // back with the failed save) or a handler's OWN unique-constraint violation
            // (which must surface so the message is not falsely marked processed). The failing entry alone
            // cannot tell them apart: EF Core attributes a *batched* SaveChanges failure to every entry in
            // the batch, and the inbox row always shares the batch with the handler writes. So check the
            // ground truth in a fresh scope -- the dedup row is committed only if a concurrent dispatch won.
            if (!await DedupRowExistsAsync(envelope, cancellationToken).ConfigureAwait(false))
                throw;

            InboxDispatcherLog.DuplicateSkipped(_logger, envelope.MessageId, _options.ConsumerId);
            return InboxDispatchOutcome.SkippedDuplicate;
        }
    }

    // Reads, in a fresh scope so it is unaffected by the aborted SaveChanges, whether the dedup row for this
    // (ConsumerId, MessageId) has been committed -- true only when a concurrent dispatch recorded it first.
    // A handler's own unique violation leaves no such row (the whole batch rolled back), so the failure
    // propagates and the transport redelivers.
    private async Task<bool> DedupRowExistsAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        return await context.Set<InboxMessage>()
            .AsNoTracking()
            .AnyAsync(m => m.ConsumerId == _options.ConsumerId && m.MessageId == envelope.MessageId, cancellationToken)
            .ConfigureAwait(false);
    }

    // Maps the consume-side envelope to the persistence-native dedup record the store records. The
    // event's stable type name (assembly-qualified) is captured here so the store contract carries no
    // messaging type.
    private static InboxRecord ToInboxRecord(IntegrationEnvelope envelope)
    {
        var type = envelope.Event.GetType();
        var eventType = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        return new InboxRecord(
            envelope.MessageId, eventType, envelope.Event.OccurredAt,
            envelope.MessageSource, envelope.CausationId, envelope.CorrelationId);
    }

    // Resolve and invoke every IIntegrationEventHandler<TConcrete> for the runtime event type. Exceptions
    // PROPAGATE: the caller never reaches SaveChanges, so nothing persists and the transport redelivers. All
    // handlers share the dispatcher's TContext, so their writes commit together in its one SaveChanges.
    private static async Task InvokeHandlersAsync(IServiceProvider provider, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var invoker = s_invokerCache.GetOrAdd(integrationEvent.GetType(), CreateInvoker);

        foreach (var handler in invoker.ResolveHandlers(provider))
            await invoker.InvokeAsync(handler, integrationEvent, cancellationToken).ConfigureAwait(false);
    }

    private static HandlerInvoker CreateInvoker(Type eventType)
    {
        var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerInterface);
        var handleAsync = handlerInterface.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))
            ?? throw new InvalidOperationException(
                $"IIntegrationEventHandler<{eventType.FullName}> is missing a HandleAsync method.");
        return new HandlerInvoker(enumerableType, handleAsync);
    }

    private sealed class HandlerInvoker(Type enumerableType, MethodInfo handleAsync)
    {
        public IEnumerable ResolveHandlers(IServiceProvider provider)
            => (IEnumerable)provider.GetRequiredService(enumerableType);

        public ValueTask InvokeAsync(object handler, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            object? result;
            try
            {
                result = handleAsync.Invoke(handler, [integrationEvent, cancellationToken]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable
            }

            return (ValueTask)result!;
        }
    }
}

/// <summary>High-performance log delegates for <see cref="InboxDispatcher{TContext}"/> (satisfies CA1848).</summary>
internal static class InboxDispatcherLog
{
    private static readonly Action<ILogger, Guid, string, Exception?> s_duplicateSkipped =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Debug,
            new EventId(1, "InboxDispatcher.DuplicateSkipped"),
            "Inbox skipped already-processed message {MessageId} for consumer {ConsumerId}.");

    public static void DuplicateSkipped(ILogger logger, Guid messageId, string consumerId) =>
        s_duplicateSkipped(logger, messageId, consumerId, null);
}
