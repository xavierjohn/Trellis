namespace Trellis.Mediator;

using Microsoft.Extensions.DependencyInjection;

internal sealed class LocalInboxIntegrationEventPublisher(IServiceProvider services) : IIntegrationEventPublisher
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
        // The host can validate publisher selection before an application's inbox initializer has run.
        var outcome = await services.GetRequiredService<IInboxDispatcher>()
            .DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
        if (outcome is not (InboxDispatchOutcome.Processed or InboxDispatchOutcome.SkippedDuplicate))
            throw new InvalidOperationException(
                $"Inbox dispatch returned unexpected outcome '{outcome}' for message '{message.MessageId}'.");
    }
}