namespace Trellis.EntityFrameworkCore;

/// <summary>
/// An explicitly selected, immutable readiness profile for outbox metadata, optional durable capture age,
/// optional local relay evidence, and optional application-provided consumer lag.
/// </summary>
/// <remarks>
/// Select <see cref="CreateStagedOperationalProfile"/> or <see cref="CreateMetadataOnlyProfile"/>
/// and use a <c>with</c> expression to enable capture age and tune thresholds and timings,
/// not the relay monitoring mode. Consumer/source lag remains disabled until
/// <see cref="ConsumerLagMeasures"/> selects at least one measure and an
/// <see cref="IConsumerLagProvider"/> is registered.
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

    /// <summary>
    /// Whether to evaluate durable row capture age. Defaults to <see langword="false"/> so applications
    /// can deploy the nullable capture column and writers before enabling the age SLO.
    /// </summary>
    public bool CaptureAgeEnabled { get; init; }

    /// <summary>The inclusive oldest-known capture age that degrades readiness. Default: 2 minutes.</summary>
    public TimeSpan CaptureAgeDegradedAfter { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The inclusive oldest-known capture age that makes readiness unhealthy. Default: 10 minutes.</summary>
    public TimeSpan CaptureAgeUnhealthyAfter { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Consumer/source lag measures to evaluate. Defaults to
    /// <see cref="Trellis.EntityFrameworkCore.ConsumerLagMeasures.None"/>.
    /// </summary>
    public ConsumerLagMeasures ConsumerLagMeasures { get; init; }

    /// <summary>The inclusive oldest-unaccounted age that degrades readiness. Default: 2 minutes.</summary>
    public TimeSpan ConsumerLagAgeDegradedAfter { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The inclusive oldest-unaccounted age that makes readiness unhealthy. Default: 10 minutes.</summary>
    public TimeSpan ConsumerLagAgeUnhealthyAfter { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The inclusive unaccounted-event count that degrades readiness. Default: 1,000.
    /// A lower-bound count below this threshold also degrades because it cannot prove a healthy upper bound.
    /// </summary>
    public long ConsumerLagCountDegradedThreshold { get; init; } = 1_000;

    /// <summary>The inclusive unaccounted-event count that makes readiness unhealthy. Default: 10,000.</summary>
    public long ConsumerLagCountUnhealthyThreshold { get; init; } = 10_000;

    /// <summary>The inclusive gap since a positive local relay observation that degrades readiness.</summary>
    public TimeSpan RelayDegradedAfter { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The inclusive local relay gap that makes readiness unhealthy.</summary>
    public TimeSpan RelayUnhealthyAfter { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long missing initial relay evidence has a warming-up reason; its status remains Degraded.</summary>
    public TimeSpan StartupGracePeriod { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The complete database/provider probe budget, shared by every enabled query. Default: 2 seconds.</summary>
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
        if (CaptureAgeDegradedAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CaptureAgeDegradedAfter));
        if (CaptureAgeUnhealthyAfter <= CaptureAgeDegradedAfter)
            throw new ArgumentOutOfRangeException(nameof(CaptureAgeUnhealthyAfter),
                "The unhealthy capture age must exceed the degraded capture age.");
        if ((int)ConsumerLagMeasures is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(ConsumerLagMeasures));
        if (ConsumerLagAgeDegradedAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ConsumerLagAgeDegradedAfter));
        if (ConsumerLagAgeUnhealthyAfter <= ConsumerLagAgeDegradedAfter)
            throw new ArgumentOutOfRangeException(nameof(ConsumerLagAgeUnhealthyAfter),
                "The unhealthy consumer lag age must exceed the degraded age.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ConsumerLagCountDegradedThreshold);
        if (ConsumerLagCountUnhealthyThreshold <= ConsumerLagCountDegradedThreshold)
            throw new ArgumentOutOfRangeException(nameof(ConsumerLagCountUnhealthyThreshold),
                "The unhealthy consumer lag count threshold must exceed the degraded threshold.");
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