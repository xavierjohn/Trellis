namespace Trellis.Messaging.AzureServiceBus.Tests;

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Mediator;

public class AzureServiceBusRegistrationTests
{
    private static readonly IntegrationEventNameMap Map = new(
    [
        new KeyValuePair<string, Type>(OrderPlaced.WireName, typeof(OrderPlaced)),
    ]);

    [Fact]
    public void AddPublisher_ReplacesAnyExistingPublisherRatherThanAddingToIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventPublisher, NoOpPublisher>();

        services.AddAzureServiceBusIntegrationEventPublisher(Map);

        services.Where(d => d.ServiceType == typeof(IIntegrationEventPublisher)).Should().ContainSingle();
        Resolve<IIntegrationEventPublisher>(services).Should().BeOfType<ServiceBusIntegrationEventPublisher>();
    }

    [Fact]
    public void AddPublisher_IsOrderIndependentWithRespectToAnExistingPublisher()
    {
        var services = new ServiceCollection();
        services.AddAzureServiceBusIntegrationEventPublisher(Map);
        services.AddSingleton<IIntegrationEventPublisher, NoOpPublisher>();
        services.AddAzureServiceBusIntegrationEventPublisher(Map);

        services.Where(d => d.ServiceType == typeof(IIntegrationEventPublisher)).Should().ContainSingle();
        Resolve<IIntegrationEventPublisher>(services).Should().BeOfType<ServiceBusIntegrationEventPublisher>();
    }

    [Fact]
    public void AddConsumer_RegistersTheHostedService()
    {
        var services = new ServiceCollection();

        services.AddAzureServiceBusIntegrationEventConsumer(
            Map, o => o.Subscribe(OrderPlaced.WireName, "billing"));

        WithInfrastructure(services).BuildServiceProvider().GetServices<IHostedService>()
            .Should().ContainSingle(s => s is ServiceBusInboxConsumer);
    }

    [Fact]
    public void AddConsumer_CalledTwice_RunsOneConsumerCarryingBothCallersSubscriptions()
    {
        var services = new ServiceCollection();

        services.AddAzureServiceBusIntegrationEventConsumer(Map, static o => o.Subscribe("topic-a", "s"));
        services.AddAzureServiceBusIntegrationEventConsumer(Map, static o => o.Subscribe("topic-b", "s"));

        var provider = WithInfrastructure(services).BuildServiceProvider();

        // Two hosted services would each open a receiver on every configured subscription, doubling delivery.
        provider.GetServices<IHostedService>().Should().ContainSingle(s => s is ServiceBusInboxConsumer);
        provider.GetRequiredService<IOptions<AzureServiceBusConsumerOptions>>().Value.Subscriptions
            .Select(s => s.TopicName).Should().BeEquivalentTo(["topic-a", "topic-b"]);
    }

    [Fact]
    public void AddConsumer_WithoutSubscriptions_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddAzureServiceBusIntegrationEventConsumer(Map, static _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*no subscriptions*");
    }

    [Fact]
    public void AddConsumer_WithDuplicateSubscription_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddAzureServiceBusIntegrationEventConsumer(
            Map, static o => o.Subscribe("t", "s").Subscribe("t", "s"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than once*");
    }

    [Fact]
    public void AddConsumer_WithZeroConcurrency_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddAzureServiceBusIntegrationEventConsumer(
            Map, static o => o.Subscribe("t", "s").MaxConcurrentCalls = 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxConcurrentCalls*");
    }

    [Fact]
    public void AddConsumer_WithNegativePrefetch_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddAzureServiceBusIntegrationEventConsumer(
            Map, static o => o.Subscribe("t", "s").PrefetchCount = -1);

        act.Should().Throw<InvalidOperationException>().WithMessage("*PrefetchCount*");
    }

    [Fact]
    public void Subscription_WithBlankNames_Throws()
    {
        var blankTopic = () => new ServiceBusSubscription(" ", "s");
        var blankSubscription = () => new ServiceBusSubscription("t", " ");

        blankTopic.Should().Throw<ArgumentException>();
        blankSubscription.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Publisher_WithAnUnmappedEventType_ThrowsRatherThanPublishingAnUnidentifiableMessage()
    {
        var publisher = new ServiceBusIntegrationEventPublisher(
            new ServiceBusClient("Endpoint=sb://localhost;SharedAccessKeyName=k;SharedAccessKey=v;UseDevelopmentEmulator=true;"),
            IntegrationEventNameMap.Empty,
            Microsoft.Extensions.Options.Options.Create(new AzureServiceBusPublisherOptions()),
            NullLogger<ServiceBusIntegrationEventPublisher>.Instance);

        var act = async () => await publisher.PublishAsync(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), new OrderPlaced("ORD-1", DateTimeOffset.UnixEpoch)),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*IntegrationEventName*");
    }

    private sealed class NoOpPublisher : IIntegrationEventPublisher
    {
        public ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private const string OfflineConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=k;SharedAccessKey=v;UseDevelopmentEmulator=true;";

    private static IServiceCollection WithInfrastructure(IServiceCollection services)
    {
        services.AddSingleton(new ServiceBusClient(OfflineConnectionString));
        services.AddLogging();
        return services;
    }

    private static T Resolve<T>(IServiceCollection services)
        where T : notnull =>
        WithInfrastructure(services).BuildServiceProvider().GetRequiredService<T>();

    private sealed class TopicResolverReached : Exception
    {
        public TopicResolverReached() { }

        public TopicResolverReached(string message) : base(message) { }

        public TopicResolverReached(string message, Exception innerException) : base(message, innerException) { }
    }

    [Fact]
    public async Task AddPublisher_UsesTheMapItWasGiven_EvenWhenAConsumerRegisteredADifferentOneFirst()
    {
        var services = new ServiceCollection();

        // A consumer that models no contracts at all, registered first.
        services.AddAzureServiceBusIntegrationEventConsumer(
            IntegrationEventNameMap.Empty, static o => o.Subscribe("t", "s"));

        // The publisher knows OrderPlaced. Sharing one map through the container would let the
        // consumer's empty map win and make this publisher reject an event it can name.
        services.AddAzureServiceBusIntegrationEventPublisher(
            Map, static o => o.TopicNameResolver = _ => throw new TopicResolverReached());

        var publisher = Resolve<IIntegrationEventPublisher>(services);

        var act = async () => await publisher.PublishAsync(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), new OrderPlaced("ORD-1", DateTimeOffset.UnixEpoch)),
            TestContext.Current.CancellationToken);

        // Reaching the topic resolver proves the wire name resolved against the publisher's own map.
        await act.Should().ThrowAsync<TopicResolverReached>();
    }

    [Fact]
    public void Registration_DoesNotPublishTheNameMapAsASharedService()
    {
        var services = new ServiceCollection();

        services.AddAzureServiceBusIntegrationEventPublisher(Map);
        services.AddAzureServiceBusIntegrationEventConsumer(
            IntegrationEventNameMap.Empty, static o => o.Subscribe("t", "s"));

        // Each component owns the map it was registered with. A shared registration would make the
        // first caller win and silently discard the second caller's argument.
        services.Should().NotContain(d => d.ServiceType == typeof(IntegrationEventNameMap));
    }

    [Fact]
    public async Task Publisher_AfterDisposal_RefusesToPublishRatherThanCachingASenderNobodyWillClose()
    {
        var publisher = new ServiceBusIntegrationEventPublisher(
            new ServiceBusClient(OfflineConnectionString),
            Map,
            Microsoft.Extensions.Options.Options.Create(new AzureServiceBusPublisherOptions()),
            NullLogger<ServiceBusIntegrationEventPublisher>.Instance);

        await publisher.DisposeAsync();

        var act = async () => await publisher.PublishAsync(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), new OrderPlaced("ORD-1", DateTimeOffset.UnixEpoch)),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
