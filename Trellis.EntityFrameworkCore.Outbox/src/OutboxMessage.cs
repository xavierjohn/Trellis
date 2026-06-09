namespace Trellis.EntityFrameworkCore;

/// <summary>
/// A persisted domain event awaiting relay — a transactional-outbox row.
/// </summary>
/// <remarks>
/// <para>
/// The <c>OutboxCaptureInterceptor</c> writes one row per uncommitted domain event in the same
/// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
/// transaction as the aggregate change that raised it, so state and outbound notifications commit
/// atomically. <c>OutboxRelay{TContext}</c> later drains pending rows and re-dispatches each event
/// to its <see cref="Trellis.Mediator.IDomainEventHandler{TEvent}"/>s.
/// </para>
/// <para>
/// This is an infrastructure record, not a domain aggregate; the rows are transient and may be
/// pruned once <see cref="ProcessedAt"/> is set. Deleting processed rows loses no source-of-truth
/// state — the aggregate tables remain authoritative (this is an outbox, not an event store).
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    private OutboxMessage(Guid id, DateTimeOffset occurredAt, string eventType, string payload)
    {
        Id = id;
        OccurredAt = occurredAt;
        EventType = eventType;
        Payload = payload;
    }

    // EF Core materialization constructor.
    private OutboxMessage()
    {
        EventType = null!;
        Payload = null!;
    }

    /// <summary>Database-generated monotonic insertion order; the relay processes ascending.</summary>
    public long Sequence { get; private set; }

    /// <summary>Stable message identity (UUIDv7) for consumer-side idempotency / de-duplication.</summary>
    public Guid Id { get; private set; }

    /// <summary>When the domain event occurred, copied from <see cref="IDomainEvent.OccurredAt"/>.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The assembly-qualified name of the concrete event type, used to rehydrate the payload.</summary>
    public string EventType { get; private set; }

    /// <summary>The JSON-serialized event.</summary>
    public string Payload { get; private set; }

    /// <summary>When the message was successfully relayed; <c>null</c> while pending.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Number of relay attempts so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>The most recent relay error, if any.</summary>
    public string? LastError { get; private set; }

    internal static OutboxMessage Create(Guid id, DateTimeOffset occurredAt, string eventType, string payload) =>
        new(id, occurredAt, eventType, payload);

    internal void MarkProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
        LastError = null;
    }

    internal void RecordFailure(string error)
    {
        Attempts++;
        LastError = error;
    }
}
