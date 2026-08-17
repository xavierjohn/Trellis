namespace Trellis.Messaging.AzureServiceBus;

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Mediator;

/// <summary>
/// Publishes integration events to Azure Service Bus, one topic per contract.
/// </summary>
/// <remarks>
/// <para>
/// Registered in place of the in-process publisher, this is what turns the outbox from a local relay into a
/// cross-service one. It is the transport half of the guarantee the inbox provides: the outbox row id is
/// stamped on <see cref="ServiceBusMessage.MessageId"/> verbatim, so however many times the relay delivers a
/// row, the consumer sees one message id and deduplicates on it.
/// </para>
/// <para>
/// Senders are cached per topic and shared, which is the SDK's documented usage: a sender owns an AMQP link,
/// and creating one per publish would make every message pay a link handshake.
/// </para>
/// </remarks>
public sealed class ServiceBusIntegrationEventPublisher : IIntegrationEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly IntegrationEventNameMap _nameMap;
    private readonly AzureServiceBusPublisherOptions _options;
    private readonly ILogger<ServiceBusIntegrationEventPublisher> _logger;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusIntegrationEventPublisher"/> class.
    /// </summary>
    /// <param name="client">The Service Bus client. Its lifetime is owned by the container, not by this publisher.</param>
    /// <param name="nameMap">Maps each event type to the stable wire name that names its topic.</param>
    /// <param name="options">Publishing options.</param>
    /// <param name="logger">Logger.</param>
    public ServiceBusIntegrationEventPublisher(
        ServiceBusClient client,
        IntegrationEventNameMap nameMap,
        IOptions<AzureServiceBusPublisherOptions> options,
        ILogger<ServiceBusIntegrationEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(nameMap);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _nameMap = nameMap;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The event type has no registered wire name. This is a contract bug — the event cannot be named on the
    /// wire, so no consumer could identify it — and it is deliberately fatal to the publish. The outbox row
    /// stays unsent and the relay retries, which surfaces the missing <c>[IntegrationEventName]</c> as a
    /// stuck message rather than as silently dropped traffic.
    /// </exception>
    public async ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var eventType = message.Event.GetType();
        var wireName = _nameMap.NameFor(eventType);
        if (!wireName.HasValue)
            throw new InvalidOperationException(
                $"Integration event '{eventType.FullName}' has no registered wire name. Annotate it with " +
                "[IntegrationEventName(\"...\")] and include its assembly when building the IntegrationEventNameMap; " +
                "without a wire name no consumer can identify the message.");

        var topic = _options.TopicNameResolver(wireName.Value);
        var serviceBusMessage = ServiceBusMessageFormatter.ToServiceBusMessage(
            message, wireName.Value, _options.MessageSource, _options.JsonSerializerOptions);

        var sender = await GetSenderAsync(topic).ConfigureAwait(false);

        await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);

        ServiceBusTransportLog.Published(_logger, wireName.Value, message.MessageId, topic);
    }

    /// <summary>
    /// Returns the cached sender for a topic, creating one if this is the first publish to it.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>GetOrAdd</c> with a factory: that factory can run on several threads at once for
    /// the same topic, and every instance but the winner is discarded without ever being closed. Creating the
    /// candidate here and closing it when another thread got there first keeps the cache the sole owner of
    /// every sender that exists, which is what makes <see cref="DisposeAsync"/> able to close all of them.
    /// </remarks>
    private async ValueTask<ServiceBusSender> GetSenderAsync(string topic)
    {
        if (_senders.TryGetValue(topic, out var existing))
            return existing;

        var candidate = _client.CreateSender(topic);
        var winner = _senders.GetOrAdd(topic, candidate);
        if (!ReferenceEquals(winner, candidate))
            await candidate.DisposeAsync().ConfigureAwait(false);

        return winner;
    }

    /// <summary>
    /// Closes the cached senders. The <see cref="ServiceBusClient"/> itself is left alone — it is resolved
    /// from the container and may be shared with consumers and other publishers.
    /// </summary>
    /// <remarks>
    /// Draining until the cache is empty rather than iterating once: a publish that passed the disposed check
    /// just before this ran can still add a sender afterwards, and a single pass would step straight past it.
    /// The loop terminates because the flag is set first, so only calls already in flight can add anything.
    /// A sender added after the very last emptiness check would still be missed; that residual case is left
    /// to the container disposing the <see cref="ServiceBusClient"/>, which closes everything it created.
    /// </remarks>
    /// <returns>A task that completes when every sender has been closed.</returns>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        while (!_senders.IsEmpty)
        {
            foreach (var topic in _senders.Keys)
            {
                if (_senders.TryRemove(topic, out var sender))
                    await sender.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
