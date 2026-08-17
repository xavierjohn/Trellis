namespace Trellis.Messaging.AzureServiceBus;

/// <summary>
/// The Service Bus message members Trellis assigns meaning to, so a producer and a consumer written
/// against this package agree on the wire format without re-deriving it from the code.
/// </summary>
/// <remarks>
/// Everything here maps onto a <i>standard</i> Service Bus member rather than a custom application
/// property wherever one exists. `MessageId` and `Subject` are indexed by the broker and surfaced in the
/// portal, Service Bus Explorer, and subscription filters, so a message stays diagnosable and routable by
/// tools that know nothing about Trellis.
/// </remarks>
public static class ServiceBusMessageFormat
{
    /// <summary>
    /// The application property carrying <see cref="Trellis.Mediator.IntegrationEnvelope.MessageSource"/> —
    /// the producing service or bounded context. Optional; observability only.
    /// </summary>
    public const string MessageSourceProperty = "trellis-message-source";

    /// <summary>
    /// The content type stamped on every message body, which is UTF-8 JSON.
    /// </summary>
    public const string JsonContentType = "application/json";
}
