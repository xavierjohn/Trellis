namespace Trellis.EntityFrameworkCore;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trellis.Mediator;

/// <summary>
/// Background relay that drains pending <see cref="OutboxMessage"/> rows and re-dispatches each event
/// to its <see cref="IDomainEventHandler{TEvent}"/>s via <see cref="IDomainEventPublisher"/> — the same
/// fan-out the in-pipeline dispatch would perform, but from a durable, crash-safe store.
/// </summary>
/// <remarks>
/// <para>
/// The guarantee is at-least-once <b>delivery</b>: a message is marked processed once it has been handed
/// to the publisher. Per the <see cref="IDomainEventHandler{TEvent}"/> contract the publisher logs and
/// swallows handler exceptions, so a <i>failing handler does not cause the message to retry</i> — only
/// infrastructure failures (deserialization, the relay's own save) leave a message pending for a later
/// attempt, up to <see cref="OutboxOptions.MaxAttempts"/>. Retry-until-handlers-succeed would require a
/// non-swallowing publish path and is a planned follow-up. Handlers must be idempotent, since a crash
/// between dispatch and the relay's save re-delivers the message.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> that owns the outbox table.</typeparam>
internal sealed class OutboxRelay<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxRelay<TContext>> _logger;

    public OutboxRelay(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        OutboxOptions options,
        ILogger<OutboxRelay<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int drained;
            try
            {
                drained = await DrainAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                OutboxRelayLog.DrainFailed(_logger, _options.PollInterval, ex);
                drained = 0;
            }

            if (drained == 0)
            {
                try
                {
                    await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Drains a single batch. Exposed internally so tests can pump the relay deterministically without
    /// running the hosted-service loop.
    /// </summary>
    internal async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var batch = await context.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && m.Attempts < _options.MaxAttempts)
            .OrderBy(m => m.Sequence)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in batch)
        {
            try
            {
                var domainEvent = Deserialize(message);
                await PublishInIsolatedScopeAsync(domainEvent, cancellationToken).ConfigureAwait(false);
                message.MarkProcessed(_timeProvider.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.RecordFailure(ex.Message);
                OutboxRelayLog.RelayFailed(_logger, message.Id, message.EventType, ex);
            }
        }

        if (batch.Count > 0)
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return batch.Count;
    }

    // Publish in a dedicated scope so handlers that inject TContext (or any scoped service) receive
    // their own instances — never the relay's bookkeeping context. Otherwise a handler's tracked
    // changes, and any aggregate events it raises, would be persisted/captured by the relay's own
    // SaveChanges below. This mirrors in-pipeline dispatch, where handlers run after the commit and the
    // unit of work has already closed, so their context mutations are not auto-persisted by the relay.
    private async Task PublishInIsolatedScopeAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        await using var publishScope = _scopeFactory.CreateAsyncScope();
        var publisher = publishScope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
        await publisher.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);
    }

    private static IDomainEvent Deserialize(OutboxMessage message)
    {
        var type = Type.GetType(message.EventType)
            ?? throw new InvalidOperationException(
                $"Cannot resolve outbox event type '{message.EventType}'. The producing assembly must be loaded by the relay.");

        return JsonSerializer.Deserialize(message.Payload, type) as IDomainEvent
            ?? throw new InvalidOperationException(
                $"Outbox payload for '{message.EventType}' did not deserialize to an {nameof(IDomainEvent)}.");
    }
}

/// <summary>High-performance log delegates for <see cref="OutboxRelay{TContext}"/> (satisfies CA1848).</summary>
internal static class OutboxRelayLog
{
    private static readonly Action<ILogger, TimeSpan, Exception?> s_drainFailed =
        LoggerMessage.Define<TimeSpan>(
            LogLevel.Error,
            new EventId(1, "OutboxRelay.DrainFailed"),
            "Outbox relay drain failed; retrying after {PollInterval}.");

    private static readonly Action<ILogger, Guid, string, Exception?> s_relayFailed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Error,
            new EventId(2, "OutboxRelay.RelayFailed"),
            "Failed to relay outbox message {Id} ({EventType}).");

    public static void DrainFailed(ILogger logger, TimeSpan pollInterval, Exception exception) =>
        s_drainFailed(logger, pollInterval, exception);

    public static void RelayFailed(ILogger logger, Guid id, string eventType, Exception exception) =>
        s_relayFailed(logger, id, eventType, exception);
}
