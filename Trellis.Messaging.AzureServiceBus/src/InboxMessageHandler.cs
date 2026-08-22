namespace Trellis.Messaging.AzureServiceBus;

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis.Mediator;

/// <summary>
/// Turns a received Service Bus message into an inbox dispatch and settles it accordingly.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="ServiceBusInboxConsumer"/> so the settlement rules do not depend on a live
/// processor: the consumer owns the receive loops and lifetime, this owns what happens to one message.
/// </para>
/// <para>
/// The rules, and why each is what it is:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Processed and SkippedDuplicate both complete.</b> Both mean the message is durably accounted for —
/// either the handlers ran and the dedup row committed with them, or a previous delivery already did that.
/// Abandoning a duplicate would redeliver it forever, because every attempt reaches the same conclusion.
/// </description></item>
/// <item><description>
/// <b>Handler failures propagate.</b> The dispatcher rolled back, so nothing was applied and redelivery is
/// correct. The SDK abandons an unsettled message whose handler threw, and <c>MaxDeliveryCount</c> eventually
/// dead-letters one that keeps failing.
/// </description></item>
/// <item><description>
/// <b>Unusable messages dead-letter immediately.</b> A missing id, a missing contract name, an unknown
/// contract, or a body that will not deserialize are properties of the message, not of this consumer's
/// state. Retrying the same bytes cannot succeed, so abandoning would only burn the delivery count and
/// dead-letter anyway — with no diagnosis attached.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class InboxMessageHandler(
    IServiceScopeFactory scopeFactory,
    IntegrationEventNameMap nameMap,
    JsonSerializerOptions jsonOptions,
    ILogger logger)
{
    public async Task HandleAsync(
        ServiceBusReceivedMessage message,
        IMessageSettler settler,
        CancellationToken cancellationToken)
    {
        var envelopeResult = ServiceBusMessageFormatter.ToEnvelope(message, nameMap, jsonOptions);

        if (!envelopeResult.TryGetValue(out var envelope, out var error))
        {
            // Code, not Kind: the dead-letter reason is what an operator filters the DLQ by, so
            // it has to spell the reason the producer named rather than the case it fell into.
            // Detail carries the human-readable half.
            ServiceBusTransportLog.DeadLettered(logger, message.MessageId, error.Code, error.Detail);

            await settler.DeadLetterAsync(error.Code, error.Detail, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IInboxDispatcher>();

        var outcome = await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);

        await settler.CompleteAsync(cancellationToken).ConfigureAwait(false);

        ServiceBusTransportLog.Settled(logger, envelope.MessageId, message.Subject, outcome);
    }
}