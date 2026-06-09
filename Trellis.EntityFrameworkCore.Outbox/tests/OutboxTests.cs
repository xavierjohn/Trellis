namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using global::Trellis.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#pragma warning disable CA1707 // readable xUnit test names

public sealed class OutboxTests
{
    [Fact]
    public async Task SaveChanges_captures_events_to_outbox_in_same_transaction_and_clears_aggregate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor()
            .Options;

        await using var context = new OutboxTestDbContext(options);
        await context.Database.EnsureCreatedAsync(ct);

        var thing = Thing.Create(ThingId.NewUniqueV7(), "alpha", DateTimeOffset.UnixEpoch);
        context.Things.Add(thing);
        await context.SaveChangesAsync(ct);

        // The interceptor cleared the aggregate's events during the commit.
        thing.UncommittedEvents().Should().BeEmpty();

        // Exactly one outbox row, persisted in the same database/transaction.
        var rows = await context.Set<OutboxMessage>().ToListAsync(ct);
        rows.Should().ContainSingle();
        rows[0].EventType.Should().Contain(nameof(ThingCreated));
        rows[0].Payload.Should().Contain("alpha");
        rows[0].ProcessedAt.Should().BeNull();
        rows[0].Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveChanges_rolls_back_outbox_when_the_aggregate_write_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor()
            .Options;

        var id = ThingId.NewUniqueV7();

        await using (var seed = new OutboxTestDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync(ct);
            seed.Things.Add(Thing.Create(id, "first", DateTimeOffset.UnixEpoch));
            await seed.SaveChangesAsync(ct);
        }

        // A fresh context inserting a duplicate primary key fails at SaveChanges (DB constraint),
        // which must roll back the outbox row captured in the same transaction.
        await using (var conflicting = new OutboxTestDbContext(options))
        {
            var thing = Thing.Create(id, "duplicate", DateTimeOffset.UnixEpoch);
            conflicting.Things.Add(thing);
            var act = async () => await conflicting.SaveChangesAsync(ct);
            await act.Should().ThrowAsync<DbUpdateException>();

            // The failed save must NOT clear the aggregate's events (so a retry can re-capture)
            // and must detach the outbox row it staged (so a retry does not double-capture).
            thing.UncommittedEvents().Should().NotBeEmpty();
            conflicting.ChangeTracker.Entries<OutboxMessage>()
                .Count(e => e.State == EntityState.Added).Should().Be(0);
        }

        // Outbox still holds only the first (successful) message — the failed capture rolled back.
        await using var verify = new OutboxTestDbContext(options);
        (await verify.Set<OutboxMessage>().CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task Relay_redispatches_pending_messages_to_handlers_and_marks_them_processed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var captured = new List<ThingCreated>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(captured);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingCreated, CapturingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.PollInterval = TimeSpan.FromMilliseconds(10));

        await using var provider = services.BuildServiceProvider();

        // Seed: create the schema and a thing — the capture interceptor writes the outbox row.
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "beta", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        var drained = await relay.DrainAsync(ct);

        drained.Should().Be(1);
        captured.Should().ContainSingle().Which.Name.Should().Be("beta");

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var row = await context.Set<OutboxMessage>().SingleAsync(ct);
            row.ProcessedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task AddTrellisOutboxInterceptor_CalledTwice_CapturesEachEventOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        // Registering the capture interceptor twice on the same options builder must not
        // double-capture; the registration is idempotent via an options marker.
        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor()
            .AddTrellisOutboxInterceptor()
            .Options;

        await using var context = new OutboxTestDbContext(options);
        await context.Database.EnsureCreatedAsync(ct);

        context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "gamma", DateTimeOffset.UnixEpoch));
        await context.SaveChangesAsync(ct);

        // Exactly one outbox row despite the duplicate interceptor registration.
        (await context.Set<OutboxMessage>().CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task Relay_does_not_persist_handler_mutations_to_its_bookkeeping_context()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingCreated, ContextMutatingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "original", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        await relay.DrainAsync(ct);

        // The handler mutates its injected TContext. If the relay shared its bookkeeping context with
        // handler execution, the relay's SaveChanges would persist the handler's new aggregate (and
        // capture a second outbox row). Isolated scopes mean only the original aggregate/row survive.
        await using (var verify = provider.CreateAsyncScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            (await context.Things.CountAsync(ct)).Should().Be(1);
            (await context.Set<OutboxMessage>().CountAsync(ct)).Should().Be(1);
        }
    }

    [Fact]
    public async Task SavedChangesAsync_ClearsAggregateEvents_EvenWhenTokenIsCanceled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseSqlite(connection)
            .AddTrellisOutboxInterceptor()
            .Options;

        await using var context = new OutboxTestDbContext(options);
        await context.Database.EnsureCreatedAsync(ct);

        var thing = Thing.Create(ThingId.NewUniqueV7(), "delta", DateTimeOffset.UnixEpoch);
        context.Things.Add(thing);
        thing.UncommittedEvents().Should().NotBeEmpty();

        // The post-commit hook must clear events even with an already-cancelled token, because it runs
        // after a successful commit — a regression guard for the removed pre-clear cancellation check.
        var interceptor = new OutboxCaptureInterceptor();
        var eventData = new SaveChangesCompletedEventData(null!, (_, _) => string.Empty, context, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await interceptor.SavedChangesAsync(eventData, 1, cts.Token).AsTask();

        thing.UncommittedEvents().Should().BeEmpty();
    }
}

// ── Test domain model ──────────────────────────────────────────────────────────

internal sealed partial class ThingId : RequiredGuid<ThingId>;

internal sealed record ThingCreated(ThingId Id, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed class Thing : Aggregate<ThingId>
{
    public string Name { get; private set; } = string.Empty;

    private Thing(ThingId id) : base(id) { }

    public static Thing Create(ThingId id, string name, DateTimeOffset occurredAt)
    {
        var thing = new Thing(id) { Name = name };
        thing.DomainEvents.Add(new ThingCreated(id, name, occurredAt));
        return thing;
    }
}

internal sealed class CapturingHandler(List<ThingCreated> captured) : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        captured.Add(domainEvent);
        return ValueTask.CompletedTask;
    }
}

internal sealed class ContextMutatingHandler(OutboxTestDbContext context) : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        // Deliberately mutate the injected context to prove the relay does not save handler work.
        context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "handler-added", DateTimeOffset.UnixEpoch));
        return ValueTask.CompletedTask;
    }
}

internal sealed class OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(ThingId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired();
        });

        modelBuilder.AddTrellisOutbox();
    }
}

#pragma warning restore CA1707
