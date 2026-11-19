namespace Trellis.Mediator;

internal sealed class LocalInboxIntegrationEventPublisher(IInboxDispatcher inbox) : IIntegrationEventPublisher
{
    public async ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var envelope = new IntegrationEnvelope(message.MessageId, message.Event)
        {
            MessageSource = message.MessageSource,
            CausationId = message.CausationId,
            CorrelationId = message.CorrelationId,
            TraceParent = message.TraceParent,
            TraceState = message.TraceState,
        };
        var outcome = await inbox.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
        if (outcome is not (InboxDispatchOutcome.Processed or InboxDispatchOutcome.SkippedDuplicate))
            throw new InvalidOperationException("The inbox did not return a successful dispatch outcome.");
    }
}