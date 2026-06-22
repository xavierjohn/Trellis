namespace Trellis.EntityFrameworkCore.Inbox.Tests;

using global::Trellis.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1707 // readable xUnit test names

public sealed class InboxTests
{
    [Fact]
    public async Task Dispatch_processes_a_message_once_and_skips_redeliveries()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var envelope = Envelope();

        await dispatcher.DispatchAsync(envelope, ct);
        await dispatcher.DispatchAsync(envelope, ct); // redelivery of the same message

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await db.Receipts.CountAsync(ct)).Should().Be(1, "the handler runs exactly once across redeliveries");
        (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task Dispatch_deduplicates_per_consumer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var providerA = BuildProvider(connection, "consumer-a");
        await using var providerB = BuildProvider(connection, "consumer-b");
        await EnsureCreatedAsync(providerA, ct);

        var envelope = Envelope();
        await providerA.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct);
        await providerB.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct);
        await providerA.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct); // A redelivery

        await using var scope = providerA.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await db.Receipts.CountAsync(ct)).Should().Be(2, "each consumer processes the message exactly once");
        (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(2);
    }

    [Fact]
    public async Task Dispatch_rolls_back_and_rethrows_when_a_handler_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing", throwing: true);
        await EnsureCreatedAsync(provider, ct);
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();

        var act = async () => await dispatcher.DispatchAsync(Envelope(), ct);
        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await db.Receipts.CountAsync(ct)).Should().Be(0, "the handler side effects rolled back with the transaction");
        (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(0, "no dedup row is written for a failed message");
    }

    [Fact]
    public async Task A_failed_message_is_reprocessed_on_redelivery()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var gate = new FailFirstGate();
        await using var provider = BuildProvider(connection, "billing", gate: gate);
        await EnsureCreatedAsync(provider, ct);
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var envelope = Envelope();

        var act = async () => await dispatcher.DispatchAsync(envelope, ct);
        await act.Should().ThrowAsync<InvalidOperationException>("the first attempt fails transiently");

        await dispatcher.DispatchAsync(envelope, ct); // redelivery succeeds

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await db.Receipts.CountAsync(ct)).Should().Be(1, "the message is processed once it finally succeeds");
        (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public void AddTrellisInbox_requires_a_ConsumerId()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisInbox<InboxTestDbContext>(o => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConsumerId*");
    }

    [Fact]
    public void AddTrellisInbox_rejects_a_ConsumerId_longer_than_the_key_column()
    {
        var services = new ServiceCollection();
        var tooLong = new string('x', InboxOptions.MaxConsumerIdLength + 1);

        var act = () => services.AddTrellisInbox<InboxTestDbContext>(o => o.ConsumerId = tooLong);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{InboxOptions.MaxConsumerIdLength}*");
    }

    [Fact]
    public async Task A_handler_constraint_violation_propagates_and_is_not_swallowed_as_a_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlite(connection));
        services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, DuplicateLedgerHandler>();
        services.AddTrellisInbox<InboxTestDbContext>(o => o.ConsumerId = "billing");
        await using var provider = services.BuildServiceProvider();

        // Pre-seed the unique value the handler will collide with.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            db.Ledgers.Add(new Ledger { Id = Guid.NewGuid(), Entry = "duplicate" });
            await db.SaveChangesAsync(ct);
        }

        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var act = async () => await dispatcher.DispatchAsync(Envelope(), ct);

        // The handler's OWN unique-constraint failure must surface — not be mistaken for the inbox dedup-row
        // clash and silently swallowed as "already processed".
        await act.Should().ThrowAsync<DbUpdateException>();

        await using (var verify = provider.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<InboxTestDbContext>();
            (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(0, "a failed message is not marked processed");
            (await db.Ledgers.CountAsync(ct)).Should().Be(1, "the duplicate ledger row rolled back with the dedup row");
        }
    }

    [Fact]
    public async Task Dispatch_returns_Processed_for_a_new_message_then_SkippedDuplicate_for_a_redelivery()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var envelope = Envelope();

        var first = await dispatcher.DispatchAsync(envelope, ct);
        var second = await dispatcher.DispatchAsync(envelope, ct); // redelivery of the same message

        first.Should().Be(InboxDispatchOutcome.Processed, "the first delivery runs the handlers");
        second.Should().Be(InboxDispatchOutcome.SkippedDuplicate, "the redelivery is a recognized no-op");
    }

    [Fact]
    public async Task FilterUnprocessedAsync_returns_every_id_when_none_are_processed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var unprocessed = await store.FilterUnprocessedAsync("billing", ids, ct);

        unprocessed.Should().Equal(ids, "nothing has been processed yet");
    }

    [Fact]
    public async Task FilterUnprocessedAsync_excludes_processed_ids_and_preserves_input_order()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();

        var envelopes = Enumerable.Range(0, 5).Select(_ => Envelope()).ToList();
        await dispatcher.DispatchAsync(envelopes[1], ct); // process the 2nd
        await dispatcher.DispatchAsync(envelopes[3], ct); // and the 4th

        // Query in a deliberately scrambled order to prove the result follows the input order, not the table's.
        var query = new[] { envelopes[4], envelopes[3], envelopes[2], envelopes[1], envelopes[0] }
            .Select(e => e.MessageId).ToList();

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var unprocessed = await store.FilterUnprocessedAsync("billing", query, ct);

        unprocessed.Should().Equal(envelopes[4].MessageId, envelopes[2].MessageId, envelopes[0].MessageId);
    }

    [Fact]
    public async Task FilterUnprocessedAsync_is_scoped_per_consumer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var providerA = BuildProvider(connection, "consumer-a");
        await using var providerB = BuildProvider(connection, "consumer-b");
        await EnsureCreatedAsync(providerA, ct);
        var envelope = Envelope();

        await providerA.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct);

        await using var scope = providerB.CreateAsyncScope();
        var storeB = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var unprocessed = await storeB.FilterUnprocessedAsync("consumer-b", new[] { envelope.MessageId }, ct);

        unprocessed.Should().Equal(new[] { envelope.MessageId }, "consumer-b has not processed a message consumer-a did");
    }

    [Fact]
    public async Task FilterUnprocessedAsync_returns_empty_for_empty_input()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var unprocessed = await store.FilterUnprocessedAsync("billing", Array.Empty<Guid>(), ct);

        unprocessed.Should().BeEmpty();
    }

    [Fact]
    public async Task FilterUnprocessedAsync_requires_a_consumerId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var act = async () => await store.FilterUnprocessedAsync(" ", new[] { Guid.CreateVersion7() }, ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Dispatch_persists_the_dedup_row_lineage_metadata()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);

        var occurredAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var messageId = Guid.CreateVersion7();
        var causationId = Guid.NewGuid();
        var envelope = new IntegrationEnvelope(messageId, new OrderPlacedIntegrationEvent(Guid.NewGuid(), occurredAt))
        {
            MessageSource = "orders-service",
            CausationId = causationId,
            CorrelationId = "corr-123",
        };

        await provider.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        var row = await db.Set<InboxMessage>().SingleAsync(ct);

        // The envelope -> InboxRecord -> InboxMessage mapping must persist every column, not just the dedup key.
        row.ConsumerId.Should().Be("billing");
        row.MessageId.Should().Be(messageId);
        row.MessageSource.Should().Be("orders-service");
        row.EventType.Should().Be(typeof(OrderPlacedIntegrationEvent).AssemblyQualifiedName);
        row.OccurredAt.Should().Be(occurredAt);
        row.CausationId.Should().Be(causationId);
        row.CorrelationId.Should().Be("corr-123");
        row.ProcessedAt.Should().NotBe(default);
    }

    private static IntegrationEnvelope Envelope() =>
        new(Guid.CreateVersion7(), new OrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch));

    private static async Task EnsureCreatedAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
    }

    private static ServiceProvider BuildProvider(
        SqliteConnection connection, string consumerId, bool throwing = false, FailFirstGate? gate = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlite(connection));

        if (gate is not null)
        {
            services.AddSingleton(gate);
            services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, FailFirstHandler>();
        }
        else if (throwing)
        {
            services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, ThrowingReceiptHandler>();
        }
        else
        {
            services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, ReceiptHandler>();
        }

        services.AddTrellisInbox<InboxTestDbContext>(o => o.ConsumerId = consumerId);
        return services.BuildServiceProvider();
    }
}

