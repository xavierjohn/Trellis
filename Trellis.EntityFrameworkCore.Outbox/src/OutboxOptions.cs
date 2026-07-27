namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Tuning for the transactional outbox relay (<c>OutboxRelay{TContext}</c>).
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>How long the relay waits before polling again when the outbox is empty. Default: 5 seconds. Must be greater than zero.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum messages drained per poll. Default: 100. Must be greater than zero.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum relay attempts before a failing message is parked (left unprocessed but skipped by the
    /// scan so it does not block later messages). Default: 10. Must be greater than zero.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// How long a relay drain holds an exclusive claim (lease) on the rows it drains before another
    /// instance may reclaim them. Set it comfortably above the time to publish one batch so a slow batch
    /// is not reclaimed mid-flight; a crashed instance's rows become reclaimable once the lease expires.
    /// Default: 5 minutes. Must be greater than zero.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The base delay before a failed message is retried. The relay backs off exponentially from this
    /// base — the wait after the <c>n</c>th failed attempt is <c>RetryBackoff × 2^(n-1)</c>, capped at
    /// <see cref="MaxRetryBackoff"/> — so a transient failure is retried with growing spacing instead of
    /// in a tight loop that would hammer the database. Default: 30 seconds. Must be greater than zero.
    /// </summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The ceiling on the exponential retry backoff: the per-retry wait grows from
    /// <see cref="RetryBackoff"/> but never exceeds this, so a persistently failing message keeps being
    /// retried at a steady, bounded cadence (rather than spacing out into many hours) until it reaches
    /// <see cref="MaxAttempts"/>. Default: 1 hour. Must be greater than or equal to
    /// <see cref="RetryBackoff"/>.
    /// </summary>
    public TimeSpan MaxRetryBackoff { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How much of the computed exponential backoff is spread by a deterministic, per-message jitter, as
    /// a fraction in <c>[0, 1]</c>. The jitter only ever <i>subtracts</i> from the delay — the actual wait
    /// is <c>computed × (1 - RetryBackoffJitter × f(Id))</c> for a stable <c>f(Id) ∈ [0, 1)</c> — so it
    /// never exceeds <see cref="MaxRetryBackoff"/> while de-correlating messages that failed together, so
    /// they do not all retry the instant a failed dependency recovers. <c>0</c> disables jitter (every
    /// message uses the exact computed delay). Default: 0.5. Must be between 0 and 1 inclusive.
    /// </summary>
    public double RetryBackoffJitter { get; set; } = 0.5;

    /// <summary>
    /// Creates an independent copy so a repeated registration can apply its <c>configure</c> callback
    /// and validate the result before the new state is committed to the container. Keep in sync with
    /// the properties above; <c>OutboxOptionsCloneTests</c> fails if a property is added and not copied.
    /// </summary>
    internal OutboxOptions Clone() => new()
    {
        PollInterval = PollInterval,
        BatchSize = BatchSize,
        MaxAttempts = MaxAttempts,
        LeaseDuration = LeaseDuration,
        RetryBackoff = RetryBackoff,
        MaxRetryBackoff = MaxRetryBackoff,
        RetryBackoffJitter = RetryBackoffJitter,
    };

    /// <summary>
    /// Validates the configured values, failing fast at registration so misconfiguration surfaces there
    /// rather than as a runtime exception inside the relay loop.
    /// </summary>
    internal void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "OutboxOptions.PollInterval must be greater than zero.");
        if (BatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "OutboxOptions.BatchSize must be greater than zero.");
        if (MaxAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "OutboxOptions.MaxAttempts must be greater than zero.");
        if (LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration), LeaseDuration, "OutboxOptions.LeaseDuration must be greater than zero.");
        if (RetryBackoff <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RetryBackoff), RetryBackoff, "OutboxOptions.RetryBackoff must be greater than zero.");
        if (MaxRetryBackoff < RetryBackoff)
            throw new ArgumentOutOfRangeException(nameof(MaxRetryBackoff), MaxRetryBackoff, "OutboxOptions.MaxRetryBackoff must be greater than or equal to OutboxOptions.RetryBackoff.");
        if (double.IsNaN(RetryBackoffJitter) || RetryBackoffJitter is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(RetryBackoffJitter), RetryBackoffJitter, "OutboxOptions.RetryBackoffJitter must be a number between 0 and 1 inclusive.");
    }
}
