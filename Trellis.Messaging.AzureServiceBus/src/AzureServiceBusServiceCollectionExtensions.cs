namespace Trellis.Messaging.AzureServiceBus;

using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Mediator;

/// <summary>
/// Registration for the Azure Service Bus integration-event transport.
/// </summary>
public static class AzureServiceBusServiceCollectionExtensions
{
    /// <summary>
    /// Per-collection accumulated consumer configuration, used only to validate at registration time.
    /// Keyed weakly so it does not keep a service collection alive or leak between tests.
    /// </summary>
    private static readonly ConditionalWeakTable<IServiceCollection, AzureServiceBusConsumerOptions> s_consumerProbes = [];

    /// <summary>
    /// Publishes integration events to Azure Service Bus instead of to in-process handlers.
    /// </summary>
    /// <remarks>
    /// This <b>replaces</b> any existing <see cref="IIntegrationEventPublisher"/> registration rather than
    /// adding to it. In-process fan-out and broker publication are alternatives, not layers: registering both
    /// would deliver each event locally <i>and</i> over the wire, so a service subscribed to its own topic
    /// would handle everything twice. Replacing makes the choice explicit and order-independent.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="nameMap">Maps each event type to the wire name that names its topic.</param>
    /// <param name="configure">Optional publisher configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAzureServiceBusIntegrationEventPublisher(
        this IServiceCollection services,
        IntegrationEventNameMap nameMap,
        Action<AzureServiceBusPublisherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(nameMap);

        var options = services.AddOptions<AzureServiceBusPublisherOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.RemoveAll<IIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(provider => new ServiceBusIntegrationEventPublisher(
            provider.GetRequiredService<ServiceBusClient>(),
            nameMap,
            provider.GetRequiredService<IOptions<AzureServiceBusPublisherOptions>>(),
            provider.GetRequiredService<ILogger<ServiceBusIntegrationEventPublisher>>()));

        return services;
    }

    /// <summary>
    /// Receives integration events from Azure Service Bus into the transactional inbox.
    /// </summary>
    /// <remarks>
    /// Requires an <see cref="IInboxDispatcher"/> registration (<c>AddTrellisInbox&lt;TContext&gt;()</c>) —
    /// consuming without one would run handlers without deduplication, which is the failure the inbox exists
    /// to prevent. The dispatcher is resolved per message from a fresh scope, so it is not required to be
    /// registered before this call.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="nameMap">Resolves a received message's wire name to its local event type.</param>
    /// <param name="configure">Consumer configuration; must register at least one subscription.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAzureServiceBusIntegrationEventConsumer(
        this IServiceCollection services,
        IntegrationEventNameMap nameMap,
        Action<AzureServiceBusConsumerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(nameMap);
        ArgumentNullException.ThrowIfNull(configure);

        // Validate against a probe that accumulates every call's configuration, not a fresh one per call.
        // Subscriptions add up across calls onto a single options instance at runtime, so a per-call probe
        // would pass two registrations that are each fine alone and together start two processors competing
        // on one subscription.
        var probe = s_consumerProbes.GetOrCreateValue(services);
        configure(probe);
        probe.Validate();

        services.AddOptions<AzureServiceBusConsumerOptions>().Configure(configure);

        // AddHostedService deduplicates on ServiceBusInboxConsumer even through a factory, so registering
        // twice configures one consumer with both callers' subscriptions rather than running two.
        services.AddHostedService(provider => new ServiceBusInboxConsumer(
            provider.GetRequiredService<ServiceBusClient>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            nameMap,
            provider.GetRequiredService<IOptions<AzureServiceBusConsumerOptions>>(),
            provider.GetRequiredService<ILogger<ServiceBusInboxConsumer>>()));

        return services;
    }
}