namespace Trellis.Messaging.AzureServiceBus;

using System.Text.Json;

/// <summary>
/// Tuning for publishing integration events to Azure Service Bus.
/// </summary>
public sealed class AzureServiceBusPublisherOptions
{
    /// <summary>
    /// The producing service or bounded context, stamped on each message and surfaced to consumers as
    /// <see cref="Trellis.Mediator.IntegrationEnvelope.MessageSource"/>. Optional; observability only —
    /// it never participates in deduplication or routing.
    /// </summary>
    public string? MessageSource { get; set; }

    /// <summary>
    /// Maps an event's stable wire name (from <c>[IntegrationEventName]</c>) to the Service Bus topic it is
    /// published to. Defaults to using the wire name verbatim, which is the topic-per-event-type layout:
    /// each contract owns a topic, and a subscriber declares interest by subscribing to the topics it wants
    /// rather than by filtering a firehose.
    /// </summary>
    /// <remarks>
    /// Override this to prefix an environment or team segment (<c>name => $"prod.{name}"</c>), or to collapse
    /// several contracts onto one topic. If you do collapse them, subscriptions need a correlation filter on
    /// <c>sys.Label</c> (the message's <c>Subject</c>, which always carries the wire name) so a subscriber
    /// does not receive contracts it cannot deserialize.
    /// </remarks>
    public Func<string, string> TopicNameResolver { get; set; } = static wireName => wireName;

    /// <summary>
    /// Controls how event bodies are serialized to JSON. Defaults to <see cref="JsonSerializerOptions.Web"/>
    /// (camelCase, case-insensitive reads), matching the convention most HTTP-facing services already use.
    /// </summary>
    /// <remarks>
    /// This is a wire-format decision shared with every consumer of these messages. Change it before the first
    /// message ships, or accept that in-flight messages written with the previous settings must still
    /// deserialize.
    /// </remarks>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = JsonSerializerOptions.Web;
}
