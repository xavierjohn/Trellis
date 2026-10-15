namespace Trellis.EntityFrameworkCore;

/// <summary>
/// One conservative, application-aggregated source/consumer lag snapshot.
/// </summary>
/// <remarks>
/// Across required partitions, use the oldest unaccounted source timestamp, sum counts without
/// averaging, retain lower-bound status, and use the oldest underlying sample time. Missing
/// partitions remain visible through <see cref="CoveredPartitionCount"/>.
/// </remarks>
public sealed record ConsumerLagEvidence
{
    /// <summary>Creates validated consumer-lag evidence.</summary>
    /// <param name="consumerId">The stable consumer or subscription identity.</param>
    /// <param name="source">The application-defined source/feed identity.</param>
    /// <param name="scope">The authorized storage/subscription scope.</param>
    /// <param name="sampledAt">The original UTC source sample time, not cache refresh time.</param>
    /// <param name="measures">Measurements authoritatively supplied by this snapshot.</param>
    /// <param name="requiredPartitionCount">Number of partitions required for complete coverage.</param>
    /// <param name="coveredPartitionCount">Number of required partitions covered by this snapshot.</param>
    /// <param name="oldestUnaccountedAt">
    /// Oldest durably unaccounted source timestamp, or null when age evidence authoritatively
    /// reports no unaccounted work.
    /// </param>
    /// <param name="unaccountedCount">Conservative aggregate count when <paramref name="measures"/> includes Count.</param>
    /// <param name="unaccountedCountIsLowerBound">
    /// Whether the count is a lower bound rather than exact. A lower bound below the configured degraded
    /// threshold still degrades readiness because it cannot prove the true count is healthy.
    /// </param>
    public ConsumerLagEvidence(
        string consumerId,
        string source,
        string scope,
        DateTimeOffset sampledAt,
        ConsumerLagMeasures measures,
        int requiredPartitionCount,
        int coveredPartitionCount,
        DateTimeOffset? oldestUnaccountedAt,
        long? unaccountedCount,
        bool unaccountedCountIsLowerBound = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (sampledAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("The source sample time must use the UTC offset.", nameof(sampledAt));
        if (oldestUnaccountedAt is { Offset: var offset } && offset != TimeSpan.Zero)
            throw new ArgumentException("The oldest unaccounted time must use the UTC offset.",
                nameof(oldestUnaccountedAt));
        if (measures == ConsumerLagMeasures.None || (int)measures is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(measures));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredPartitionCount);
        if (coveredPartitionCount < 0 || coveredPartitionCount > requiredPartitionCount)
            throw new ArgumentOutOfRangeException(nameof(coveredPartitionCount));
        if (!measures.HasFlag(ConsumerLagMeasures.Age) && oldestUnaccountedAt is not null)
            throw new ArgumentException("Oldest-unaccounted evidence requires the Age measure.",
                nameof(oldestUnaccountedAt));
        if (!measures.HasFlag(ConsumerLagMeasures.Count) && unaccountedCount is not null)
            throw new ArgumentException("Unaccounted-count evidence requires the Count measure.",
                nameof(unaccountedCount));
        if (unaccountedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(unaccountedCount));
        if (measures.HasFlag(ConsumerLagMeasures.Count) && unaccountedCount is null)
            throw new ArgumentException("Count evidence requires an unaccounted count.", nameof(unaccountedCount));
        if (unaccountedCountIsLowerBound && unaccountedCount is null)
            throw new ArgumentException("A lower-bound marker requires an unaccounted count.",
                nameof(unaccountedCountIsLowerBound));

        ConsumerId = consumerId;
        Source = source;
        Scope = scope;
        SampledAt = sampledAt;
        Measures = measures;
        RequiredPartitionCount = requiredPartitionCount;
        CoveredPartitionCount = coveredPartitionCount;
        OldestUnaccountedAt = oldestUnaccountedAt;
        UnaccountedCount = unaccountedCount;
        UnaccountedCountIsLowerBound = unaccountedCountIsLowerBound;
    }

    /// <summary>The stable consumer or subscription identity.</summary>
    public string ConsumerId { get; }

    /// <summary>The application-defined source/feed identity.</summary>
    public string Source { get; }

    /// <summary>The authorized storage/subscription scope.</summary>
    public string Scope { get; }

    /// <summary>The original source sample time.</summary>
    public DateTimeOffset SampledAt { get; }

    /// <summary>Measurements authoritatively supplied by this snapshot.</summary>
    public ConsumerLagMeasures Measures { get; }

    /// <summary>Number of partitions required for complete coverage.</summary>
    public int RequiredPartitionCount { get; }

    /// <summary>Number of required partitions covered by this snapshot.</summary>
    public int CoveredPartitionCount { get; }

    /// <summary>Oldest durably unaccounted source timestamp, or null when none exists.</summary>
    public DateTimeOffset? OldestUnaccountedAt { get; }

    /// <summary>Conservative aggregate unaccounted count, when supplied.</summary>
    public long? UnaccountedCount { get; }

    /// <summary>
    /// Whether <see cref="UnaccountedCount"/> is a lower bound rather than exact. A lower bound below the
    /// configured degraded threshold still degrades readiness because it cannot prove the true count is healthy.
    /// </summary>
    public bool UnaccountedCountIsLowerBound { get; }
}