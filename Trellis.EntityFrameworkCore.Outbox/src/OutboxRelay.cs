namespace Trellis.EntityFrameworkCore;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trellis.Mediator;

/// <summary>
/// Background relay that drains pending <see cref="OutboxMessage"/> rows from a durable, crash-safe
/// store and routes each by <see cref="OutboxMessage.Kind"/>: <see cref="OutboxMessageKind.Domain"/>
/// rows re-dispatch to their <see cref="IDomainEventHandler{TEvent}"/>s via
/// <see cref="IDomainEventPublisher"/> (the same fan-out the in-pipeline dispatch would perform), and any
/// integration events their translators emit into <see cref="IIntegrationEventCollector"/> are staged as
/// new <see cref="OutboxMessageKind.Integration"/> rows; those are later published through
/// <see cref="IIntegrationEventPublisher"/>.
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
        var scope = _scopeFactory.CreateAsyncScope();
        await using var scopeLifetime = scope.ConfigureAwait(false);
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        // DateTime (not DateTimeOffset) so the lease comparison translates on every EF provider, including
        // SQLite, which cannot compare offset-bearing DateTimeOffset values in SQL.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var claimToken = Guid.NewGuid();
        var leaseExpiry = now + _options.LeaseDuration;

        // 1. Find eligible rows (pending, under the attempt cap, not under a live lease), oldest first.
        var candidates = await context.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null
                        && m.Attempts < _options.MaxAttempts
                        && (m.LockedUntil == null || m.LockedUntil <= now))
            .OrderBy(m => m.Sequence)
            .Take(_options.BatchSize)
            .Select(m => m.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
            return 0;

        // 2. Atomically claim them for this drain. Two relay instances running this UPDATE are serialized
        //    by row locks and each re-evaluates the guard, so every row is claimed by exactly one
        //    instance — the scale-out safety the relay depends on. The predicate mirrors the candidate
        //    filter (including the attempt cap) so a row another instance just parked between the read and
        //    this claim is not re-claimed.
        await context.Set<OutboxMessage>()
            .Where(m => candidates.Contains(m.Sequence)
                        && m.ProcessedAt == null
                        && m.Attempts < _options.MaxAttempts
                        && (m.LockedUntil == null || m.LockedUntil <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.LockedBy, claimToken)
                .SetProperty(m => m.LockedUntil, leaseExpiry), cancellationToken)
            .ConfigureAwait(false);

        // 3. Load the rows this drain actually won (a competing instance may have claimed some first).
        var batch = await context.Set<OutboxMessage>()
            .Where(m => m.LockedBy == claimToken)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0)
            return 0;

        List<(OutboxMessage Message, Exception Error)>? failures = null;
        Dictionary<OutboxMessage, List<OutboxMessage>>? stagedBySource = null;
        foreach (var message in batch)
        {
            try
            {
                if (message.Kind == OutboxMessageKind.Integration)
                {
                    var integrationEvent = Deserialize<IIntegrationEvent>(message);
                    await PublishIntegrationAsync(message.Id, integrationEvent, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var domainEvent = Deserialize<IDomainEvent>(message);
                    var produced = await PublishDomainAndCollectAsync(domainEvent, cancellationToken).ConfigureAwait(false);
                    if (produced.Count > 0)
                    {
                        // Materialize every row for THIS message before staging any of them, so a
                        // serialization failure on a later produced event does not leave earlier rows
                        // staged for a domain message that the catch then records as failed. The local
                        // list is discarded on throw; only a fully-converted set is enrolled.
                        var rows = new List<OutboxMessage>(produced.Count);
                        foreach (var integrationEvent in produced)
                            rows.Add(CreateIntegrationRow(integrationEvent));
                        (stagedBySource ??= [])[message] = rows;
                    }
                }

                message.MarkProcessed(_timeProvider.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var retryDelay = OutboxRetryBackoff.Compute(
                    message.Id,
                    message.Attempts + 1,
                    _options.RetryBackoff,
                    _options.MaxRetryBackoff,
                    _options.RetryBackoffJitter);
                // Schedule the retry from the time of THIS failure, not the drain-start `now`: a slow batch
                // could otherwise set a retry time already in the past, defeating the backoff and recreating
                // the tight retry loop. Deterministic under a fake clock that is not advanced mid-drain.
                var failureNow = _timeProvider.GetUtcNow().UtcDateTime;
                message.RecordFailure(ex.Message, failureNow + retryDelay);
                (failures ??= []).Add((message, ex));
            }
        }

        // Integration events translated from the domain events in this batch are staged transactionally
        // with their source messages' MarkProcessed, so a domain event is marked delivered only once the
        // integration rows it produced are durably enrolled. They are picked up on a later drain.
        if (stagedBySource is not null)
            foreach (var rows in stagedBySource.Values)
                context.Set<OutboxMessage>().AddRange(rows);

        // Persist MarkProcessed (releases the lease) / RecordFailure (backs the lease off to the retry
        // time) under the LockedBy concurrency guard, then log. Emitting the failure logs only after the
        // save succeeds keeps the alertable MessageParked (and the retry Warning) honest: a write that
        // never persisted must not raise a false "parked" alert. Rows whose lease was stolen mid-batch are
        // abandoned by SaveDrainAsync, so they are dropped from the failure logs too.
        var stolen = await SaveDrainAsync(context, stagedBySource, cancellationToken).ConfigureAwait(false);
        if (stolen is not null)
        {
            failures?.RemoveAll(f => stolen.Contains(f.Message));
            OutboxRelayLog.LeaseLost(_logger, typeof(TContext).Name, stolen.Count, _options.LeaseDuration);
        }

        LogFailures(failures);
        var processed = batch.Count - (stolen?.Count ?? 0);
        OutboxRelayLog.DrainCompleted(_logger, processed);

        return processed;
    }

    // Saves the drain's bookkeeping while honoring the LockedBy concurrency token. If a slow batch
    // outlived its lease and another instance reclaimed a row, that row's UPDATE matches no row (its
    // LockedBy changed) and EF raises a concurrency conflict; we abandon our pending changes for that row
    // — and drop any integration rows it produced, so they are not double-enrolled — rather than clobber
    // the instance that now owns it, then retry the rest. Returns the abandoned rows, or null when none
    // (the common case, so a healthy drain pays no extra cost).
    private static async Task<List<OutboxMessage>?> SaveDrainAsync(
        TContext context,
        Dictionary<OutboxMessage, List<OutboxMessage>>? stagedBySource,
        CancellationToken cancellationToken)
    {
        List<OutboxMessage>? stolen = null;
        while (true)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return stolen;
            }
            catch (DbUpdateConcurrencyException ex)
                when (ex.Entries.Count > 0 && ex.Entries.All(e => e.Entity is OutboxMessage))
            {
                foreach (var entry in ex.Entries)
                {
                    var source = (OutboxMessage)entry.Entity;
                    if (stagedBySource is not null && stagedBySource.TryGetValue(source, out var integrationRows))
                        foreach (var row in integrationRows)
                            context.Entry(row).State = EntityState.Detached;

                    entry.State = EntityState.Detached;
                    (stolen ??= []).Add(source);
                }
            }
        }
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

    // Publish a domain event in a dedicated scope so handlers that inject TContext (or any scoped
    // service) receive their own instances — never the relay's bookkeeping context. Otherwise a
    // handler's tracked changes, and any aggregate events it raises, would be persisted/captured by the
    // relay's own SaveChanges. This mirrors in-pipeline dispatch, where handlers run after the commit and
    // the unit of work has already closed. After publishing, drain whatever integration events the
    // handlers (translators) produced in this same scope so they can be staged for delivery.
    private async Task<IReadOnlyList<IIntegrationEvent>> PublishDomainAndCollectAsync(
        IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var publishScope = _scopeFactory.CreateAsyncScope();
        await using var publishScopeLifetime = publishScope.ConfigureAwait(false);
        var publisher = publishScope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
        await publisher.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);

        // The collector is optional: consumers that do not translate integration events never register
        // it, and existing domain-only outboxes are unaffected.
        var collector = publishScope.ServiceProvider.GetService<IIntegrationEventCollector>();
        return collector?.DrainPending() ?? [];
    }

    // The row's own id travels with the event so a broker adapter can stamp it on the wire. Delivery is
    // at-least-once, so the same row can be published more than once; carrying a per-attempt id instead
    // would make each copy look like a distinct message and defeat the consumer's inbox dedup.
    private async Task PublishIntegrationAsync(
        Guid messageId, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var publishScope = _scopeFactory.CreateAsyncScope();
        await using var publishScopeLifetime = publishScope.ConfigureAwait(false);
        var publisher = publishScope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(new OutboundIntegrationMessage(messageId, integrationEvent), cancellationToken)
            .ConfigureAwait(false);
    }

    private static OutboxMessage CreateIntegrationRow(IIntegrationEvent integrationEvent)
    {
        var type = integrationEvent.GetType();
        var eventType = type.AssemblyQualifiedName
            ?? throw new InvalidOperationException(
                $"Integration event type '{type}' has no AssemblyQualifiedName and cannot be relayed from the outbox; use a concrete, non-generic event type.");

        return OutboxMessage.Create(
            Guid.CreateVersion7(),
            integrationEvent.OccurredAt,
            eventType,
            JsonSerializer.Serialize(integrationEvent, type, OutboxEventSerialization.Options),
            OutboxMessageKind.Integration);
    }

    private static T Deserialize<T>(OutboxMessage message)
        where T : class
    {
        var type = Type.GetType(message.EventType)
            ?? throw new InvalidOperationException(
                $"Cannot resolve outbox event type '{message.EventType}'. The producing assembly must be loaded by the relay.");

        return JsonSerializer.Deserialize(message.Payload, type, OutboxEventSerialization.Options) as T
            ?? throw new InvalidOperationException(
                $"Outbox payload for '{message.EventType}' did not deserialize to an {typeof(T).Name}.");
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
            "Outbox relay parked message {MessageId} ({EventType}) after {Attempts} failed attempts; it is dead-lettered and will not be retried until replayed via IOutboxMaintenance.");

    private static readonly Action<ILogger, string, int, TimeSpan, Exception?> s_leaseLost =
        LoggerMessage.Define<string, int, TimeSpan>(
            LogLevel.Warning,
            new EventId(6, "OutboxRelay.LeaseLost"),
            "Outbox relay for {ContextType} lost its lease on {Count} message(s) mid-drain; another instance reclaimed them, so this drain abandoned its writes for them. Increase LeaseDuration (currently {LeaseDuration}) above the worst-case batch publish time.");

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

    public static void LeaseLost(ILogger logger, string contextType, int count, TimeSpan leaseDuration) =>
        s_leaseLost(logger, contextType, count, leaseDuration, null);
}
