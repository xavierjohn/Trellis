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
    }
}
