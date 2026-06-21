namespace Trellis.EntityFrameworkCore;

/// <summary>
/// The consume-side envelope handed to <see cref="IInboxDispatcher"/>: a stable <see cref="MessageId"/>
/// plus the deserialized <see cref="IIntegrationEvent"/>. Only <see cref="MessageId"/> and
/// <see cref="Event"/> are load-bearing; the remaining members are optional lineage / observability
/// metadata and never participate in deduplication.
/// </summary>
/// <param name="MessageId">
/// The stable, unique id used for deduplication — the producer's outbox message id (a UUIDv7) carried
/// verbatim by the transport. Redeliveries of the same message carry the same value.
/// </param>
/// <param name="Event">The deserialized integration event to dispatch to its handlers.</param>
public sealed record IntegrationEnvelope(Guid MessageId, IIntegrationEvent Event)
{
    /// <summary>Optional producer namespace (the originating service / bounded context), for observability.</summary>
    public string? MessageSource { get; init; }

    /// <summary>Optional lineage: the id of the message that directly caused this one (the source outbox id).</summary>
    public Guid? CausationId { get; init; }

    /// <summary>Optional lineage: the workflow / conversation id shared across a business transaction.</summary>
    public string? CorrelationId { get; init; }
}
