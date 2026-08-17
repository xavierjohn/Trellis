namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Net.Sockets;
using Azure.Messaging.ServiceBus;

/// <summary>
/// Resolves the Service Bus client the transport integration tests run against, once per test process.
/// </summary>
/// <remarks>
/// <para>
/// The emulator is not present on every machine or CI agent, so probing is a first-class outcome rather
/// than a failure: <see cref="TryGetClientAsync"/> returns <c>null</c> when nothing is listening and the
/// tests skip visibly. The suite either runs against a real broker or is seen not to run — it never passes
/// against a substitute.
/// </para>
/// <para>
/// Entities are declared in <c>emulator/Config.json</c> and cannot be created at runtime, so
/// <see cref="Topic"/> and the subscription names below must match that file.
/// </para>
/// </remarks>
internal static class ServiceBusEmulator
{
    /// <summary>
    /// The well-known emulator connection string. Published by Microsoft and identical on every
    /// installation, so this is a fixed test constant rather than a secret.
    /// </summary>
    private const string ConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private const string Host = "localhost";
    private const int AmqpPort = 5672;

    /// <summary>The topic declared in <c>emulator/Config.json</c>, named after the event's wire name.</summary>
    public const string Topic = OrderPlaced.WireName;

    private static readonly Lazy<Task<ServiceBusClient?>> Client = new(ProbeAsync);

    /// <summary>Returns a shared client, or <c>null</c> when no emulator is reachable.</summary>
    public static Task<ServiceBusClient?> TryGetClientAsync() => Client.Value;

    private static async Task<ServiceBusClient?> ProbeAsync()
    {
        using var probe = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await probe.ConnectAsync(Host, AmqpPort, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            // Narrow by design: these are the shapes "no emulator here" takes. Anything else is a real
            // defect and must surface rather than silently skipping the whole suite.
            return null;
        }

        return new ServiceBusClient(ConnectionString);
    }
}
