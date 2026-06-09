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
        OutboxRelayLog.Started(_logger, typeof(TContext).Name, _options.PollInterval, _options.BatchSize, _options.MaxAttempts);

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

        List<(OutboxMessage Message, Exception Error)>? failures = null;
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
                (failures ??= []).Add((message, ex));
            }
        }

        if (batch.Count > 0)
        {
            // Persist MarkProcessed / RecordFailure first, then log. Emitting the failure logs only
            // after the save succeeds keeps the alertable MessageParked (and the retry Warning) honest:
            // if this save throws, the Attempts increments never persisted, the message is NOT parked,
            // ExecuteAsync logs DrainFailed instead, and no false "parked" alert is raised.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogFailures(failures);
            OutboxRelayLog.DrainCompleted(_logger, batch.Count);
        }

        return batch.Count;
    }

    private void LogFailures(List<(OutboxMessage Message, Exception Error)>? failures)
    {
        if (failures is null)
            return;

        foreach (var (message, error) in failures)
        {
            // Attempts now reflects the persisted value, so the parked-vs-retry decision matches the DB.
            // Parked is the alertable, intervention-required signal; a retry is a transient, self-healing
            // Warning. Both carry the exception and structured fields for triage.
            if (message.Attempts >= _options.MaxAttempts)
                OutboxRelayLog.MessageParked(_logger, message.Id, message.EventType, message.Attempts, error);
            else
                OutboxRelayLog.RelayAttemptFailed(_logger, message.Id, message.EventType, message.Attempts, _options.MaxAttempts, error);
        }
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
    private static readonly Action<ILogger, string, TimeSpan, int, int, Exception?> s_started =
        LoggerMessage.Define<string, TimeSpan, int, int>(
            LogLevel.Information,
            new EventId(1, "OutboxRelay.Started"),
            "Outbox relay started for {ContextType}; polling every {PollInterval}, batch size {BatchSize}, max attempts {MaxAttempts}.");

    private static readonly Action<ILogger, int, Exception?> s_drainCompleted =
        LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(2, "OutboxRelay.DrainCompleted"),
            "Outbox relay processed {MessageCount} outbox message(s) this cycle.");

    private static readonly Action<ILogger, TimeSpan, Exception?> s_drainFailed =
        LoggerMessage.Define<TimeSpan>(
            LogLevel.Error,
            new EventId(3, "OutboxRelay.DrainFailed"),
            "Outbox relay drain cycle failed; retrying after {PollInterval}.");

    private static readonly Action<ILogger, Guid, string, int, int, Exception?> s_attemptFailed =
        LoggerMessage.Define<Guid, string, int, int>(
            LogLevel.Warning,
            new EventId(4, "OutboxRelay.RelayAttemptFailed"),
            "Outbox relay failed to deliver message {MessageId} ({EventType}) on attempt {Attempts} of {MaxAttempts}; it will be retried.");

    private static readonly Action<ILogger, Guid, string, int, Exception?> s_parked =
        LoggerMessage.Define<Guid, string, int>(
            LogLevel.Error,
            new EventId(5, "OutboxRelay.MessageParked"),
            "Outbox relay parked message {MessageId} ({EventType}) after {Attempts} failed attempts; it will not be retried and requires manual intervention.");

    public static void Started(ILogger logger, string contextType, TimeSpan pollInterval, int batchSize, int maxAttempts) =>
        s_started(logger, contextType, pollInterval, batchSize, maxAttempts, null);

    public static void DrainCompleted(ILogger logger, int messageCount) =>
        s_drainCompleted(logger, messageCount, null);

    public static void DrainFailed(ILogger logger, TimeSpan pollInterval, Exception exception) =>
        s_drainFailed(logger, pollInterval, exception);

    public static void RelayAttemptFailed(ILogger logger, Guid messageId, string eventType, int attempts, int maxAttempts, Exception exception) =>
        s_attemptFailed(logger, messageId, eventType, attempts, maxAttempts, exception);

    public static void MessageParked(ILogger logger, Guid messageId, string eventType, int attempts, Exception exception) =>
        s_parked(logger, messageId, eventType, attempts, exception);
}
