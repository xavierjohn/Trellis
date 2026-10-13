namespace Trellis.EntityFrameworkCore;

/// <summary>
/// An explicitly selected, immutable readiness profile for outbox metadata and local relay evidence.
/// </summary>
/// <remarks>
/// Select <see cref="CreateStagedOperationalProfile"/> and use a <c>with</c> expression to tune it.
/// Capture age and consumer/source lag are not implemented by this profile.
/// </remarks>
public sealed record OutboxReadinessOptions
{
    private OutboxReadinessOptions() { }

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
    public static OutboxReadinessOptions CreateStagedOperationalProfile() => new();

    internal void Validate()
    {
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
        if (EvidenceMaxAge <= CacheDuration)
            throw new ArgumentOutOfRangeException(nameof(EvidenceMaxAge),
                "Evidence freshness must exceed the cache duration.");
    }
}