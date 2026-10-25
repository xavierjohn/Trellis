namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Net.Sockets;
using Azure.Messaging.ServiceBus;

/// <summary>
/// Owns the shared Service Bus client for the transport integration test class.
/// </summary>
/// <remarks>
/// <para>
/// The emulator is not present on every machine or CI agent, so probing is a first-class outcome rather
/// than a failure: <see cref="TryGetClientAsync"/> returns <c>null</c> when nothing is listening and the
/// tests skip visibly. The suite either runs against a real broker or is seen not to run — it never passes
/// against a substitute.
/// </para>
/// <para>An explicitly configured connection is required to work; probe failures propagate.</para>
/// <para>Explicit Testcontainers selection owns a broker and its SQL backend; startup failures propagate.</para>
/// <para>
/// Entities are declared in <c>emulator/Config.json</c> and cannot be created at runtime, so
/// <see cref="Topic"/> and the subscription names below must match that file.
/// </para>
/// </remarks>
public sealed class ServiceBusEmulator : IAsyncLifetime
{
    /// <summary>
    /// The well-known emulator connection string. Published by Microsoft and identical on every
    /// installation, so this is a fixed test constant rather than a secret.
    /// </summary>
    private const string ConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private const int AmqpPort = 5672;

    /// <summary>The topic declared in <c>emulator/Config.json</c>, named after the event's wire name.</summary>
    public const string Topic = OrderPlaced.WireName;

    private readonly Lazy<Task<ServiceBusClient?>> _client;
    private readonly Func<ServiceBusTestcontainersFixture> _createFixture = () => new();
    private ServiceBusTestcontainersFixture? _ownedFixture;

    /// <summary>Creates the shared emulator client fixture.</summary>
    public ServiceBusEmulator() : this(ReadEnabled(), () => new())
    {
    }

    internal ServiceBusEmulator(Func<Task<ServiceBusClient?>> probe) => _client = new(probe);

    internal ServiceBusEmulator(
        bool useTestcontainers,
        Func<ServiceBusTestcontainersFixture> createFixture,
        Func<Task<ServiceBusClient?>>? nativeProbe = null)
    {
        _createFixture = createFixture;
        _client = useTestcontainers
            ? new(CreateOwnedClientAsync)
            : new(nativeProbe ?? ProbeAsync);
    }

    private static bool ReadEnabled()
    {
        var backend = Environment.GetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS");
        if (string.IsNullOrWhiteSpace(backend))
            return false;

        if (backend != "testcontainers")
            throw new InvalidOperationException(
                "TRELLIS_TEST_SERVICEBUS must be unset or 'testcontainers'.");

        if (!string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS_CONNECTION")))
            throw new InvalidOperationException(
                "Do not combine TRELLIS_TEST_SERVICEBUS=testcontainers with TRELLIS_TEST_SERVICEBUS_CONNECTION.");

        return true;
    }

    private async Task<ServiceBusClient?> CreateOwnedClientAsync()
    {
        _ownedFixture = _createFixture();
        await _ownedFixture.InitializeAsync();
        return _ownedFixture.Client;
    }

    /// <summary>Returns a shared client, or <c>null</c> when the default emulator is unreachable.</summary>
    public Task<ServiceBusClient?> TryGetClientAsync() => _client.Value;

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_ownedFixture is not null)
        {
            await _ownedFixture.DisposeAsync();
            return;
        }

        await DisposeNativeClientAsync().WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private async Task DisposeNativeClientAsync()
    {
        if (!_client.IsValueCreated)
            return;

        var clientTask = _client.Value;
        // Failed probes have no client to close; their callers already observe the failure.
        if (clientTask.IsFaulted || clientTask.IsCanceled)
            return;

        var client = await clientTask;
        if (client is not null)
            await client.DisposeAsync();
    }

    private static async Task<ServiceBusClient?> ProbeAsync()
    {
        using var probe = new TcpClient();
        var configuredConnection = Environment.GetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS_CONNECTION");
        return await ProbeAsync(configuredConnection, probe.ConnectAsync);
    }

    internal static async Task<ServiceBusClient?> ProbeAsync(
        string? configuredConnection,
        Func<string, int, CancellationToken, ValueTask> connect)
    {
        var connectionString = string.IsNullOrWhiteSpace(configuredConnection)
            ? ConnectionString
            : configuredConnection;
        var endpoint = ServiceBusConnectionStringProperties.Parse(connectionString).Endpoint
            ?? throw new InvalidOperationException("The emulator connection string must include an Endpoint.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await connect(endpoint.Host, endpoint.IsDefaultPort ? AmqpPort : endpoint.Port, timeout.Token);
        }
        catch (Exception ex) when (string.IsNullOrWhiteSpace(configuredConnection)
            && (ex is SocketException or OperationCanceledException))
        {
            // Narrow by design: these are the shapes "no emulator here" takes. Anything else is a real
            // defect and must surface rather than silently skipping the whole suite.
            return null;
        }

        return new ServiceBusClient(connectionString);
    }
}