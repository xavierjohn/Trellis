namespace Trellis.Asp.Idempotency.Cosmos.Tests;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Resolves the container the Cosmos DB tests run against, once per test process.
/// </summary>
/// <remarks>
/// The emulator is not present on every machine or CI agent, so probing is a first-class outcome
/// rather than a failure: <see cref="TryGetContainerAsync"/> returns <c>null</c> when the emulator
/// cannot be reached and the tests skip. That keeps the suite honest — it either runs against real
/// Cosmos DB or visibly does not run at all, rather than passing against a substitute.
/// </remarks>
internal static class CosmosEmulator
{
    /// <summary>
    /// Well-known Cosmos DB emulator credentials. Published by Microsoft and identical on every
    /// installation, so this is a fixed test constant rather than a secret.
    /// </summary>
    private const string Endpoint = "https://localhost:8081/";

    private const string Key =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private const string DatabaseId = "trellis-conformance";

    private static readonly Lazy<Task<Container?>> Container = new(ProvisionAsync);

    /// <summary>
    /// Returns the shared idempotency container, or <c>null</c> when no emulator is reachable.
    /// </summary>
    public static Task<Container?> TryGetContainerAsync() => Container.Value;

    private static async Task<Container?> ProvisionAsync()
    {
        var client = new CosmosClient(Endpoint, Key, new CosmosClientOptions
        {
            // The emulator presents a self-signed certificate, and Gateway mode keeps the test to
            // the single HTTPS port that was probed.
            ConnectionMode = ConnectionMode.Gateway,
            ServerCertificateCustomValidationCallback = (_, _, _) => true,
            RequestTimeout = TimeSpan.FromSeconds(10),
            MaxRetryAttemptsOnRateLimitedRequests = 0,
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var database = await client.CreateDatabaseIfNotExistsAsync(
                DatabaseId, cancellationToken: timeout.Token);

            return await CosmosIdempotencyContainer.CreateIfNotExistsAsync(
                database.Database, cancellationToken: timeout.Token);
        }
        catch (Exception ex) when (ex is CosmosException or HttpRequestException or OperationCanceledException)
        {
            // Narrow by design: these are the shapes "no emulator here" takes. Anything else is a
            // real defect and must surface rather than silently skipping the whole suite.
            client.Dispose();
            return null;
        }
    }
}
