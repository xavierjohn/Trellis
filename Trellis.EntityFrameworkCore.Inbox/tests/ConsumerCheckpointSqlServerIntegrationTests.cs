namespace Trellis.EntityFrameworkCore.Inbox.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1707 // readable xUnit test names

/// <summary>
/// SQL Server integration coverage for the pull-consumer checkpoint store. The in-memory SQLite unit tests
/// cover the read/upsert logic; this exercises the round-trip and the advance-updates-in-place behavior
/// against a real provider (where the upsert is a separate UPDATE rather than a SQLite in-memory write).
/// Excluded from default runs — use <c>dotnet test -- --filter-trait "Category=Integration"</c>
/// (requires SQL Server LocalDB).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConsumerCheckpointSqlServerIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=TrellisCheckpointIntegrationTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static DbContextOptions<InboxTestDbContext> SchemaOptions() =>
        new DbContextOptionsBuilder<InboxTestDbContext>().UseSqlServer(ConnectionString).Options;

    public async ValueTask InitializeAsync()
    {
        await using var context = new InboxTestDbContext(SchemaOptions());
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = new InboxTestDbContext(SchemaOptions());
        await context.Database.EnsureDeletedAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Round_trips_and_advances_the_checkpoint_in_place()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        (await store.GetAsync("billing", ct)).HasValue.Should().BeFalse("no checkpoint yet");

        await store.SetAsync("billing", "cursor-1", ct);
        (await store.GetAsync("billing", ct)).GetValueOrDefault("").Should().Be("cursor-1");

        await store.SetAsync("billing", "cursor-2", ct);
        (await store.GetAsync("billing", ct)).GetValueOrDefault("").Should().Be("cursor-2");

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await db.Set<ConsumerCheckpoint>().CountAsync(ct)).Should().Be(1, "the advance updates the single row");
    }

    [Fact]
    public async Task Concurrent_first_writes_resolve_to_last_writer_wins_without_throwing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        // Many parallel first writes for the SAME new consumer — the upsert must absorb the duplicate-key race
        // (one inserts, the rest retry as updates) rather than throwing.
        var positions = Enumerable.Range(0, 12).Select(i => $"cursor-{i}").ToList();
        var write = async () => await Task.WhenAll(positions.Select(p => store.SetAsync("billing", p, ct)));

        await write.Should().NotThrowAsync();

        (await store.GetAsync("billing", ct)).HasValue.Should().BeTrue();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await db.Set<ConsumerCheckpoint>().CountAsync(ct)).Should().Be(1, "exactly one checkpoint row exists for the consumer");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddTrellisConsumerCheckpointStore<InboxTestDbContext>();
        return services.BuildServiceProvider();
    }
}

#pragma warning restore CA1707
