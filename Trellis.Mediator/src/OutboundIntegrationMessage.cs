namespace Trellis.Mediator;

/// <summary>
/// The publish-side counterpart of <see cref="IntegrationEnvelope"/>: the integration event to publish
/// together with the stable <see cref="MessageId"/> a transport must carry verbatim onto the wire.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the id travels with the event.</b> Consumer-side deduplication keys on
/// <c>(ConsumerId, MessageId)</c>, and <see cref="IntegrationEnvelope.MessageId"/> is specified as the
/// producer's outbox message id carried verbatim by the transport. Outbox relay delivery is
/// at-least-once, so the same row can be published more than once — a crash between publishing and the
/// relay's bookkeeping save re-delivers it. A transport that minted a fresh id per publish attempt would
/// put a <i>different</i> <c>MessageId</c> on each copy, the consumer's dedup would miss, and handlers
/// would run twice. Passing the row's own id makes redeliveries indistinguishable to the consumer, which
/// is exactly what the inbox needs.
/// </para>
/// <para>
/// This collapses redeliveries of a <i>single</i> outbox row. It does not collapse the other duplicate the
/// outbox can produce: a retried domain row re-runs its translator and stages a genuinely new integration
/// row with its own id. Consumers still dedupe that on business identity.
/// </para>
/// <para>
/// Optional lineage and W3C trace context are copied from the persisted outbox row, not from the relay's
/// ambient activity, so retries and redeliveries keep the same identity and parent context.
/// </para>
/// </remarks>
/// <param name="MessageId">
/// The stable, unique message identity — the producer's outbox row id (a UUIDv7). Every redelivery of the
/// same row carries this same value. Must not be <see cref="Guid.Empty"/>: an empty id is not a missing
/// value a transport can work around, because every message stamped with it collapses to the same
/// <c>(ConsumerId, MessageId)</c> inbox key and the consumer would discard all but the first as duplicates.
/// </param>
/// <param name="Event">The integration event to publish.</param>
public sealed record OutboundIntegrationMessage(Guid MessageId, IIntegrationEvent Event)
{
    private readonly Guid _messageId = MessageId != Guid.Empty
        ? MessageId
        : throw new ArgumentException("Message identity must not be empty.", nameof(MessageId));

    private readonly IIntegrationEvent _event = Event ?? throw new ArgumentNullException(nameof(Event));

    /// <summary>
    /// Gets the stable, unique message identity — the producer's outbox row id (a UUIDv7).
    /// </summary>
    /// <exception cref="ArgumentException">The value is <see cref="Guid.Empty"/>.</exception>
    public Guid MessageId
    {
        get => _messageId;
        init => _messageId = value != Guid.Empty
            ? value
            : throw new ArgumentException("Message identity must not be empty.", nameof(value));
    }

    /// <summary>
    /// Gets the integration event to publish.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public IIntegrationEvent Event
    {
        get => _event;
        init => _event = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The optional producer namespace.</summary>
    public string? MessageSource { get; init; }

    /// <summary>The source domain outbox row's id, when this event was translated.</summary>
    public Guid? CausationId { get; init; }

    /// <summary>The optional, application-owned business workflow identifier.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The persisted W3C traceparent value, when available.</summary>
    public string? TraceParent { get; init; }

    /// <summary>The persisted W3C tracestate value, when available.</summary>
    public string? TraceState { get; init; }
}