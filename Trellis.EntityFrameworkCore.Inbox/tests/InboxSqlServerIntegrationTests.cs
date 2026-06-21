namespace Trellis.EntityFrameworkCore.Inbox.Tests;

using global::Trellis.Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1707 // readable xUnit test names

/// <summary>
/// End-to-end SQL Server integration test for the transactional inbox. The in-memory SQLite unit tests
/// drive redeliveries sequentially; this exercises the duplicate-key guard under real database
/// concurrency — many parallel dispatches of the same <c>(ConsumerId, MessageId)</c> must apply each
/// message's side effect exactly once, with the loser of the race rolled back rather than throwing.
/// Excluded from default runs — use <c>dotnet test --filter-trait "Category=Integration"</c>
/// (requires SQL Server LocalDB).
/// </summary>
[Trait("Category", "Integration")]
public sealed class InboxSqlServerIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=TrellisInboxIntegrationTests;Trusted_Connection=True;TrustServerCertificate=True";

    private const string ConsumerId = "billing";

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
    public async Task Concurrent_dispatches_of_the_same_message_process_it_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        const int messageCount = 25;

        // Distinct messages; each is delivered twice, concurrently, by two independent consumers
        // pointed at the same inbox table — the worst case the duplicate-key guard must absorb.
        var envelopes = Enumerable.Range(0, messageCount)
            .Select(_ => new IntegrationEnvelope(
                Guid.CreateVersion7(),
                new OrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch)))
            .ToList();

        await using var providerA = BuildProvider();
        await using var providerB = BuildProvider();
        var dispatcherA = providerA.GetRequiredService<IInboxDispatcher>();
        var dispatcherB = providerB.GetRequiredService<IInboxDispatcher>();

        // Start both dispatches of every message before awaiting any, so they genuinely race for the
        // composite primary key rather than running back to back.
        var dispatches = envelopes
            .SelectMany(e => new[]
            {
                dispatcherA.DispatchAsync(e, ct),
                dispatcherB.DispatchAsync(e, ct),
            })
            .ToList();
        await Task.WhenAll(dispatches);

        await using var verify = BuildProvider();
        await using var scope = verify.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();

        // Exactly one dedup row and one side effect per message — no double-processing despite the race,
        // and the losing dispatch swallowed the duplicate-key violation instead of surfacing it.
        (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(messageCount);
        var receiptOrderIds = await db.Receipts.Select(r => r.OrderId).ToListAsync(ct);
        receiptOrderIds.Should().HaveCount(messageCount);
        receiptOrderIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_handler_unique_violation_is_not_swallowed_as_a_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, DuplicateLedgerHandler>();
        services.AddTrellisInbox<InboxTestDbContext>(o => o.ConsumerId = ConsumerId);
        await using var provider = services.BuildServiceProvider();

        // Pre-seed the unique value the handler will collide with.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
            db.Ledgers.Add(new Ledger { Id = Guid.NewGuid(), Entry = "duplicate" });
            await db.SaveChangesAsync(ct);
        }

        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var envelope = new IntegrationEnvelope(
            Guid.CreateVersion7(), new OrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch));
        var act = async () => await dispatcher.DispatchAsync(envelope, ct);

        // SQL Server batches the inbox row and the handler row into one command batch, so a failed batch
        // reports BOTH entries. The dispatcher must still not mistake the handler's own unique violation for
        // the inbox dedup-row collision: it must surface, and nothing must be recorded as processed.
        await act.Should().ThrowAsync<DbUpdateException>();

        await using (var verify = provider.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<InboxTestDbContext>();
            (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(0, "a failed message is not marked processed");
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, ReceiptHandler>();
        services.AddTrellisInbox<InboxTestDbContext>(o => o.ConsumerId = ConsumerId);
        return services.BuildServiceProvider();
    }
}

#pragma warning restore CA1707
