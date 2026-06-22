namespace Trellis.EntityFrameworkCore;

/// <summary>
/// A persisted resume cursor: the last <see cref="Position"/> a pull consumer (<see cref="ConsumerId"/>) has
/// advanced past in its source feed.
/// </summary>
/// <remarks>
/// Performance state, not a domain aggregate. Losing or resetting it only forces a wider rescan — the inbox
/// anti-join still guarantees once-effective processing — never incorrect processing.
/// </remarks>
public sealed class ConsumerCheckpoint
{
    // EF Core materialization constructor.
    private ConsumerCheckpoint()
    {
        ConsumerId = null!;
        Position = null!;
    }

    private ConsumerCheckpoint(string consumerId, string position, DateTimeOffset updatedAt)
    {
        ConsumerId = consumerId;
        Position = position;
        UpdatedAt = updatedAt;
    }

    /// <summary>The stable subscriber identifier; the primary key.</summary>
    public string ConsumerId { get; private set; }

    /// <summary>The opaque resume cursor into the source feed (the consumer's own encoding).</summary>
    public string Position { get; private set; }

    /// <summary>When this checkpoint was last advanced.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    internal static ConsumerCheckpoint Create(string consumerId, string position, DateTimeOffset updatedAt) =>
        new(consumerId, position, updatedAt);

    internal void Advance(string position, DateTimeOffset updatedAt)
    {
        Position = position;
        UpdatedAt = updatedAt;
    }
}
