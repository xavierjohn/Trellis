namespace Trellis.Asp.Idempotency.Cosmos.Tests;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Trellis.TestSupport;

public sealed class CosmosEmulatorTests
{
    [Fact]
    public async Task ProvisionAsync_ExplicitEndpoint_ProvisionFailure_PropagatesWithoutFallback()
    {
        const string endpoint = "https://127.0.0.1:49152/";
        var failure = new HttpRequestException("The configured Cosmos endpoint is unavailable.");
        Uri? selectedEndpoint = null;
        CosmosClientOptions? selectedOptions = null;
        var attempts = 0;

        Func<Task> provision = async () => _ = await CosmosEmulator.ProvisionAsync(
            CosmosEnvironment(endpoint),
            (client, _) =>
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

    [Fact]
    public async Task ProvisionAsync_DefaultEndpoint_Unreachable_ReturnsNullSoTheSuiteSkips()
    {
        Uri? selectedEndpoint = null;
        CosmosClientOptions? selectedOptions = null;

        var container = await CosmosEmulator.ProvisionAsync(
            CosmosEnvironment(null),
            (client, _) =>
            {
                selectedEndpoint = client.Endpoint;
                selectedOptions = client.ClientOptions;
                return Task.FromException<Container>(new HttpRequestException("No emulator is listening."));
            });

        container.Should().BeNull();
        selectedEndpoint.Should().Be(new Uri("https://localhost:8081/"));
        (selectedOptions?.LimitToEndpoint).Should().BeFalse();
    }

    private static Func<string, string?> CosmosEnvironment(string? endpoint) =>
        FakeEnvironment.With(("TRELLIS_TEST_COSMOS_ENDPOINT", endpoint));
}