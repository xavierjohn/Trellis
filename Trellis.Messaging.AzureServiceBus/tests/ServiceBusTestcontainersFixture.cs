namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Runtime.ExceptionServices;
using Azure.Messaging.ServiceBus;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;
using Trellis.TestSupport;

internal sealed class ServiceBusTestcontainersFixture : IAsyncLifetime
{
    // Both images are pinned by digest so a registry-side change cannot alter the required check. To move to a newer
    // build of the same tag, refresh the digest: docker buildx imagetools inspect <tag> --format '{{.Manifest.Digest}}'
    private const string BrokerImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1@sha256:5a96d893b245031740f7d46e0fe5ff282d24b78c4b7d761dd57590f3f010a9b3";
    private const string DatabaseImage = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";
    private const string OwnerLabel = "trellis.testcontainers-fixture";
    private const string Owner = "servicebus-integration";
    private readonly ContainerLogs _logs = new("servicebus-integration", "servicebus");
    private IContainer? _broker;
    private IContainer? _database;
    private INetwork? _network;
    private Func<string>? _getConnectionString;
    private ServiceBusClient? _client;

    internal ServiceBusTestcontainersFixture()
    {
    }

    internal ServiceBusTestcontainersFixture(
        IContainer broker, IContainer database, INetwork network, Func<string> getConnectionString)
    {
        _broker = broker;
        _database = database;
        _network = network;
        _getConnectionString = getConnectionString;
    }

    internal ServiceBusClient Client =>
        _client ?? throw new InvalidOperationException("The Service Bus container is not initialized.");

    public async ValueTask InitializeAsync()
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        startup.CancelAfter(ContainerFixtureSupport.StartupTimeout);
        if (_broker is null)
            CreateContainers();

        var broker = _broker ?? throw new InvalidOperationException("The Service Bus container is not configured.");
        var getConnectionString = _getConnectionString
            ?? throw new InvalidOperationException("The Service Bus connection is not configured.");
        await broker.StartAsync(startup.Token);
        _client = new ServiceBusClient(getConnectionString());
    }

    public ValueTask DisposeAsync() => ContainerFixtureSupport.DisposeWithinAsync(
        DisposeResourcesAsync(), _logs, ContainerFixtureSupport.TeardownTimeout);

    private async Task DisposeResourcesAsync()
    {
        List<Exception> failures = [];
        IAsyncDisposable?[] resources = [_client, _broker, _database, _network];
        foreach (var resource in resources)
        {
            if (resource is null)
                continue;

            try
            {
                await resource.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Service Bus fixture cleanup failed.", failures);
    }

    private void CreateContainers()
    {
        _network = new NetworkBuilder()
            .WithLabel(OwnerLabel, Owner)
            .Build();
        var password = ContainerFixtureSupport.NewSqlPassword();
        var database = new MsSqlBuilder(DatabaseImage)
            .WithPassword(password)
            .WithNetwork(_network)
            .WithNetworkAliases("broker-sql")
            .WithLabel(OwnerLabel, Owner)
            .WithOutputConsumer(_logs.Create("sql"))
            .WithCreateParameterModifier(BindToLoopback)
            .Build();
        _database = database;
        var broker = new ServiceBusBuilder(BrokerImage)
            .WithAcceptLicenseAgreement(true)
            .WithMsSqlContainer(_network, database, "broker-sql", password)
            .WithConfig(Path.Combine(AppContext.BaseDirectory, "emulator", "Config.json"))
            .WithLabel(OwnerLabel, Owner)
            .WithOutputConsumer(_logs.Create("broker"))
            .WithCreateParameterModifier(BindToLoopback)
            .Build();
        _broker = broker;
        _getConnectionString = broker.GetConnectionString;
    }

    private static void BindToLoopback(CreateContainerParameters parameters)
    {
        var portBindings = parameters.HostConfig?.PortBindings
            ?? throw new InvalidOperationException("The Service Bus fixture requires explicit published ports.");
        foreach (var bindings in portBindings.Values)
            foreach (var binding in bindings)
                binding.HostIP = "127.0.0.1";
    }
}