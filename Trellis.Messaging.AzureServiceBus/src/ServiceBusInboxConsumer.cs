namespace Trellis.Messaging.AzureServiceBus;

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Mediator;

/// <summary>
/// Receives integration events from Azure Service Bus and feeds them to the transactional inbox.
/// </summary>
/// <remarks>
/// <para>
/// The settlement rules are the whole design, and they follow from what <see cref="IInboxDispatcher"/>
/// guarantees:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Complete</b> on <see cref="InboxDispatchOutcome.Processed"/> <i>and</i> on
/// <see cref="InboxDispatchOutcome.SkippedDuplicate"/>. Both mean the message is durably accounted for; a
/// duplicate that was abandoned instead would be redelivered forever, since every attempt would reach the
/// same conclusion.
/// </description></item>
/// <item><description>
/// <b>Abandon</b> when a handler throws. The dispatcher rolls its transaction back, so nothing was applied
/// and redelivery is the correct response. The exception is deliberately not caught here: the SDK abandons
/// an unsettled message whose handler threw, and routes the exception to the error handler, which is exactly
/// the behaviour wanted. Service Bus dead-letters the message once its <c>MaxDeliveryCount</c> is reached,
/// which is where a persistently failing handler ends up.
/// </description></item>
/// <item><description>
/// <b>Dead-letter</b> when the message itself is unusable — no parseable id, no contract name, an unknown
/// contract, or a body that does not deserialize. Retrying the same bytes cannot change the outcome, so
/// abandoning would only burn the delivery count before dead-lettering anyway, with no diagnosis attached.
/// </description></item>
/// </list>
/// <para>
/// Each message is dispatched inside its own DI scope, because the inbox commits handler side effects and
/// the dedup row in one unit of work and that unit of work is scoped.
/// </para>
/// </remarks>
public sealed class ServiceBusInboxConsumer : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegrationEventNameMap _nameMap;
    private readonly AzureServiceBusConsumerOptions _options;
    private readonly ILogger<ServiceBusInboxConsumer> _logger;
    private readonly List<ServiceBusProcessor> _processors = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusInboxConsumer"/> class.
    /// </summary>
    /// <param name="client">The Service Bus client.</param>
    /// <param name="scopeFactory">Creates the per-message DI scope the inbox dispatch runs in.</param>
    /// <param name="nameMap">Resolves a message's wire name to its local event type.</param>
    /// <param name="options">Consumer options.</param>
    /// <param name="logger">Logger.</param>
    public ServiceBusInboxConsumer(
        ServiceBusClient client,
        IServiceScopeFactory scopeFactory,
        IntegrationEventNameMap nameMap,
        IOptions<AzureServiceBusConsumerOptions> options,
        ILogger<ServiceBusInboxConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(nameMap);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _scopeFactory = scopeFactory;
        _nameMap = nameMap;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var subscription in _options.Subscriptions)
        {
            var processor = _client.CreateProcessor(
                subscription.TopicName,
                subscription.SubscriptionName,
                new ServiceBusProcessorOptions
                {
                    // Settlement is decided by the inbox outcome, so the processor must not settle for us.
                    AutoCompleteMessages = false,
                    MaxConcurrentCalls = _options.MaxConcurrentCalls,
                    PrefetchCount = _options.PrefetchCount,
                });

            processor.ProcessMessageAsync += ProcessMessageAsync;
            processor.ProcessErrorAsync += ProcessErrorAsync;
            _processors.Add(processor);

            await processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);

            ServiceBusTransportLog.Consuming(_logger, subscription.SubscriptionName, subscription.TopicName);
        }

        // The processors own their own receive loops; this task just parks until shutdown.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processor in _processors)
        {
            await processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
            await processor.DisposeAsync().ConfigureAwait(false);
        }

        _processors.Clear();

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var envelopeResult = ServiceBusMessageFormatter.ToEnvelope(
            args.Message, _nameMap, _options.JsonSerializerOptions);

        if (!envelopeResult.TryGetValue(out var envelope, out var error))
        {
            ServiceBusTransportLog.DeadLettered(_logger, args.Message.MessageId, error.Code, error.Detail);

            await args.DeadLetterMessageAsync(args.Message, error.Code, error.Detail, args.CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IInboxDispatcher>();

        var outcome = await dispatcher.DispatchAsync(envelope, args.CancellationToken).ConfigureAwait(false);

        await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);

        ServiceBusTransportLog.Settled(_logger, envelope.MessageId, args.Message.Subject, outcome);
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        ServiceBusTransportLog.ProcessingError(_logger, args.EntityPath, args.ErrorSource, args.Exception);
        return Task.CompletedTask;
    }
}
