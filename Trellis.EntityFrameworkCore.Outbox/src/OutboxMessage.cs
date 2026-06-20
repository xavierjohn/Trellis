namespace Trellis.EntityFrameworkCore;

/// <summary>
/// A persisted event awaiting relay — a transactional-outbox row. Carries either a captured
/// <see cref="IDomainEvent"/> or a translated <see cref="IIntegrationEvent"/>, distinguished by
/// <see cref="Kind"/>.
/// </summary>
/// <remarks>
/// <para>
/// For domain rows, the <c>OutboxCaptureInterceptor</c> writes one row per uncommitted domain event in
/// the same
/// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
/// transaction as the aggregate change that raised it, so state and outbound notifications commit
/// atomically. <c>OutboxRelay{TContext}</c> later drains pending rows and routes each by <see cref="Kind"/>:
/// domain events re-dispatch to <see cref="Trellis.Mediator.IDomainEventHandler{TEvent}"/>s, and any
/// integration events their translators produce are staged as new <see cref="OutboxMessageKind.Integration"/>
/// rows and published through <see cref="Trellis.Mediator.IIntegrationEventPublisher"/>.
/// </para>
/// <para>
/// This is an infrastructure record, not a domain aggregate; the rows are transient and may be
/// pruned once <see cref="ProcessedAt"/> is set. Deleting processed rows loses no source-of-truth
/// state — the aggregate tables remain authoritative (this is an outbox, not an event store).
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    private OutboxMessage(Guid id, DateTimeOffset occurredAt, string eventType, string payload, OutboxMessageKind kind)
    {
        Id = id;
        OccurredAt = occurredAt;
        EventType = eventType;
        Payload = payload;
        Kind = kind;
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

    /// <summary>Whether this row carries a domain event or a translated integration event.</summary>
    public OutboxMessageKind Kind { get; private set; }

    /// <summary>When the event occurred, copied from the event's <c>OccurredAt</c>.</summary>
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

    /// <summary>
    /// The UTC instant until which a relay drain holds an exclusive claim (lease) on this row; <c>null</c>
    /// when unclaimed. The relay only claims rows whose lease is absent or expired, so concurrent relay
    /// instances (horizontal scale-out) never publish the same row twice. A crashed instance's rows become
    /// reclaimable once the lease expires.
    /// </summary>
    public DateTime? LockedUntil { get; private set; }

    /// <summary>The claim token of the relay drain that currently holds this row; <c>null</c> when unclaimed.</summary>
    public Guid? LockedBy { get; private set; }

    internal static OutboxMessage Create(Guid id, DateTimeOffset occurredAt, string eventType, string payload, OutboxMessageKind kind) =>
        new(id, occurredAt, eventType, payload, kind);

    internal void MarkProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
        LastError = null;
        ReleaseLease();
    }

    internal void RecordFailure(string error)
    {
        Attempts++;
        LastError = error;
        ReleaseLease();
    }

    // Release the claim so a processed row is tidy and a failed row is immediately reclaimable by any instance.
    private void ReleaseLease()
    {
        LockedUntil = null;
        LockedBy = null;
    }
}
