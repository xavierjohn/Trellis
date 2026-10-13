namespace Trellis.EntityFrameworkCore;

/// <summary>
/// An explicitly selected, immutable readiness profile for outbox metadata and optional local relay evidence.
/// </summary>
/// <remarks>
/// Select <see cref="CreateStagedOperationalProfile"/> or <see cref="CreateMetadataOnlyProfile"/>
/// and use a <c>with</c> expression to tune thresholds and timings, not the monitoring mode.
/// Capture age and consumer/source lag are not implemented by this profile.
/// </remarks>
public sealed record OutboxReadinessOptions
{
    private OutboxReadinessOptions(OutboxRelayMonitoring relayMonitoring, int? workerMaxAttempts = null)
    {
        RelayMonitoring = relayMonitoring;
        WorkerMaxAttempts = workerMaxAttempts;
    }

    /// <summary>Whether this profile evaluates in-process relay observations.</summary>
    public OutboxRelayMonitoring RelayMonitoring { get; }

    /// <summary>
    /// The worker's retry limit, explicitly supplied in metadata-only mode because its options
    /// cannot be read from another process. Must equal the worker's <see cref="OutboxOptions.MaxAttempts"/>.
    /// Null and unused in local-relay mode, which reads the local outbox options instead.
    /// </summary>
    public int? WorkerMaxAttempts { get; }

    /// <summary>The inclusive pending-row threshold for Degraded readiness.</summary>
    public int PendingDegradedThreshold { get; init; } = 1_000;

    /// <summary>The inclusive pending-row threshold for Unhealthy readiness and the pending-count cap.</summary>
    public int PendingUnhealthyThreshold { get; init; } = 10_000;

    /// <summary>The inclusive gap since a positive local relay observation that degrades readiness.</summary>
    public TimeSpan RelayDegradedAfter { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The inclusive local relay gap that makes readiness unhealthy.</summary>
    public TimeSpan RelayUnhealthyAfter { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long missing initial relay evidence has a warming-up reason; its status remains Degraded.</summary>
    public TimeSpan StartupGracePeriod { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The complete database/provider probe budget, shared by all queries. Default: 2 seconds.</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to reuse a completed metadata probe. Default: 10 seconds.</summary>
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The inclusive age at which enabled metadata evidence becomes stale. Default: 30 seconds.</summary>
    public TimeSpan EvidenceMaxAge { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Selects the staged operational reference profile. No relay registration selects it automatically.
    /// </summary>
    /// <returns>A new immutable profile that can be tuned with a <c>with</c> expression.</returns>
    public static OutboxReadinessOptions CreateStagedOperationalProfile() => new(OutboxRelayMonitoring.LocalRelay);

    /// <summary>
    /// Selects bounded metadata readiness without observing a relay in this process or a remote worker.
    /// Incidental local outbox options and observations are not resolved by the check.
    /// </summary>
    /// <param name="workerMaxAttempts">
    /// The positive retry limit used by the worker, matching its <see cref="OutboxOptions.MaxAttempts"/>.
    /// Derive both values from the same configuration key or constant.
    /// </param>
    /// <returns>A new immutable metadata-only profile with the explicit worker retry limit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The worker retry limit is not positive.</exception>
    public static OutboxReadinessOptions CreateMetadataOnlyProfile(int workerMaxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerMaxAttempts);
        return new(OutboxRelayMonitoring.Disabled, workerMaxAttempts);
    }

    internal void Validate()
    {
        if (RelayMonitoring is not (OutboxRelayMonitoring.LocalRelay or OutboxRelayMonitoring.Disabled))
            throw new ArgumentOutOfRangeException(nameof(RelayMonitoring));
        if ((RelayMonitoring == OutboxRelayMonitoring.LocalRelay && WorkerMaxAttempts is not null)
            || (RelayMonitoring == OutboxRelayMonitoring.Disabled && WorkerMaxAttempts is not > 0))
            throw new ArgumentOutOfRangeException(nameof(WorkerMaxAttempts),
                "WorkerMaxAttempts must be positive in metadata-only mode and null in local-relay mode.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PendingDegradedThreshold);
        if (PendingUnhealthyThreshold <= PendingDegradedThreshold)
            throw new ArgumentOutOfRangeException(nameof(PendingUnhealthyThreshold),
                "The unhealthy pending threshold must exceed the degraded threshold.");
        if (RelayDegradedAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RelayDegradedAfter));
        if (RelayUnhealthyAfter <= RelayDegradedAfter)
            throw new ArgumentOutOfRangeException(nameof(RelayUnhealthyAfter),
                "The unhealthy relay gap must exceed the degraded gap.");
        if (StartupGracePeriod < TimeSpan.Zero || StartupGracePeriod >= RelayDegradedAfter)
            throw new ArgumentOutOfRangeException(nameof(StartupGracePeriod),
                "Startup grace must be nonnegative and shorter than the degraded relay gap.");
        if (ProbeTimeout <= TimeSpan.Zero || ProbeTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(ProbeTimeout));
        if (CacheDuration <= TimeSpan.Zero || CacheDuration < ProbeTimeout)
            throw new ArgumentOutOfRangeException(nameof(CacheDuration),
                "The cache duration must be positive and at least the probe timeout.");
        if (EvidenceMaxAge <= CacheDuration || EvidenceMaxAge - CacheDuration <= ProbeTimeout)
            throw new ArgumentOutOfRangeException(nameof(EvidenceMaxAge),
                "Evidence freshness must exceed the cache duration plus the probe timeout.");
    }
}