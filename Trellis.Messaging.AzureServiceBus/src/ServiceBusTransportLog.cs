namespace Trellis.Messaging.AzureServiceBus;

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Trellis.Mediator;

/// <summary>High-performance log delegates for the Service Bus transport (satisfies CA1848).</summary>
internal static class ServiceBusTransportLog
{
    private static readonly Action<ILogger, string, Guid, string, Exception?> s_published =
        LoggerMessage.Define<string, Guid, string>(
            LogLevel.Debug,
            new EventId(1, "ServiceBus.Published"),
            "Published integration event {WireName} (message {MessageId}) to Service Bus topic {Topic}.");

    private static readonly Action<ILogger, string, string, Exception?> s_consuming =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(2, "ServiceBus.Consuming"),
            "Consuming integration events from Service Bus subscription {Subscription} on topic {Topic}.");

    private static readonly Action<ILogger, string?, string, string?, Exception?> s_deadLettered =
        LoggerMessage.Define<string?, string, string?>(
            LogLevel.Warning,
            new EventId(3, "ServiceBus.DeadLettered"),
            "Dead-lettering Service Bus message {ServiceBusMessageId}: {Reason} — {Detail}");

    private static readonly Action<ILogger, Guid, string?, InboxDispatchOutcome, Exception?> s_settled =
        LoggerMessage.Define<Guid, string?, InboxDispatchOutcome>(
            LogLevel.Debug,
            new EventId(4, "ServiceBus.Settled"),
            "Message {MessageId} ({WireName}) completed as {Outcome}.");

    private static readonly Action<ILogger, string, ServiceBusErrorSource, Exception?> s_processingError =
        LoggerMessage.Define<string, ServiceBusErrorSource>(
            LogLevel.Error,
            new EventId(5, "ServiceBus.ProcessingError"),
            "Service Bus processing error on {EntityPath} during {ErrorSource}.");

    public static void Published(ILogger logger, string wireName, Guid messageId, string topic) =>
        s_published(logger, wireName, messageId, topic, null);

    public static void Consuming(ILogger logger, string subscription, string topic) =>
        s_consuming(logger, subscription, topic, null);

    public static void DeadLettered(ILogger logger, string? serviceBusMessageId, string reason, string? detail) =>
        s_deadLettered(logger, serviceBusMessageId, reason, detail, null);

    public static void Settled(ILogger logger, Guid messageId, string? wireName, InboxDispatchOutcome outcome) =>
        s_settled(logger, messageId, wireName, outcome, null);

    public static void ProcessingError(ILogger logger, string entityPath, ServiceBusErrorSource errorSource, Exception exception) =>
        s_processingError(logger, entityPath, errorSource, exception);
}
