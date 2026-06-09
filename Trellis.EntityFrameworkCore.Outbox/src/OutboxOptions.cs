namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Tuning for the transactional outbox relay (<c>OutboxRelay{TContext}</c>).
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>How long the relay waits before polling again when the outbox is empty. Default: 5 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum messages drained per poll. Default: 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum relay attempts before a failing message is parked (left unprocessed but skipped by the
    /// scan so it does not block later messages). Default: 10.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;
}
