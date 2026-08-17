namespace Trellis.Messaging.AzureServiceBus;

using System.Text.Json;

/// <summary>
/// One Service Bus subscription this service consumes from.
/// </summary>
/// <param name="TopicName">The topic. With the default topic-per-contract layout this is the event's wire name.</param>
/// <param name="SubscriptionName">The subscription on that topic belonging to this service.</param>
public sealed record ServiceBusSubscription(string TopicName, string SubscriptionName)
{
    /// <summary>Gets the topic name.</summary>
    public string TopicName { get; init; } = !string.IsNullOrWhiteSpace(TopicName)
        ? TopicName
        : throw new ArgumentException("Topic name must not be blank.", nameof(TopicName));

    /// <summary>Gets the subscription name.</summary>
    public string SubscriptionName { get; init; } = !string.IsNullOrWhiteSpace(SubscriptionName)
        ? SubscriptionName
        : throw new ArgumentException("Subscription name must not be blank.", nameof(SubscriptionName));
}

/// <summary>
/// Tuning for consuming integration events from Azure Service Bus into the transactional inbox.
/// </summary>
/// <remarks>
/// The subscriber identity used for deduplication is <b>not</b> configured here — it is
/// <c>InboxOptions.ConsumerId</c>, so every transport a service consumes from shares one dedup namespace and
/// a message that arrives twice by two routes is still processed once.
/// </remarks>
public sealed class AzureServiceBusConsumerOptions
{
    /// <summary>
    /// The subscriptions to receive from. Each gets its own processor.
    /// </summary>
    public IList<ServiceBusSubscription> Subscriptions { get; } = [];

    /// <summary>
    /// How many messages one subscription processes concurrently. Defaults to 1.
    /// </summary>
    /// <remarks>
    /// The default is deliberately conservative: each concurrent message runs handlers inside its own
    /// database transaction, so raising this raises pressure on the connection pool and the odds of write
    /// contention between handlers. Deduplication stays correct at any value — concurrent delivery of the
    /// same message is resolved by the inbox's composite primary key — so this is purely a throughput knob.
    /// </remarks>
    public int MaxConcurrentCalls { get; set; } = 1;

    /// <summary>
    /// How many messages the client fetches ahead of processing. Defaults to 0 (no prefetch).
    /// </summary>
    /// <remarks>
    /// Prefetched messages have their lock clock already running, so a large prefetch combined with slow
    /// handlers expires locks and causes redelivery. Redelivery is safe here (the inbox absorbs it) but it
    /// wastes work; raise this only once handler latency is known to be well inside the lock duration.
    /// </remarks>
    public int PrefetchCount { get; set; }

    /// <summary>
    /// Controls how event bodies are deserialized. Must agree with the producer's settings; defaults to
    /// <see cref="JsonSerializerOptions.Web"/>, matching <see cref="AzureServiceBusPublisherOptions"/>.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = JsonSerializerOptions.Web;

    /// <summary>
    /// Adds a subscription to receive from.
    /// </summary>
    /// <param name="topicName">The topic name.</param>
    /// <param name="subscriptionName">The subscription name.</param>
    /// <returns>The same options instance, for chaining.</returns>
    public AzureServiceBusConsumerOptions Subscribe(string topicName, string subscriptionName)
    {
        Subscriptions.Add(new ServiceBusSubscription(topicName, subscriptionName));
        return this;
    }

    /// <summary>
    /// Validates the configured values, failing at registration rather than on the first message.
    /// </summary>
    internal void Validate()
    {
        if (Subscriptions.Count == 0)
            throw new InvalidOperationException(
                "AzureServiceBusConsumerOptions has no subscriptions; call Subscribe(topic, subscription) at least once, " +
                "otherwise the consumer would start and silently receive nothing.");

        if (MaxConcurrentCalls < 1)
            throw new InvalidOperationException("AzureServiceBusConsumerOptions.MaxConcurrentCalls must be at least 1.");

        if (PrefetchCount < 0)
            throw new InvalidOperationException("AzureServiceBusConsumerOptions.PrefetchCount must not be negative.");

        var duplicate = Subscriptions
            .GroupBy(static s => (s.TopicName, s.SubscriptionName))
            .FirstOrDefault(static g => g.Count() > 1);

        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Subscription '{duplicate.Key.SubscriptionName}' on topic '{duplicate.Key.TopicName}' is registered more than once; " +
                "each registration starts its own processor, so the duplicate only competes with itself for the same messages.");
    }
}
