namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

internal sealed class OutboxReadinessHealthCheck<TContext>(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    OutboxOptions? outboxOptions,
    OutboxReadinessOptions profile,
    OutboxRelayObservations<TContext>? observations,
    ILogger<OutboxReadinessHealthCheck<TContext>> logger) : IHealthCheck
    where TContext : DbContext
{
    private readonly Lock _gate = new();
    private Task<ProbeResult>? _refresh;
    private Task<ProbeResult>? _databaseProbe;
    private ProbeResult? _cached;
    private QueueEvidence? _lastEvidence;
    private ConsumerLagEvidence? _lastConsumerLagEvidence;
    private long _cachedAt;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var maxAttempts = profile.RelayMonitoring switch
        {
            OutboxRelayMonitoring.LocalRelay => outboxOptions?.MaxAttempts
                ?? throw new InvalidOperationException("Local outbox readiness requires OutboxOptions."),
            OutboxRelayMonitoring.Disabled => profile.WorkerMaxAttempts
                ?? throw new InvalidOperationException("Metadata-only readiness requires WorkerMaxAttempts."),
            _ => throw new InvalidOperationException("The outbox relay monitoring mode is invalid."),
        };
        if (maxAttempts <= 0)
            throw new InvalidOperationException("The effective outbox retry limit must be greater than zero.");

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
        var relay = profile.RelayMonitoring == OutboxRelayMonitoring.LocalRelay
            ? observations?.Read() ?? throw new InvalidOperationException("Local outbox readiness requires relay observations.")
            : null;
        return Evaluate(probe, relay);
    }

    private async Task<ProbeResult> RefreshAsync(int maxAttempts)
    {
        ProbeResult result;
        if (_databaseProbe is { IsCompleted: false })
            result = new(maxAttempts, null, false, "probe-timeout", null, null, null, null);
        else
        {
            var started = timeProvider.GetTimestamp();
            var deadline = new CancellationTokenSource(profile.ProbeTimeout, timeProvider);
            var progress = new ProbeProgress();
            // A probe has the relay's background scope, not the initiating request's ambient tenancy.
            using (ExecutionContext.SuppressFlow())
                _databaseProbe = Task.Run(() => ReadMetadataAsync(maxAttempts, deadline, progress));
            var remaining = profile.ProbeTimeout - timeProvider.GetElapsedTime(started);
            try
            {
                result = await _databaseProbe.WaitAsync(
                    remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                    timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                var evidence = Volatile.Read(ref progress.Evidence);
                result = new(maxAttempts, evidence, evidence is not null,
                    "probe-timeout", null, null, null, null);
            }
        }

        if (result.FailureReason == "probe-timeout")
            OutboxReadinessLog.ProbeTimedOut(logger, typeof(TContext).Name);
        lock (_gate)
        {
            if (result is { MetadataAvailable: true, Evidence: not null })
                _lastEvidence = result.Evidence;
            else if (_lastEvidence?.MaxAttempts == maxAttempts)
                result = result with { Evidence = _lastEvidence };
            if (result.ConsumerLagEvidence is not null)
                _lastConsumerLagEvidence = result.ConsumerLagEvidence;
            else if (profile.ConsumerLagMeasures != ConsumerLagMeasures.None
                && _lastConsumerLagEvidence is not null)
                result = result with { ConsumerLagEvidence = _lastConsumerLagEvidence };
            _cached = result;
            _cachedAt = timeProvider.GetTimestamp();
        }

        return result;
    }

    private async Task<ProbeResult> ReadMetadataAsync(
        int maxAttempts, CancellationTokenSource deadline, ProbeProgress progress)
    {
        using (deadline)
        {
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                QueueEvidence evidence;
                ConsumerLagEvidence? consumerLagEvidence = null;
                string? consumerLagFailureReason = null;
                string? consumerLagFaultType = null;
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
                    DateTime? oldestCapturedAt = null;
                    var unknownCaptureAgeCount = 0;
                    DateTimeOffset? captureAgeSampledAt = null;
                    if (profile.CaptureAgeEnabled)
                    {
                        oldestCapturedAt = await rows
                            .Where(m => m.ProcessedAt == null
                                && m.Attempts < maxAttempts
                                && m.CapturedAt != null)
                            .OrderBy(m => m.CapturedAt)
                            .ThenBy(m => m.Sequence)
                            .Select(m => m.CapturedAt)
                            .FirstOrDefaultAsync(deadline.Token).ConfigureAwait(false);
                        unknownCaptureAgeCount = await rows
                            .Where(m => m.ProcessedAt == null
                                && m.Attempts < maxAttempts
                                && m.CapturedAt == null)
                            .OrderBy(m => m.Sequence)
                            .Select(m => m.Sequence)
                            .Take(profile.PendingUnhealthyThreshold)
                            .CountAsync(deadline.Token).ConfigureAwait(false);
                        captureAgeSampledAt = timeProvider.GetUtcNow();
                    }

                    var parked = await rows
                        .AnyAsync(m => m.ProcessedAt == null && m.Attempts >= maxAttempts, deadline.Token)
                        .ConfigureAwait(false);
                    evidence = new(
                        maxAttempts,
                        pending,
                        parked ? 1 : 0,
                        pendingSampledAt,
                        timeProvider.GetUtcNow(),
                        oldestCapturedAt,
                        unknownCaptureAgeCount,
                        captureAgeSampledAt);
                    Volatile.Write(ref progress.Evidence, evidence);

                    if (profile.ConsumerLagMeasures != ConsumerLagMeasures.None)
                    {
                        try
                        {
                            consumerLagEvidence = await scope.ServiceProvider
                                .GetRequiredService<IConsumerLagProvider>()
                                .GetLagAsync(deadline.Token)
                                .ConfigureAwait(false);
                            if (consumerLagEvidence is null)
                            {
                                consumerLagFailureReason = "consumer-lag-provider-failed";
                                consumerLagFaultType = "NullEvidence";
                                OutboxReadinessLog.ConsumerLagProbeFailed(
                                    logger, typeof(TContext).Name, consumerLagFaultType);
                            }
                        }
                        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            consumerLagFailureReason = "consumer-lag-provider-failed";
                            consumerLagFaultType = exception.GetType().Name;
                            OutboxReadinessLog.ConsumerLagProbeFailed(
                                logger, typeof(TContext).Name, consumerLagFaultType);
                        }
                    }
                }

                return deadline.IsCancellationRequested
                    ? new(maxAttempts, evidence, true, "probe-timeout", null, null, null, null)
                    : new(maxAttempts, evidence, true, null, null, consumerLagEvidence,
                        consumerLagFailureReason, consumerLagFaultType);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                var evidence = Volatile.Read(ref progress.Evidence);
                return new(maxAttempts, evidence, evidence is not null,
                    "probe-timeout", null, null, null, null);
            }
            catch (Exception exception)
            {
                var faultType = exception.GetType().Name;
                OutboxReadinessLog.ProbeFailed(logger, typeof(TContext).Name, faultType);
                return new(maxAttempts, null, false, "probe-failed", faultType, null, null, null);
            }
        }
    }

    private HealthCheckResult Evaluate(ProbeResult probe, OutboxRelayEvidence? relay)
    {
        var evaluatedAt = relay?.SampledAt ?? timeProvider.GetUtcNow();
        var status = HealthStatus.Healthy;
        var reasons = new List<string>();
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["profile"] = relay is null ? "metadata-only-reference" : "staged-operational-reference",
            ["storageContext"] = typeof(TContext).Name,
            ["maxAttempts"] = probe.MaxAttempts,
            ["maxAttemptsSource"] = relay is null ? "profile" : "outbox-options",
            ["relayMonitoring"] = relay is null ? "disabled" : "local-relay",
            ["metadataAvailable"] = probe.MetadataAvailable,
            ["pendingCountCap"] = profile.PendingUnhealthyThreshold,
            ["parkedCountCap"] = 1,
            ["pendingDegradedThreshold"] = profile.PendingDegradedThreshold,
            ["pendingUnhealthyThreshold"] = profile.PendingUnhealthyThreshold,
            ["probeTimeout"] = profile.ProbeTimeout,
            ["cacheDuration"] = profile.CacheDuration,
            ["evidenceMaxAge"] = profile.EvidenceMaxAge,
            ["captureAge"] = profile.CaptureAgeEnabled ? "enabled" : "disabled",
            ["consumerLag"] = profile.ConsumerLagMeasures == ConsumerLagMeasures.None ? "disabled" : "enabled",
            ["sampling"] = SamplingDescription(relay is not null),
        };
        if (probe.FailureReason is not null)
            AddCondition(HealthStatus.Unhealthy, probe.FailureReason);
        if (probe.FaultType is not null)
            data["probeFaultType"] = probe.FaultType;
        if (profile.ConsumerLagMeasures != ConsumerLagMeasures.None)
        {
            data["consumerLagAvailable"] = probe.FailureReason is null
                && probe.ConsumerLagFailureReason is null
                && probe.ConsumerLagEvidence is not null;
            data["consumerLagMeasures"] = MeasureNames(profile.ConsumerLagMeasures);
            if (profile.ConsumerLagMeasures.HasFlag(ConsumerLagMeasures.Age))
            {
                data["consumerLagAgeDegradedAfter"] = profile.ConsumerLagAgeDegradedAfter;
                data["consumerLagAgeUnhealthyAfter"] = profile.ConsumerLagAgeUnhealthyAfter;
            }

            if (profile.ConsumerLagMeasures.HasFlag(ConsumerLagMeasures.Count))
            {
                data["consumerLagCountDegradedThreshold"] = profile.ConsumerLagCountDegradedThreshold;
                data["consumerLagCountUnhealthyThreshold"] = profile.ConsumerLagCountUnhealthyThreshold;
            }

            if (probe.ConsumerLagFailureReason is not null)
                AddCondition(HealthStatus.Unhealthy, probe.ConsumerLagFailureReason);
            if (probe.ConsumerLagFaultType is not null)
                data["consumerLagFaultType"] = probe.ConsumerLagFaultType;
            if (probe.ConsumerLagEvidence is { } consumerLag)
                EvaluateConsumerLag(consumerLag);
        }

        if (probe.Evidence is { } evidence)
        {
            data["pendingCount"] = evidence.PendingCount;
            data["pendingCountIsLowerBound"] = evidence.PendingCount == profile.PendingUnhealthyThreshold;
            data["parkedCount"] = evidence.ParkedCount;
            data["parkedCountIsLowerBound"] = evidence.ParkedCount > 0;
            data["pendingSampledAt"] = evidence.PendingSampledAt;
            data["parkedSampledAt"] = evidence.ParkedSampledAt;
            var pendingSampleAge = evaluatedAt - evidence.PendingSampledAt;
            var parkedSampleAge = evaluatedAt - evidence.ParkedSampledAt;
            var captureAgeSampleAge = evidence.CaptureAgeSampledAt is { } captureAgeSampledAt
                ? evaluatedAt - captureAgeSampledAt
                : (TimeSpan?)null;
            if (pendingSampleAge < TimeSpan.Zero
                || parkedSampleAge < TimeSpan.Zero
                || captureAgeSampleAge < TimeSpan.Zero)
                AddCondition(HealthStatus.Degraded, "metadata-clock-invalid");
            if (pendingSampleAge >= profile.EvidenceMaxAge
                || parkedSampleAge >= profile.EvidenceMaxAge
                || captureAgeSampleAge >= profile.EvidenceMaxAge)
                AddCondition(HealthStatus.Degraded, "metadata-stale");
            if (evidence.PendingCount >= profile.PendingUnhealthyThreshold)
                AddCondition(HealthStatus.Unhealthy, "pending-depth-unhealthy");
            else if (evidence.PendingCount >= profile.PendingDegradedThreshold)
                AddCondition(HealthStatus.Degraded, "pending-depth-degraded");
            if (evidence.ParkedCount > 0)
                AddCondition(HealthStatus.Degraded, "parked-messages");
            if (profile.CaptureAgeEnabled)
            {
                data["captureAgeDegradedAfter"] = profile.CaptureAgeDegradedAfter;
                data["captureAgeUnhealthyAfter"] = profile.CaptureAgeUnhealthyAfter;
                data["unknownCaptureAgeCountCap"] = profile.PendingUnhealthyThreshold;
                data["unknownCaptureAgeCount"] = evidence.UnknownCaptureAgeCount;
                data["unknownCaptureAgeCountIsLowerBound"] =
                    evidence.UnknownCaptureAgeCount == profile.PendingUnhealthyThreshold;
                if (evidence.CaptureAgeSampledAt is { } sampledAt)
                    data["captureAgeSampledAt"] = sampledAt;
                if (evidence.OldestCapturedAt is { } storedCapturedAt)
                {
                    var capturedAt = new DateTimeOffset(
                        DateTime.SpecifyKind(storedCapturedAt, DateTimeKind.Utc));
                    var captureAge = evaluatedAt - capturedAt;
                    data["oldestPendingCapturedAt"] = capturedAt;
                    data["oldestPendingCaptureAge"] = captureAge;
                    if (captureAge < TimeSpan.Zero)
                        AddCondition(HealthStatus.Degraded, "capture-age-clock-invalid");
                    else if (captureAge >= profile.CaptureAgeUnhealthyAfter)
                        AddCondition(HealthStatus.Unhealthy, "capture-age-unhealthy");
                    else if (captureAge >= profile.CaptureAgeDegradedAfter)
                        AddCondition(HealthStatus.Degraded, "capture-age-degraded");
                }

                if (evidence.UnknownCaptureAgeCount > 0)
                    AddCondition(HealthStatus.Degraded, "capture-age-unknown");
            }
        }

        if (relay is not null)
        {
            data["relayDegradedAfter"] = profile.RelayDegradedAfter;
            data["relayUnhealthyAfter"] = profile.RelayUnhealthyAfter;
            data["startupGracePeriod"] = profile.StartupGracePeriod;
            data["relayScope"] = "local-instance";
            data["relaySampledAt"] = relay.SampledAt;
            data["deliveredRows"] = relay.DeliveredRows;
            if (relay.StartedAt is { } startedAt)
                data["relayStartedAt"] = startedAt;
            if (relay.LastDeliveryAt is { } deliveredAt)
                data["lastDurableDeliveryAt"] = deliveredAt;
            if (relay.LastPositiveAt is { } positiveAt)
                data["lastPositiveRelayObservationAt"] = positiveAt;
            if ((relay.LastPositiveAt ?? relay.StartedAt) is { } gapStart)
            {
                var gap = evaluatedAt - gapStart;
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
        }

        data["reasons"] = reasons.ToArray();
        var healthyDescription = relay is null && profile.ConsumerLagMeasures == ConsumerLagMeasures.None
            ? "Outbox metadata is healthy."
            : "Outbox readiness is healthy.";
        return new(status, reasons.Count == 0 ? healthyDescription : string.Join(", ", reasons), data: data);

        void EvaluateConsumerLag(ConsumerLagEvidence evidence)
        {
            data["consumerLagEvidenceMeasures"] = MeasureNames(evidence.Measures);
            data["consumerLagConsumerId"] = evidence.ConsumerId;
            data["consumerLagSource"] = evidence.Source;
            data["consumerLagScope"] = evidence.Scope;
            data["consumerLagSampledAt"] = evidence.SampledAt;
            data["consumerLagRequiredPartitionCount"] = evidence.RequiredPartitionCount;
            data["consumerLagCoveredPartitionCount"] = evidence.CoveredPartitionCount;

            if ((evidence.Measures & profile.ConsumerLagMeasures) != profile.ConsumerLagMeasures)
                AddCondition(HealthStatus.Degraded, "consumer-lag-evidence-missing");
            if (evidence.CoveredPartitionCount < evidence.RequiredPartitionCount)
                AddCondition(HealthStatus.Degraded, "consumer-lag-coverage-missing");

            var sampleAge = evaluatedAt - evidence.SampledAt;
            if (sampleAge < TimeSpan.Zero)
                AddCondition(HealthStatus.Degraded, "consumer-lag-clock-invalid");
            else if (sampleAge >= profile.EvidenceMaxAge)
                AddCondition(HealthStatus.Degraded, "consumer-lag-stale");

            if (profile.ConsumerLagMeasures.HasFlag(ConsumerLagMeasures.Age)
                && evidence.Measures.HasFlag(ConsumerLagMeasures.Age)
                && evidence.OldestUnaccountedAt is { } oldestUnaccountedAt)
            {
                var age = evaluatedAt - oldestUnaccountedAt;
                data["consumerLagOldestUnaccountedAt"] = oldestUnaccountedAt;
                data["consumerLagOldestUnaccountedAge"] = age;
                if (age < TimeSpan.Zero)
                    AddCondition(HealthStatus.Degraded, "consumer-lag-clock-invalid");
                else if (age >= profile.ConsumerLagAgeUnhealthyAfter)
                    AddCondition(HealthStatus.Unhealthy, "consumer-lag-age-unhealthy");
                else if (age >= profile.ConsumerLagAgeDegradedAfter)
                    AddCondition(HealthStatus.Degraded, "consumer-lag-age-degraded");
            }

            if (profile.ConsumerLagMeasures.HasFlag(ConsumerLagMeasures.Count)
                && evidence.Measures.HasFlag(ConsumerLagMeasures.Count)
                && evidence.UnaccountedCount is { } unaccountedCount)
            {
                data["consumerLagUnaccountedCount"] = unaccountedCount;
                data["consumerLagUnaccountedCountIsLowerBound"] = evidence.UnaccountedCountIsLowerBound;
                if (unaccountedCount >= profile.ConsumerLagCountUnhealthyThreshold)
                    AddCondition(HealthStatus.Unhealthy, "consumer-lag-count-unhealthy");
                else if (unaccountedCount >= profile.ConsumerLagCountDegradedThreshold)
                    AddCondition(HealthStatus.Degraded, "consumer-lag-count-degraded");
                else if (evidence.UnaccountedCountIsLowerBound)
                    AddCondition(HealthStatus.Degraded, "consumer-lag-count-lower-bound");
            }
        }

        string SamplingDescription(bool hasRelay)
        {
            var sampling = profile.CaptureAgeEnabled
                ? "separate-metadata-and-capture-age-queries"
                : "separate-metadata-queries";
            if (profile.ConsumerLagMeasures != ConsumerLagMeasures.None)
                sampling += "-and-consumer-lag";
            if (hasRelay)
                sampling += "-and-local-relay";
            return sampling;
        }

        void AddCondition(HealthStatus severity, string reason)
        {
            if (severity < status)
                status = severity;
            if (!reasons.Contains(reason))
                reasons.Add(reason);
        }
    }

    private sealed record QueueEvidence(
        int MaxAttempts, int PendingCount, int ParkedCount,
        DateTimeOffset PendingSampledAt, DateTimeOffset ParkedSampledAt,
        DateTime? OldestCapturedAt, int UnknownCaptureAgeCount, DateTimeOffset? CaptureAgeSampledAt);

    private sealed class ProbeProgress
    {
        public QueueEvidence? Evidence;
    }

    private sealed record ProbeResult(
        int MaxAttempts,
        QueueEvidence? Evidence,
        bool MetadataAvailable,
        string? FailureReason,
        string? FaultType,
        ConsumerLagEvidence? ConsumerLagEvidence,
        string? ConsumerLagFailureReason,
        string? ConsumerLagFaultType);

    private static string[] MeasureNames(ConsumerLagMeasures measures) =>
        measures == (ConsumerLagMeasures.Age | ConsumerLagMeasures.Count)
            ? ["age", "count"]
            : measures == ConsumerLagMeasures.Age ? ["age"] : ["count"];
}

internal static class OutboxReadinessLog
{
    private static readonly Action<ILogger, string, string, Exception?> s_probeFailed =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(1, "OutboxReadiness.ProbeFailed"),
            "Outbox readiness probe for {ContextType} failed with {FaultType}; exception details are redacted.");
    private static readonly Action<ILogger, string, Exception?> s_probeTimedOut =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, "OutboxReadiness.ProbeTimedOut"),
            "Outbox readiness probe for {ContextType} exceeded its budget.");
    private static readonly Action<ILogger, string, string, Exception?> s_consumerLagProbeFailed =
        LoggerMessage.Define<string, string>(LogLevel.Error,
            new EventId(3, "OutboxReadiness.ConsumerLagProbeFailed"),
            "Consumer lag probe for {ContextType} failed with {FaultType}; exception details are redacted.");

    public static void ProbeFailed(ILogger logger, string contextType, string faultType) =>
        s_probeFailed(logger, contextType, faultType, null);

    public static void ProbeTimedOut(ILogger logger, string contextType) => s_probeTimedOut(logger, contextType, null);

    public static void ConsumerLagProbeFailed(ILogger logger, string contextType, string faultType) =>
        s_consumerLagProbeFailed(logger, contextType, faultType, null);

}