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
        var outcomes = await Task.WhenAll(dispatches);

        // Each message races two dispatches: exactly one wins (Processed) and the other is recognized as a
        // duplicate (SkippedDuplicate) — the loser must never throw, whether it lost in the TryRecord
        // fast-path or in the duplicate-key catch.
        outcomes.Count(o => o == InboxDispatchOutcome.Processed).Should().Be(messageCount);
        outcomes.Count(o => o == InboxDispatchOutcome.SkippedDuplicate).Should().Be(messageCount);

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

    [Fact]
    public async Task A_lost_duplicate_key_race_returns_SkippedDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var envelope = new IntegrationEnvelope(
            Guid.CreateVersion7(), new OrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch));

        // A barrier inside the handler holds BOTH dispatches past TryRecordAsync (i.e. in their handler, with
        // their dedup row staged but not saved) before either reaches SaveChanges. So neither can short-circuit
        // on the fast-path existence check, and the loser is guaranteed to take the duplicate-key *catch* path
        // — the one that re-checks in a fresh scope and returns SkippedDuplicate.
        var barrier = new DispatchBarrier(participants: 2);
        await using var providerA = BuildBarrierProvider(barrier);
        await using var providerB = BuildBarrierProvider(barrier);

        var a = providerA.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct);
        var b = providerB.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct);
        var outcomes = await Task.WhenAll(a, b);

        outcomes.Should().BeEquivalentTo(
            new[] { InboxDispatchOutcome.Processed, InboxDispatchOutcome.SkippedDuplicate },
            "exactly one dispatch wins the primary key; the loser takes the duplicate-key catch and reports SkippedDuplicate");

        await using var verify = BuildProvider();
        await using var scope = verify.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(1, "only the winner's dedup row committed");
        (await db.Receipts.CountAsync(ct)).Should().Be(1, "the loser's handler side effect rolled back with its failed save");
    }

    [Fact]
    public async Task FilterUnprocessedAsync_handles_a_window_far_larger_than_the_sql_parameter_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();

        // Process a handful, then query a window with far more candidate ids than SQL Server's 2100-parameter
        // limit. EF Core parameterizes the Contains collection as a single OPENJSON argument, so this must not
        // fail with "too many parameters" regardless of window size.
        var processed = Enumerable.Range(0, 5)
            .Select(_ => new IntegrationEnvelope(
                Guid.CreateVersion7(), new OrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch)))
            .ToList();
        foreach (var e in processed)
            await dispatcher.DispatchAsync(e, ct);

        var window = new List<Guid>(processed.Select(e => e.MessageId));
        window.AddRange(Enumerable.Range(0, 5000).Select(_ => Guid.CreateVersion7()));

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var unprocessed = await store.FilterUnprocessedAsync(ConsumerId, window, ct);

        unprocessed.Should().HaveCount(5000, "the 5 processed ids are excluded and the 5000-id window did not hit the parameter limit");
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

    private static ServiceProvider BuildBarrierProvider(DispatchBarrier barrier)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddSingleton(barrier);
        services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, BarrierReceiptHandler>();
        services.AddTrellisInbox<InboxTestDbContext>(o => o.ConsumerId = ConsumerId);
        return services.BuildServiceProvider();
    }
}

// Releases all participants only once every one of them has arrived, so concurrent dispatches are guaranteed
// to be past TryRecordAsync (in their handler) before any of them saves.
internal sealed class DispatchBarrier(int participants)
{
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public Task ArriveAndWaitAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrived) >= participants)
            _allArrived.TrySetResult();
        return _allArrived.Task.WaitAsync(cancellationToken);
    }
}

// Like ReceiptHandler, but waits on the shared barrier first so concurrent dispatches interleave at the
// SaveChanges boundary rather than running back to back.
internal sealed class BarrierReceiptHandler(InboxTestDbContext context, DispatchBarrier barrier)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public async ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await barrier.ArriveAndWaitAsync(cancellationToken);
        context.Receipts.Add(new Receipt { Id = Guid.NewGuid(), OrderId = integrationEvent.OrderId });
    }
}

#pragma warning restore CA1707
