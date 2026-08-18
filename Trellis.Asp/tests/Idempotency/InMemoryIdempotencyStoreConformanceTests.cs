namespace Trellis.Asp.Tests.Idempotency;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Trellis.Asp.Idempotency;
using Trellis.Testing.Idempotency;

/// <summary>
/// Runs the published <see cref="IdempotencyStoreConformance"/> suite against
/// <see cref="InMemoryIdempotencyStore"/>.
/// </summary>
/// <remarks>
/// This keeps the shipped reference implementation honest against the same contract third-party
/// stores are held to, so the suite cannot drift away from the store it documents.
/// </remarks>
public sealed class InMemoryIdempotencyStoreConformanceTests : IdempotencyStoreConformance
{
    private readonly FakeTimeProvider _time = new();

    protected override ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options) =>
        new(new InMemoryIdempotencyStore(options, _time));

    protected override Task AdvanceAsync(TimeSpan duration)
    {
        _time.Advance(duration);
        return Task.CompletedTask;
    }
}