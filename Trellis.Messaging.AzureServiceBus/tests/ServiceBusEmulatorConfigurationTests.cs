namespace Trellis.Messaging.AzureServiceBus.Tests;

using Trellis.TestSupport;

public sealed class ServiceBusEmulatorConfigurationTests
{
    private const string NativeConnection =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    [Theory]
    [InlineData("unsupported", null)]
    [InlineData("testcontainers", NativeConnection)]
    public void Constructor_BackendSelection_InvalidConfiguration_Throws(string backend, string? connection)
    {
        var environment = ServiceBusEnvironment(backend, connection);

        var create = () => new ServiceBusEmulator(environment);

        create.Should().ThrowExactly<InvalidOperationException>()
            .Which.Message.Should().Contain("TRELLIS_TEST_SERVICEBUS");
    }

    [Fact]
    public async Task TryGetClientAsync_BackendUnset_UsesNativeProbe_DoesNotCreateContainers()
    {
        var fixtureCalls = 0;
        await using var emulator = new ServiceBusEmulator(
            ServiceBusEnvironment(null, null),
            () =>
            {
                fixtureCalls++;
                throw new InvalidOperationException("Docker must not be accessed.");
            },
            () => Task.FromResult<Azure.Messaging.ServiceBus.ServiceBusClient?>(null));

        var client = await emulator.TryGetClientAsync();

        client.Should().BeNull();
        fixtureCalls.Should().Be(0);
    }

    [Fact]
    public async Task TryGetClientAsync_BackendTestcontainers_CreatesOwnedFixture()
    {
        var sentinel = new InvalidOperationException("owned fixture requested");
        await using var emulator = new ServiceBusEmulator(
            ServiceBusEnvironment("testcontainers", null),
            () => throw sentinel,
            () => throw new InvalidOperationException("The native probe must not run."));

        var attempt = async () => await emulator.TryGetClientAsync();

        (await attempt.Should().ThrowExactlyAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(sentinel);
    }

    private static Func<string, string?> ServiceBusEnvironment(string? backend, string? connection) =>
        FakeEnvironment.With(
            ("TRELLIS_TEST_SERVICEBUS", backend),
            ("TRELLIS_TEST_SERVICEBUS_CONNECTION", connection));
}
