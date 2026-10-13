namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

internal sealed class OutboxReadinessHealthCheck<TContext>(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    OutboxOptions outboxOptions,
    OutboxReadinessOptions profile,
    OutboxRelayObservations<TContext> observations,
    ILogger<OutboxReadinessHealthCheck<TContext>> logger) : IHealthCheck
    where TContext : DbContext
{
    private readonly Lock _gate = new();
    private Task<ProbeResult>? _refresh;
    private Task<ProbeResult>? _databaseProbe;
    private ProbeResult? _cached;
    private QueueEvidence? _lastEvidence;
    private long _cachedAt;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var maxAttempts = outboxOptions.MaxAttempts;
        if (maxAttempts <= 0)
            throw new InvalidOperationException("OutboxOptions.MaxAttempts must be greater than zero.");

        ProbeResult probe;
        do
        {
            Task<ProbeResult> refresh;
            lock (_gate)
            {
                refresh = _cached is { } cached && cached.MaxAttempts == maxAttempts
                    && timeProvider.GetElapsedTime(_cachedAt) < profile.CacheDuration
                    ? Task.FromResult(cached)
                    : _refresh is { IsCompleted: false } running
                        ? running
                        : _refresh = RefreshAsync(maxAttempts);
            }

            probe = await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        while (probe.MaxAttempts != maxAttempts);

        cancellationToken.ThrowIfCancellationRequested();
        return Evaluate(probe, observations.Read());
    }

    private async Task<ProbeResult> RefreshAsync(int maxAttempts)
    {
        ProbeResult result;
        if (_databaseProbe is { IsCompleted: false })
            result = new(maxAttempts, null, "probe-timeout", null);
        else
        {
            var started = timeProvider.GetTimestamp();
            var deadline = new CancellationTokenSource(profile.ProbeTimeout, timeProvider);
            // A probe has the relay's background scope, not the initiating request's ambient tenancy.
            using (ExecutionContext.SuppressFlow())
                _databaseProbe = Task.Run(() => ReadMetadataAsync(maxAttempts, deadline));
            var remaining = profile.ProbeTimeout - timeProvider.GetElapsedTime(started);
            try
            {
                result = await _databaseProbe.WaitAsync(
                    remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                    timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                result = new(maxAttempts, null, "probe-timeout", null);
            }
        }

        if (result.FailureReason == "probe-timeout")
            OutboxReadinessLog.ProbeTimedOut(logger, typeof(TContext).Name);
        lock (_gate)
        {
            if (result.Evidence is not null)
                _lastEvidence = result.Evidence;
            else if (_lastEvidence?.MaxAttempts == maxAttempts)
                result = result with { Evidence = _lastEvidence };
            _cached = result;
            _cachedAt = timeProvider.GetTimestamp();
        }

        return result;
    }

    private async Task<ProbeResult> ReadMetadataAsync(int maxAttempts, CancellationTokenSource deadline)
    {
        using (deadline)
        {
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                QueueEvidence evidence;
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var database = scope.ServiceProvider.GetRequiredService<TContext>();
                    var rows = database.Set<OutboxMessage>().AsNoTracking().IgnoreAutoIncludes();
                    var pending = await rows
                        .Where(m => m.ProcessedAt == null && m.Attempts < maxAttempts)
                        .OrderBy(m => m.Sequence)
                        .Select(m => m.Sequence)
                        .Take(profile.PendingUnhealthyThreshold)
                        .CountAsync(deadline.Token).ConfigureAwait(false);
                    var pendingSampledAt = timeProvider.GetUtcNow();
                    var parked = await rows
                        .AnyAsync(m => m.ProcessedAt == null && m.Attempts >= maxAttempts, deadline.Token)
                        .ConfigureAwait(false);
                    evidence = new(maxAttempts, pending, parked ? 1 : 0, pendingSampledAt, timeProvider.GetUtcNow());
                }

                return deadline.IsCancellationRequested
                    ? new(maxAttempts, null, "probe-timeout", null)
                    : new(maxAttempts, evidence, null, null);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                return new(maxAttempts, null, "probe-timeout", null);
            }
            catch (Exception exception)
            {
                var faultType = exception.GetType().Name;
                OutboxReadinessLog.ProbeFailed(logger, typeof(TContext).Name, faultType);
                return new(maxAttempts, null, "probe-failed", faultType);
            }
        }
    }

    private HealthCheckResult Evaluate(ProbeResult probe, OutboxRelayEvidence relay)
    {
        var status = HealthStatus.Healthy;
        var reasons = new List<string>();
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["profile"] = "staged-operational-reference",
            ["storageContext"] = typeof(TContext).Name,
            ["maxAttempts"] = probe.MaxAttempts,
            ["metadataAvailable"] = probe.FailureReason is null,
            ["pendingCountCap"] = profile.PendingUnhealthyThreshold,
            ["parkedCountCap"] = 1,
            ["pendingDegradedThreshold"] = profile.PendingDegradedThreshold,
            ["pendingUnhealthyThreshold"] = profile.PendingUnhealthyThreshold,
            ["relayDegradedAfter"] = profile.RelayDegradedAfter,
            ["relayUnhealthyAfter"] = profile.RelayUnhealthyAfter,
            ["startupGracePeriod"] = profile.StartupGracePeriod,
            ["probeTimeout"] = profile.ProbeTimeout,
            ["cacheDuration"] = profile.CacheDuration,
            ["evidenceMaxAge"] = profile.EvidenceMaxAge,
            ["relayScope"] = "local-instance",
            ["relaySampledAt"] = relay.SampledAt,
            ["deliveredRows"] = relay.DeliveredRows,
            ["captureAge"] = "deferred",
            ["consumerLag"] = "disabled",
            ["sampling"] = "separate-metadata-queries-and-local-relay",
        };
        if (probe.FailureReason is not null)
            AddCondition(HealthStatus.Unhealthy, probe.FailureReason);
        if (probe.FaultType is not null)
            data["probeFaultType"] = probe.FaultType;
        if (probe.Evidence is { } evidence)
        {
            data["pendingCount"] = evidence.PendingCount;
            data["pendingCountIsLowerBound"] = evidence.PendingCount == profile.PendingUnhealthyThreshold;
            data["parkedCount"] = evidence.ParkedCount;
            data["parkedCountIsLowerBound"] = evidence.ParkedCount > 0;
            data["pendingSampledAt"] = evidence.PendingSampledAt;
            data["parkedSampledAt"] = evidence.ParkedSampledAt;
            var pendingSampleAge = relay.SampledAt - evidence.PendingSampledAt;
            var parkedSampleAge = relay.SampledAt - evidence.ParkedSampledAt;
            if (pendingSampleAge < TimeSpan.Zero || parkedSampleAge < TimeSpan.Zero)
                AddCondition(HealthStatus.Degraded, "metadata-clock-invalid");
            if (pendingSampleAge >= profile.EvidenceMaxAge || parkedSampleAge >= profile.EvidenceMaxAge)
                AddCondition(HealthStatus.Degraded, "metadata-stale");
            if (evidence.PendingCount >= profile.PendingUnhealthyThreshold)
                AddCondition(HealthStatus.Unhealthy, "pending-depth-unhealthy");
            else if (evidence.PendingCount >= profile.PendingDegradedThreshold)
                AddCondition(HealthStatus.Degraded, "pending-depth-degraded");
            if (evidence.ParkedCount > 0)
                AddCondition(HealthStatus.Degraded, "parked-messages");
        }

        if (relay.StartedAt is { } startedAt)
            data["relayStartedAt"] = startedAt;
        if (relay.LastDeliveryAt is { } deliveredAt)
            data["lastDurableDeliveryAt"] = deliveredAt;
        if (relay.LastPositiveAt is { } positiveAt)
            data["lastPositiveRelayObservationAt"] = positiveAt;
        if ((relay.LastPositiveAt ?? relay.StartedAt) is { } gapStart)
        {
            var gap = relay.SampledAt - gapStart;
            data["relayGap"] = gap;
            if (gap < TimeSpan.Zero)
                AddCondition(HealthStatus.Degraded, "relay-clock-invalid");
            else if (gap >= profile.RelayUnhealthyAfter)
                AddCondition(HealthStatus.Unhealthy, "relay-observation-overdue");
            else if (relay.LastPositiveAt is null)
                AddCondition(HealthStatus.Degraded,
                    gap < profile.StartupGracePeriod ? "relay-warming-up" : "relay-observation-missing");
            else if (gap >= profile.RelayDegradedAfter)
                AddCondition(HealthStatus.Degraded, "relay-observation-delayed");
        }
        else
            AddCondition(HealthStatus.Degraded, "relay-not-started");

        data["reasons"] = reasons.ToArray();
        return new(status, reasons.Count == 0 ? "Outbox readiness is healthy." : string.Join(", ", reasons), data: data);

        void AddCondition(HealthStatus severity, string reason)
        {
            if (severity < status)
                status = severity;
            reasons.Add(reason);
        }
    }

    private sealed record QueueEvidence(
        int MaxAttempts, int PendingCount, int ParkedCount,
        DateTimeOffset PendingSampledAt, DateTimeOffset ParkedSampledAt);

    private sealed record ProbeResult(
        int MaxAttempts, QueueEvidence? Evidence, string? FailureReason, string? FaultType);
}

internal static class OutboxReadinessLog
{
    private static readonly Action<ILogger, string, string, Exception?> s_probeFailed =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(1, "OutboxReadiness.ProbeFailed"),
            "Outbox readiness probe for {ContextType} failed with {FaultType}; exception details are redacted.");
    private static readonly Action<ILogger, string, Exception?> s_probeTimedOut =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, "OutboxReadiness.ProbeTimedOut"),
            "Outbox readiness probe for {ContextType} exceeded its budget.");

    public static void ProbeFailed(ILogger logger, string contextType, string faultType) =>
        s_probeFailed(logger, contextType, faultType, null);

    public static void ProbeTimedOut(ILogger logger, string contextType) => s_probeTimedOut(logger, contextType, null);
}