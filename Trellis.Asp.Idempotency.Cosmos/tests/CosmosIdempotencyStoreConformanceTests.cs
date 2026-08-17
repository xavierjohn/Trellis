namespace Trellis.Asp.Idempotency.Cosmos.Tests;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Trellis.Asp.Idempotency;
using Trellis.Testing.Idempotency;

/// <summary>
/// Runs the published <see cref="IdempotencyStoreConformance"/> suite against a real Cosmos DB
/// instance (the emulator).
/// </summary>
/// <remarks>
/// <para>
/// The suite runs with the default 30-second reservation timeout and one-hour TTL and still
/// completes in the time of ordinary Cosmos DB round trips, because
/// <see cref="CosmosIdempotencyStore"/> enforces expiry against an injected
/// <see cref="TimeProvider"/> rather than trusting the service's background TTL sweep. Advancing a
/// fake clock is therefore enough to exercise takeover and TTL expiry — which is the same property
/// that makes the store correct in production, where an item may outlive its <c>ttl</c> and still
/// be returned by a read.
/// </para>
/// <para>
/// Skips when no emulator is reachable. See <see cref="CosmosEmulator"/>.
/// </para>
/// <para>
/// Excluded from default runs — use <c>dotnet test --filter-trait "Category=Integration"</c>
/// (requires the Azure Cosmos DB emulator).
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class CosmosIdempotencyStoreConformanceTests : IdempotencyStoreConformance
{
    private readonly FakeTimeProvider _time = new();

    protected override async ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options)
    {
        var container = await CosmosEmulator.TryGetContainerAsync();
        Assert.SkipWhen(
            container is null,
            "No Cosmos DB emulator reachable at https://localhost:8081/, so the Cosmos conformance suite did not run.");

        return new CosmosIdempotencyStore(container!, options, _time);
    }

    protected override Task AdvanceAsync(TimeSpan duration)
    {
        _time.Advance(duration);
        return Task.CompletedTask;
    }

    // Each caller is a real network round trip to the emulator, so keep the race small enough to
    // stay fast while still being a genuine race.
    protected override int ConcurrentReservationAttempts => 8;
}
