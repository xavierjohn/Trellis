namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Runtime.ExceptionServices;
using Azure.Messaging.ServiceBus;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;

internal sealed class ServiceBusTestcontainersFixture : IAsyncLifetime
{
    private const string BrokerImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1";
    private const string DatabaseImage = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04";
    private const string OwnerLabel = "trellis.testcontainers-fixture";
    private const string Owner = "servicebus-integration";
    private readonly List<IOutputConsumer> _outputs = [];
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
        startup.CancelAfter(TimeSpan.FromMinutes(5));
        if (_broker is null)
            CreateContainers();

        var broker = _broker ?? throw new InvalidOperationException("The Service Bus container is not configured.");
        var getConnectionString = _getConnectionString
            ?? throw new InvalidOperationException("The Service Bus connection is not configured.");
        await broker.StartAsync(startup.Token);
        _client = new ServiceBusClient(getConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        var cleanup = DisposeResourcesAsync();
        try
        {
            await cleanup.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        finally
        {
            // An in-flight teardown still owns the output streams.
            if (cleanup.IsCompletedSuccessfully)
                foreach (var output in _outputs)
                    output.Dispose();
        }
    }

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
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "servicebus-integration");
        Directory.CreateDirectory(logDirectory);
        var instance = Guid.NewGuid().ToString("N");
        _network = new NetworkBuilder()
            .WithLabel(OwnerLabel, Owner)
            .Build();
        var password = $"{Guid.NewGuid():N}aA1!";
        var database = new MsSqlBuilder(DatabaseImage)
            .WithPassword(password)
            .WithNetwork(_network)
            .WithNetworkAliases("broker-sql")
            .WithLabel(OwnerLabel, Owner)
            .WithOutputConsumer(CreateOutput(logDirectory, instance, "sql"))
            .WithCreateParameterModifier(BindToLoopback)
            .Build();
        _database = database;
        var broker = new ServiceBusBuilder(BrokerImage)
            .WithAcceptLicenseAgreement(true)
            .WithMsSqlContainer(_network, database, "broker-sql", password)
            .WithConfig(Path.Combine(AppContext.BaseDirectory, "emulator", "Config.json"))
            .WithLabel(OwnerLabel, Owner)
            .WithOutputConsumer(CreateOutput(logDirectory, instance, "broker"))
            .WithCreateParameterModifier(BindToLoopback)
            .Build();
        _broker = broker;
        _getConnectionString = broker.GetConnectionString;
    }

    private IOutputConsumer CreateOutput(string directory, string instance, string resource)
    {
        var stream = Stream.Synchronized(File.Create(Path.Combine(directory, $"servicebus-{instance}-{resource}.log")));
        var output = Consume.RedirectStdoutAndStderrToStream(stream, stream);
        _outputs.Add(output);
        return output;
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