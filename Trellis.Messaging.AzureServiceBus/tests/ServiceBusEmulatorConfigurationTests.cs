namespace Trellis.Messaging.AzureServiceBus.Tests;

[CollectionDefinition(nameof(ServiceBusEmulatorEnvironmentState), DisableParallelization = true)]
public sealed class ServiceBusEmulatorEnvironmentState;

[Collection(nameof(ServiceBusEmulatorEnvironmentState))]
public sealed class ServiceBusEmulatorConfigurationTests
{
    [Theory]
    [InlineData("unsupported", null)]
    [InlineData("testcontainers", "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;")]
    public async Task Constructor_BackendSelection_InvalidConfiguration_Throws(
        string backend, string? connection)
    {
        var previousBackend = Environment.GetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS");
        var previousConnection = Environment.GetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS_CONNECTION");

        try
        {
            Environment.SetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS", backend);
            Environment.SetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS_CONNECTION", connection);
            Func<Task> create = async () =>
            {
                await using var fixture = new ServiceBusEmulator();
                await fixture.InitializeAsync();
            };

            var failure = await create.Should().ThrowExactlyAsync<InvalidOperationException>();
            failure.Which.Message.Should().Contain("TRELLIS_TEST_SERVICEBUS");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS", previousBackend);
            Environment.SetEnvironmentVariable("TRELLIS_TEST_SERVICEBUS_CONNECTION", previousConnection);
        }
    }
}