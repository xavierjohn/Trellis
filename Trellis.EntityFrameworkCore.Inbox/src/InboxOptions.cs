namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Tuning for the transactional inbox (idempotent integration-event consumption).
/// </summary>
public sealed class InboxOptions
{
    /// <summary>The maximum length of <see cref="ConsumerId"/>, matching the mapped key column width.</summary>
    public const int MaxConsumerIdLength = 256;

    /// <summary>
    /// A stable identifier for this subscriber / consumer-group. It is part of the dedup key
    /// (<c>ConsumerId + MessageId</c>), so two services consuming the same message each get one effective
    /// processing. Keep it stable across deploys — renaming it resets dedup history. Required, and at most
    /// <see cref="MaxConsumerIdLength"/> characters.
    /// </summary>
    public string ConsumerId { get; set; } = string.Empty;

    /// <summary>
    /// Creates an independent copy so a repeated registration can apply its <c>configure</c> callback
    /// and validate the result before the new state is committed to the container. Keep in sync with
    /// the properties above; <c>InboxOptionsCloneTests</c> fails if a property is added and not copied.
    /// </summary>
    internal InboxOptions Clone() => new()
    {
        ConsumerId = ConsumerId,
    };

    /// <summary>
    /// Validates the configured values, failing fast at registration so misconfiguration surfaces there
    /// rather than as a runtime error on the first message.
    /// </summary>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConsumerId))
            throw new InvalidOperationException(
                "InboxOptions.ConsumerId must be set to a stable subscriber identifier; it is part of the inbox dedup key.");

        if (ConsumerId.Length > MaxConsumerIdLength)
            throw new InvalidOperationException(
                $"InboxOptions.ConsumerId must be at most {MaxConsumerIdLength} characters; it is stored in a fixed-width key column.");
    }
}
