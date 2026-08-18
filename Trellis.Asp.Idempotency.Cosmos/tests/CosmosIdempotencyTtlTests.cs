namespace Trellis.Asp.Idempotency.Cosmos.Tests;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Time.Testing;
using Trellis.Asp.Idempotency;

/// <summary>
/// Pins the per-item <c>ttl</c> the store persists in each document state.
/// </summary>
/// <remarks>
/// <para>
/// The conformance suite cannot cover this. It advances a fake clock, but Cosmos DB's sweeper runs
/// on real time, so no conformance run is long enough for an item to be physically deleted. That
/// leaves a gap the store must close by construction: if a <em>reserved</em> document carried a
/// finite <c>ttl</c>, then once the sweeper eventually removed it, a same-key request with a
/// different body would be granted a fresh reservation instead of the
/// <see cref="IdempotencyReservationOutcome.BodyHashMismatch"/> that
/// <see cref="CosmosIdempotencyDecision.Classify"/> returns while the document is still present —
/// so the store's answer would depend on whether a best-effort background sweep had happened to
/// run. These tests assert the store never gets into that position.
/// </para>
/// <para>
/// Excluded from default runs — use <c>dotnet test --filter-trait "Category=Integration"</c>
/// (requires the Azure Cosmos DB emulator).
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class CosmosIdempotencyTtlTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task Reserved_document_never_expires_so_a_sweep_cannot_change_the_answer()
    {
        var (store, container, scope) = await CreateAsync();

        var reserved = await store.TryReserveAsync(scope, "key", "fingerprint", TestContext.Current.CancellationToken);
        reserved.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();

        var ttl = await ReadTtlAsync(container, scope);
        ttl.Should().Be(-1, "a reserved document outlives every timeout in Classify, so Cosmos DB must not delete it");
    }

    [Fact]
    public async Task Completed_document_expires_so_storage_is_reclaimed()
    {
        var (store, container, scope) = await CreateAsync();

        var reserved = (IdempotencyReservationOutcome.Reserved)await store.TryReserveAsync(
            scope, "key", "fingerprint", TestContext.Current.CancellationToken);

        await store.CompleteAsync(
            scope,
            "key",
            reserved.ReservationId,
            new IdempotencyResponseSnapshot(200, new Dictionary<string, string[]>(), [], "fingerprint"),
            TestContext.Current.CancellationToken);

        var ttl = await ReadTtlAsync(container, scope);
        ttl.Should().BePositive("a completed document is unreachable once Classify treats it as absent");
    }

    private async Task<(CosmosIdempotencyStore Store, Container Container, string Scope)> CreateAsync()
    {
        var container = await CosmosEmulator.TryGetContainerAsync();
        Assert.SkipWhen(
            container is null,
            "No Cosmos DB emulator reachable at https://localhost:8081/, so the Cosmos TTL tests did not run.");

        var options = new IdempotencyOptions();
        return (new CosmosIdempotencyStore(container!, options, _time), container!, Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Reads <c>ttl</c> straight out of the stored JSON rather than through the store's own model,
    /// so the assertion is about what Cosmos DB will actually act on.
    /// </summary>
    private static async Task<int?> ReadTtlAsync(Container container, string scope)
    {
        using var iterator = container.GetItemQueryStreamIterator(
            new QueryDefinition("SELECT c.ttl FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(scope) });

        using var response = await iterator.ReadNextAsync(TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        using var payload = await JsonDocument.ParseAsync(
            response.Content, cancellationToken: TestContext.Current.CancellationToken);

        var documents = payload.RootElement.GetProperty("Documents");
        documents.GetArrayLength().Should().Be(1, "each test uses its own scope, so the partition holds one entry");

        var ttl = documents[0];
        return ttl.TryGetProperty("ttl", out var value) ? value.GetInt32() : null;
    }
}