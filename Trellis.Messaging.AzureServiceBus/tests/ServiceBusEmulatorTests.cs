namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Net.Sockets;
using Azure.Messaging.ServiceBus;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Moq;

public sealed class ServiceBusEmulatorTests
{
    private const string Connection =
        "Endpoint=sb://127.0.0.1:49152;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(null, "localhost", 5672)]
    [InlineData("sb://broker.example", "broker.example", 5672)]
    [InlineData("sb://broker.example:5672", "broker.example", 5672)]
    [InlineData("sb://127.0.0.1:49152", "127.0.0.1", 49152)]
    public async Task ProbeAsync_EndpointSelection_Unavailable_UsesSelectedPortAndFailurePolicy(
        string? endpoint, string host, int port)
    {
        var connection = endpoint is null
            ? null
            : $"Endpoint={endpoint};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
        Exception failure = port == 49152
            ? new OperationCanceledException("Probe cancelled.")
            : new SocketException((int)SocketError.ConnectionRefused);
        var attempts = 0;
        ValueTask Connect(string actualHost, int actualPort, CancellationToken token)
        {
            attempts++;
            actualHost.Should().Be(host);
            actualPort.Should().Be(port);
            token.CanBeCanceled.Should().BeTrue();
            return ValueTask.FromException(failure);
        }

        if (endpoint is null)
            (await ServiceBusEmulator.ProbeAsync(connection, Connect)).Should().BeNull();
        else
        {
            Func<Task> probe = async () => _ = await ServiceBusEmulator.ProbeAsync(connection, Connect);
            var thrown = await probe.Should().ThrowAsync<Exception>();
            thrown.Which.Should().BeSameAs(failure);
        }

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task TryGetClientAsync_DefaultBackend_UsesNativeProbe_DoesNotCreateContainers()
    {
        var nativeCalls = 0;
        await using var fixture = new ServiceBusEmulator(false,
            () => throw new InvalidOperationException("Docker must not be accessed."),
            () =>
            {
                nativeCalls++;
                return Task.FromResult<ServiceBusClient?>(null);
            });

        await fixture.InitializeAsync();

        (await fixture.TryGetClientAsync()).Should().BeNull();
        (await fixture.TryGetClientAsync()).Should().BeNull();
        nativeCalls.Should().Be(1);
    }

    [Fact]
    public async Task TryGetClientAsync_Testcontainers_MultipleCalls_SharesOwnedLifecycle()
    {
        var resources = new Resources();
        var factoryCalls = 0;
        ServiceBusClient? client = null;
        resources.Broker.Setup(c => c.DisposeAsync()).Callback(() =>
        {
            (client?.IsClosed).Should().BeTrue();
            resources.CleanupOrder.Add("broker");
        }).Returns(ValueTask.CompletedTask);

        await using (var fixture = new ServiceBusEmulator(true, () =>
        {
            factoryCalls++;
            return resources.CreateFixture();
        }))
        {
            await fixture.InitializeAsync();
            factoryCalls.Should().Be(0);
            client = await fixture.TryGetClientAsync();
            client.Should().NotBeNull();
            (await fixture.TryGetClientAsync()).Should().BeSameAs(client);
            factoryCalls.Should().Be(1);
            resources.Broker.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
            resources.StartupToken.CanBeCanceled.Should().BeTrue();
            resources.ConnectionRequests.Should().Be(1);
        }

        resources.CleanupOrder.Should().Equal(["broker", "sql", "network"]);
    }

    [Fact]
    public async Task DisposeAsync_FailedStartup_PendingCleanup_AwaitsAllOwnedResources()
    {
        var resources = new Resources();
        var failure = new InvalidOperationException("Container startup failed.");
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        resources.Broker.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        resources.Broker.Setup(c => c.DisposeAsync()).Callback(() =>
        {
            resources.CleanupOrder.Add("broker");
            cleanupEntered.SetResult();
        }).Returns(() => new ValueTask(releaseCleanup.Task));
        var fixture = new ServiceBusEmulator(true, resources.CreateFixture);
        Task? cleanup = null;

        try
        {
            Func<Task> start = async () => _ = await fixture.TryGetClientAsync();
            var thrown = await start.Should().ThrowExactlyAsync<InvalidOperationException>();
            thrown.Which.Should().BeSameAs(failure);
            resources.ConnectionRequests.Should().Be(0);

            cleanup = fixture.DisposeAsync().AsTask();
            await cleanupEntered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            cleanup.IsCompleted.Should().BeFalse();
            resources.Database.Verify(c => c.DisposeAsync(), Times.Never);
            resources.Network.Verify(n => n.DisposeAsync(), Times.Never);
        }
        finally
        {
            releaseCleanup.TrySetResult();
            await (cleanup ?? fixture.DisposeAsync().AsTask()).WaitAsync(Patience, CancellationToken.None);
        }

        resources.CleanupOrder.Should().Equal(["broker", "sql", "network"]);
    }

    [Fact]
    public async Task DisposeAsync_ResourceFailures_CleansRemainingResources_PreservesFailures()
    {
        var resources = new Resources();
        var brokerFailure = new InvalidOperationException("Broker cleanup failed.");
        var databaseFailure = new InvalidOperationException("SQL cleanup failed.");
        resources.Broker.Setup(c => c.DisposeAsync()).Callback(() => resources.CleanupOrder.Add("broker"))
            .Returns(() => ValueTask.FromException(brokerFailure));
        resources.Database.Setup(c => c.DisposeAsync()).Callback(() => resources.CleanupOrder.Add("sql"))
            .Returns(() => ValueTask.FromException(databaseFailure));
        var fixture = new ServiceBusEmulator(true, resources.CreateFixture);
        var client = await fixture.TryGetClientAsync();
        Func<Task> dispose = () => fixture.DisposeAsync().AsTask();

        var thrown = await dispose.Should().ThrowExactlyAsync<AggregateException>();

        thrown.Which.InnerExceptions.Should().Equal([brokerFailure, databaseFailure]);
        (client?.IsClosed).Should().BeTrue();
        resources.CleanupOrder.Should().Equal(["broker", "sql", "network"]);
    }

    private sealed class Resources
    {
        internal Mock<IContainer> Broker { get; } = new(MockBehavior.Strict);
        internal Mock<IContainer> Database { get; } = new(MockBehavior.Strict);
        internal Mock<INetwork> Network { get; } = new(MockBehavior.Strict);
        internal List<string> CleanupOrder { get; } = [];
        internal CancellationToken StartupToken { get; private set; }
        internal int ConnectionRequests { get; private set; }

        internal Resources()
        {
            Broker.Setup(c => c.StartAsync(It.IsAny<CancellationToken>()))
                .Callback<CancellationToken>(token => StartupToken = token)
                .Returns(Task.CompletedTask);
            Broker.Setup(c => c.DisposeAsync()).Callback(() => CleanupOrder.Add("broker"))
                .Returns(ValueTask.CompletedTask);
            Database.Setup(c => c.DisposeAsync()).Callback(() => CleanupOrder.Add("sql"))
                .Returns(ValueTask.CompletedTask);
            Network.Setup(n => n.DisposeAsync()).Callback(() => CleanupOrder.Add("network"))
                .Returns(ValueTask.CompletedTask);
        }

        internal ServiceBusTestcontainersFixture CreateFixture() =>
            new(Broker.Object, Database.Object, Network.Object, () =>
            {
                ConnectionRequests++;
                return Connection;
            });
    }
}