namespace Trellis;

/// <summary>
/// The persistence-native description of a processed message handed to
/// <see cref="IInboxStore.TryRecordAsync"/>: the dedup identity (<see cref="MessageId"/>) plus optional
/// lineage / observability metadata the store may persist alongside the dedup row. It carries no transport
/// or messaging type, so the inbox store contract depends only on Trellis.Core.
/// </summary>
/// <param name="MessageId">
/// The stable, unique id used for deduplication — the producer's outbox message id (a UUIDv7) carried
/// verbatim by the transport. Redeliveries of the same message carry the same value.
/// </param>
/// <param name="EventType">
/// A stable identifier for the message's type (e.g. an assembly-qualified type name), recorded for audit.
/// </param>
/// <param name="OccurredAt">When the business fact occurred.</param>
/// <param name="MessageSource">Optional producing service / bounded context, for observability.</param>
/// <param name="CausationId">Optional lineage: the id of the message that directly caused this one.</param>
/// <param name="CorrelationId">Optional lineage: the workflow / conversation id shared across a business transaction.</param>
public sealed record InboxRecord(
    Guid MessageId,
    string EventType,
    DateTimeOffset OccurredAt,
    string? MessageSource = null,
    Guid? CausationId = null,
    string? CorrelationId = null);