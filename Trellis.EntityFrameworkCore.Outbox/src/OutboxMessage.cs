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
    /// The UTC instant until which this row is not eligible for draining; <c>null</c> when eligible now.
    /// It serves two roles: while a drain is publishing the row it is the exclusive claim (lease) expiry, so
    /// concurrent relay instances (horizontal scale-out) never publish the same row twice and a crashed
    /// instance's rows become reclaimable once the lease expires; after a failed attempt it is the retry
    /// time (an exponential backoff), so a failing message is retried with growing spacing rather than in a
    /// tight loop.
    /// </summary>
    public DateTime? LockedUntil { get; private set; }

    /// <summary>The claim token of the relay drain that currently holds this row; <c>null</c> when unclaimed.</summary>
    public Guid? LockedBy { get; private set; }

    /// <summary>
    /// For <see cref="OutboxMessageKind.Domain"/> rows, the <see cref="Type.FullName"/> of every
    /// <see cref="Trellis.Mediator.IDomainEventHandler{TEvent}"/> that has already completed for this
    /// event. A retry skips these, so a handler that succeeded on an earlier attempt is never invoked
    /// twice because an unrelated sibling failed. Empty for integration rows, which have a single
    /// publish step rather than a fan-out.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> cleared by <c>IOutboxMaintenance.ReplayAsync</c>: replaying a parked
    /// message means "finish the work that did not happen", so the handlers that already ran must stay
    /// skipped. It is also not cleared by <see cref="MarkProcessed"/>, so a processed row still shows
    /// which handlers ran, for forensics.
    /// </remarks>
    public IReadOnlyList<string> CompletedHandlers { get; private set; } = [];

    internal static OutboxMessage Create(Guid id, DateTimeOffset occurredAt, string eventType, string payload, OutboxMessageKind kind) =>
        new(id, occurredAt, eventType, payload, kind);

    // Cumulative by contract: the publisher's report already carries forward the handlers it skipped, so
    // this overwrites rather than merges.
    internal void RecordHandlerProgress(IReadOnlyList<string> completedHandlers) =>
        CompletedHandlers = completedHandlers;

    internal void MarkProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
        LastError = null;
        ReleaseLease();
    }

    internal void RecordFailure(string error, DateTime retryAt)
    {
        Attempts++;
        LastError = error;
        // Back the lease off to the retry time instead of releasing it: the row stays ineligible until
        // retryAt, which spaces out retries (no tight claim/fail/reclaim loop that would hammer the DB) yet
        // leaves it reclaimable by any instance afterwards. LockedBy is cleared — this is a backoff, not a claim.
        LockedUntil = retryAt;
        LockedBy = null;
    }

    // Release the claim so a processed row is tidy; failed rows back the lease off instead (see RecordFailure).
    private void ReleaseLease()
    {
        LockedUntil = null;
        LockedBy = null;
    }
}