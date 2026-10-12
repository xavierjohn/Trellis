namespace Trellis.EntityFrameworkCore;

/// <summary>
/// A persisted record that a (<see cref="ConsumerId"/>, <see cref="MessageId"/>) message has been processed.
/// Its existence is the dedup guarantee: a redelivery of the same message finds the row and is skipped, so a
/// handler's side effects run effectively once.
/// </summary>
/// <remarks>
/// This is transient infrastructure, not a domain aggregate. There is no automatic expiry. Any pruning
/// policy must cover all redelivery and replay sources; deleting a row removes its dedup protection, so an
/// older delivery can apply the message again.
/// </remarks>
public sealed class InboxMessage
{
    // EF Core materialization constructor.
    private InboxMessage()
    {
        ConsumerId = null!;
        EventType = null!;
    }

    private InboxMessage(
        string consumerId, Guid messageId, string? messageSource, string eventType,
        DateTimeOffset occurredAt, DateTimeOffset processedAt, Guid? causationId, string? correlationId)
    {
        ConsumerId = consumerId;
        MessageId = messageId;
        MessageSource = messageSource;
        EventType = eventType;
        OccurredAt = occurredAt;
        ProcessedAt = processedAt;
        CausationId = causationId;
        CorrelationId = correlationId;
    }

    /// <summary>The stable subscriber identifier; part of the dedup key.</summary>
    public string ConsumerId { get; private set; }

    /// <summary>The message's stable id (the producer's outbox id); part of the dedup key.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>The producing service / bounded context, if the envelope supplied one.</summary>
    public string? MessageSource { get; private set; }

    /// <summary>The assembly-qualified name of the integration event type, recorded for audit.</summary>
    public string EventType { get; private set; }

    /// <summary>When the business fact occurred (copied from the event's <c>OccurredAt</c>).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The UTC timestamp assigned when the dedup row is staged, before handlers and the commit.</summary>
    public DateTimeOffset ProcessedAt { get; private set; }

    /// <summary>Optional lineage: the id of the message that directly caused this one.</summary>
    public Guid? CausationId { get; private set; }

    /// <summary>Optional lineage: the workflow / conversation id.</summary>
    public string? CorrelationId { get; private set; }

    internal static InboxMessage Create(string consumerId, InboxRecord record, DateTimeOffset processedAt) =>
        new(consumerId, record.MessageId, record.MessageSource, record.EventType,
            record.OccurredAt, processedAt, record.CausationId, record.CorrelationId);
}