// ── Test model ──────────────────────────────────────────────────────────────────

internal sealed record OrderPlacedIntegrationEvent(Guid OrderId, DateTimeOffset OccurredAt) : IIntegrationEvent;

internal sealed class Receipt
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
}

// A handler whose side effect (adding a Receipt) we count to prove once-effective processing.
internal sealed class ReceiptHandler(InboxTestDbContext context) : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        context.Receipts.Add(new Receipt { Id = Guid.NewGuid(), OrderId = integrationEvent.OrderId });
        return ValueTask.CompletedTask;
    }
}

// Stages a side effect then throws — the dispatcher must roll both back and not record the message.
internal sealed class ThrowingReceiptHandler(InboxTestDbContext context) : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        context.Receipts.Add(new Receipt { Id = Guid.NewGuid(), OrderId = integrationEvent.OrderId });
        throw new InvalidOperationException("handler boom");
    }
}

internal sealed class FailFirstGate
{
    public int Calls;
}

// Fails the first delivery, succeeds the second — proving a redelivery reprocesses a previously-failed message.
internal sealed class FailFirstHandler(InboxTestDbContext context, FailFirstGate gate)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        context.Receipts.Add(new Receipt { Id = Guid.NewGuid(), OrderId = integrationEvent.OrderId });
        if (gate.Calls++ == 0)
            throw new InvalidOperationException("transient boom");
        return ValueTask.CompletedTask;
    }
}

internal sealed class Ledger
{
    public Guid Id { get; set; }
    public string Entry { get; set; } = string.Empty;
}

// Inserts a Ledger row that collides with a pre-seeded unique value — a handler-side unique violation used
// to prove the dispatcher does NOT mistake a handler's own constraint failure for the inbox dedup clash.
internal sealed class DuplicateLedgerHandler(InboxTestDbContext context) : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        context.Ledgers.Add(new Ledger { Id = Guid.NewGuid(), Entry = "duplicate" });
        return ValueTask.CompletedTask;
    }
}

internal sealed class InboxTestDbContext(DbContextOptions<InboxTestDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Ledger> Ledgers => Set<Ledger>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Receipt>(b => b.HasKey(r => r.Id));
        modelBuilder.Entity<Ledger>(b =>
        {
            b.HasKey(l => l.Id);
            b.HasIndex(l => l.Entry).IsUnique();
        });
        modelBuilder.AddTrellisInbox();
        modelBuilder.AddTrellisConsumerCheckpoints();
    }
}

#pragma warning restore CA1707
