namespace Trellis.Asp.Idempotency.Cosmos.Tests;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.Cosmos;

[CollectionDefinition(nameof(CosmosEmulatorEnvironmentState), DisableParallelization = true)]
public sealed class CosmosEmulatorEnvironmentState;

[Collection(nameof(CosmosEmulatorEnvironmentState))]
public sealed class CosmosEmulatorTests
{
    [Fact]
    public async Task ProvisionAsync_ExplicitEndpoint_ProvisionFailure_PropagatesWithoutFallback()
    {
        const string endpoint = "https://127.0.0.1:49152/";
        var previousEndpoint = Environment.GetEnvironmentVariable("TRELLIS_TEST_COSMOS_ENDPOINT");
        var failure = new HttpRequestException("The configured Cosmos endpoint is unavailable.");
        Uri? selectedEndpoint = null;
        CosmosClientOptions? selectedOptions = null;
        var attempts = 0;

        try
        {
            Environment.SetEnvironmentVariable("TRELLIS_TEST_COSMOS_ENDPOINT", endpoint);
            Func<Task> provision = async () => _ = await CosmosEmulator.ProvisionAsync((client, _) =>
            {
                selectedEndpoint = client.Endpoint;
                selectedOptions = client.ClientOptions;
                attempts++;
                return Task.FromException<Container>(failure);
            });

            var thrown = await provision.Should().ThrowExactlyAsync<HttpRequestException>();

            thrown.Which.Should().BeSameAs(failure);
            selectedEndpoint.Should().Be(new Uri(endpoint));
            (selectedOptions?.LimitToEndpoint).Should().BeTrue();
            (selectedOptions?.ConnectionMode).Should().Be(ConnectionMode.Gateway);
            attempts.Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRELLIS_TEST_COSMOS_ENDPOINT", previousEndpoint);
        }
    }
}