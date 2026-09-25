namespace Trellis.Messaging.AzureServiceBus;

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Trellis.Mediator;

/// <summary>
/// Translates between Trellis's publish/consume contracts and Service Bus messages. Kept separate from the
/// publisher and the processor so the wire format can be asserted without a broker.
/// </summary>
internal static class ServiceBusMessageFormatter
{
    /// <summary>
    /// Builds the Service Bus message for an outbound integration event.
    /// </summary>
    /// <remarks>
    /// <see cref="ServiceBusMessage.MessageId"/> is the outbox row id, carried verbatim. This is the single
    /// most important line in the package: relay delivery is at-least-once, so the same row can reach the
    /// broker more than once, and the consumer's <c>(ConsumerId, MessageId)</c> inbox dedup only collapses
    /// those copies if they all carry the same id. Generating one here — which is what the SDK does if the
    /// member is left unset — would make every redelivery look like a new message.
    /// </remarks>
    public static ServiceBusMessage ToServiceBusMessage(
        OutboundIntegrationMessage message,
        string wireName,
        string? messageSource,
        JsonSerializerOptions jsonOptions)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message.Event, message.Event.GetType(), jsonOptions);

        var serviceBusMessage = new ServiceBusMessage(body)
        {
            MessageId = message.MessageId.ToString(),
            Subject = wireName,
            ContentType = ServiceBusMessageFormat.JsonContentType,
            CorrelationId = message.CorrelationId,
        };

        var source = message.MessageSource ?? messageSource;
        if (!string.IsNullOrWhiteSpace(source))
            serviceBusMessage.ApplicationProperties[ServiceBusMessageFormat.MessageSourceProperty] = source;

        if (message.CausationId is { } causationId)
            serviceBusMessage.ApplicationProperties[ServiceBusMessageFormat.CausationIdProperty] = causationId.ToString();

        if (!string.IsNullOrWhiteSpace(message.TraceParent))
            serviceBusMessage.ApplicationProperties[ServiceBusMessageFormat.TraceParentProperty] = message.TraceParent;

        if (!string.IsNullOrWhiteSpace(message.TraceState))
            serviceBusMessage.ApplicationProperties[ServiceBusMessageFormat.TraceStateProperty] = message.TraceState;

        return serviceBusMessage;
    }

    /// <summary>
    /// Rebuilds the inbox envelope from a received message, or explains why the message is undeliverable.
    /// </summary>
    /// <remarks>
    /// Every failure here is a property of the message itself, not of this consumer's state, so retrying the
    /// same bytes can never succeed. The caller dead-letters rather than abandons.
    /// </remarks>
    public static Result<IntegrationEnvelope> ToEnvelope(
        ServiceBusReceivedMessage message,
        IntegrationEventNameMap nameMap,
        JsonSerializerOptions jsonOptions)
    {
        if (!Guid.TryParse(message.MessageId, out var messageId) || messageId == Guid.Empty)
            return Result.Fail<IntegrationEnvelope>(ServiceBusConsumerErrors.UnusableMessageId(message.MessageId));

        if (string.IsNullOrWhiteSpace(message.Subject))
            return Result.Fail<IntegrationEnvelope>(ServiceBusConsumerErrors.MissingSubject);

        var eventType = nameMap.TypeFor(message.Subject);
        if (!eventType.HasValue)
            return Result.Fail<IntegrationEnvelope>(ServiceBusConsumerErrors.UnknownContract(message.Subject));

        IIntegrationEvent? integrationEvent;
        try
        {
            integrationEvent = JsonSerializer.Deserialize(message.Body.ToMemory().Span, eventType.Value, jsonOptions)
                as IIntegrationEvent;
        }
        catch (JsonException ex)
        {
            return Result.Fail<IntegrationEnvelope>(ServiceBusConsumerErrors.MalformedBody(message.Subject, ex.Message));
        }
        catch (NotSupportedException ex)
        {
            // The contract itself cannot be materialized — an abstract or interface-typed member, or a shape
            // with no converter. Every future delivery of these bytes fails identically, so this belongs with
            // the dead-letter failures rather than escaping as a fault the consumer would retry.
            return Result.Fail<IntegrationEnvelope>(ServiceBusConsumerErrors.MalformedBody(message.Subject, ex.Message));
        }

        if (integrationEvent is null)
            return Result.Fail<IntegrationEnvelope>(ServiceBusConsumerErrors.MalformedBody(message.Subject, "the body deserialized to null"));

        var properties = message.ApplicationProperties;
        string? source = null;
        if (properties.TryGetValue(ServiceBusMessageFormat.MessageSourceProperty, out var rawSource))
        {
            if (rawSource is not null and not string)
                return Result.Fail<IntegrationEnvelope>(ServiceBusConsumerErrors.MalformedMetadata(
                    ServiceBusMessageFormat.MessageSourceProperty, "a string"));

            source = rawSource as string;
            if (string.IsNullOrWhiteSpace(source))
                source = null;
        }

        Guid? causation = null;
        if (properties.TryGetValue(ServiceBusMessageFormat.CausationIdProperty, out var rawCausation))
        {
            if (rawCausation is not string causationText
                || !Guid.TryParse(causationText, out var causationId)
                || causationId == Guid.Empty)
                return Result.Fail<IntegrationEnvelope>(ServiceBusConsumerErrors.MalformedMetadata(
                    ServiceBusMessageFormat.CausationIdProperty, "a non-empty GUID string"));

            causation = causationId;
        }

        var traceParent = properties.TryGetValue(ServiceBusMessageFormat.TraceParentProperty, out var rawTraceParent)
            ? rawTraceParent as string : null;
        var traceState = properties.TryGetValue(ServiceBusMessageFormat.TraceStateProperty, out var rawTraceState)
            ? rawTraceState as string : null;

        return Result.Ok(new IntegrationEnvelope(messageId, integrationEvent)
        {
            MessageSource = source,
            CausationId = causation,
            CorrelationId = string.IsNullOrWhiteSpace(message.CorrelationId) ? null : message.CorrelationId,
            TraceParent = traceParent,
            TraceState = traceState,
        });
    }
}