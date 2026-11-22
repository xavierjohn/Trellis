namespace Trellis.Asp.Idempotency.Cosmos.Tests;

using System.Reflection;
using System.Text.Json;

public class CosmosIdempotencySerializationTests
{
    [Fact]
    public void Serialize_LargeSnapshot_PreservesWireShapeWithoutGrowingTheOutputBuffer()
    {
        var serialize = typeof(CosmosIdempotencyStore)
            .GetMethod("Serialize", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<CosmosIdempotencyDocument, MemoryStream>>();
        var body = new byte[64 * 1024];
        new Random(42).NextBytes(body);
        var document = new CosmosIdempotencyDocument
        {
            Id = "a2V5",
            Scope = "actor:1",
            Key = "key",
            Fingerprint = "hash",
            CompletedAtUnixMs = 1_767_268_801_000,
            Ttl = 86_460,
            Snapshot = new CosmosResponseSnapshotDocument
            {
                StatusCode = 201,
                Headers = new() { ["Location"] = ["/orders/1"] },
                Body = body,
                Fingerprint = "hash",
            },
        };

        var minAllocated = long.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            using var sample = serialize(document);
            minAllocated = Math.Min(minAllocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        using var serialized = serialize(document);
        serialized.TryGetBuffer(out var buffer).Should().BeTrue();
        buffer.Array!.LongLength.Should().Be(serialized.Length);

        using var json = JsonDocument.Parse(serialized);
        var root = json.RootElement;
        root.GetProperty("id").GetString().Should().Be(document.Id);
        root.GetProperty("scope").GetString().Should().Be(document.Scope);
        root.GetProperty("ttl").GetInt32().Should().Be(document.Ttl);
        root.TryGetProperty("reservationId", out _).Should().BeFalse();
        var snapshot = root.GetProperty("snapshot");
        snapshot.GetProperty("statusCode").GetInt32().Should().Be(201);
        snapshot.GetProperty("headers").GetProperty("Location")[0].GetString().Should().Be("/orders/1");
        snapshot.GetProperty("body").GetBytesFromBase64().Should().Equal(body);
        minAllocated.Should().BeLessThan(2L * body.Length);
    }
}
