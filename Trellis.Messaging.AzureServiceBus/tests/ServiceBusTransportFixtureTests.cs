namespace Trellis.Messaging.AzureServiceBus.Tests;

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;
using ConsumerHost = ServiceBusTransportIntegrationTests.ConsumerHost;
using ControlledClient = ServiceBusInboxConsumerLifecycleTests.ControlledClient;
using ControlledProcessor = ServiceBusInboxConsumerLifecycleTests.ControlledProcessor;

public sealed class ServiceBusTransportFixtureTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DisposeAsync_DuringStartup_AwaitsProcessorShutdownBeforeProviderDisposal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var processor = new ControlledProcessor(blockStart: true, blockStop: true);
        var (host, provider, disposal) = BuildHost(new ControlledClient(processor));
        await using var ownedProvider = provider;
        Task? shutdown = null;

        try
        {
            await host.StartAsync(cancellationToken);
            await processor.Started.Task.WaitAsync(Patience, cancellationToken);

            shutdown = host.DisposeAsync().AsTask();
            shutdown.IsCompleted.Should().BeFalse();
            processor.StopCalls.Should().Be(0);
            disposal.Calls.Should().Be(0);

            processor.ReleaseStart();
            await processor.Stopping.Task.WaitAsync(Patience, cancellationToken);

            shutdown.IsCompleted.Should().BeFalse();
            processor.CloseCalls.Should().Be(0);
            disposal.Calls.Should().Be(0);
        }
        finally
        {
            processor.ReleaseStart();
            processor.ReleaseStop();
            await (shutdown ?? host.DisposeAsync().AsTask()).WaitAsync(Patience, CancellationToken.None);
        }

        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
        disposal.Calls.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_StopFailure_DisposesProviderAndPropagatesFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stopFailure = new InvalidOperationException("shutdown failed");
        var processor = new ControlledProcessor { StopFailure = stopFailure };
        var (host, provider, disposal) = BuildHost(new ControlledClient(processor));
        await using var ownedProvider = provider;
        await host.StartAsync(cancellationToken);
        await processor.Started.Task.WaitAsync(Patience, cancellationToken);
        var dispose = async () => await host.DisposeAsync();

        var failure = await dispose.Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Should().BeSameAs(stopFailure);
        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
        disposal.Calls.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_SharedClient_RemainsOpenUntilFixtureTeardown()
    {
        typeof(IClassFixture<ServiceBusEmulator>).IsAssignableFrom(typeof(ServiceBusTransportIntegrationTests))
            .Should().BeTrue();

        var cancellationToken = TestContext.Current.CancellationToken;
        var firstProcessor = new ControlledProcessor();
        var secondProcessor = new ControlledProcessor();
        var client = new ControlledClient(firstProcessor, secondProcessor);

        await using (var emulator = new ServiceBusEmulator(() => Task.FromResult<ServiceBusClient?>(client)))
        {
            (await emulator.TryGetClientAsync()).Should().BeSameAs(client);
            await using (var firstHost = ServiceBusTransportIntegrationTests.BuildConsumer(
                client, "roundtrip", new ServiceBusTransportIntegrationTests.RecordingInboxDispatcher()))
            {
                await firstHost.StartAsync(cancellationToken);
                await firstProcessor.Started.Task.WaitAsync(Patience, cancellationToken);
            }

            client.DisposeCalls.Should().Be(0);
            (await emulator.TryGetClientAsync()).Should().BeSameAs(client);

            await using (var secondHost = ServiceBusTransportIntegrationTests.BuildConsumer(
                client, "roundtrip", new ServiceBusTransportIntegrationTests.RecordingInboxDispatcher()))
            {
                await secondHost.StartAsync(cancellationToken);
                await secondProcessor.Started.Task.WaitAsync(Patience, cancellationToken);
            }

            client.DisposeCalls.Should().Be(0);
        }

        client.DisposeCalls.Should().Be(1);
    }

    private static (ConsumerHost Host, ServiceProvider Provider, ProviderDisposal Disposal) BuildHost(ControlledClient client)
    {
        var disposal = new ProviderDisposal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ServiceBusClient>(client);
        services.AddSingleton(_ => disposal);
        services.AddAzureServiceBusIntegrationEventConsumer(
            IntegrationEventNameMap.Empty, options => options.Subscribe(ServiceBusEmulator.Topic, "roundtrip"));

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ProviderDisposal>();
        return (new ConsumerHost(provider), provider, disposal);
    }

    private sealed class ProviderDisposal : IDisposable
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Dispose() => Interlocked.Increment(ref _calls);
    }
}