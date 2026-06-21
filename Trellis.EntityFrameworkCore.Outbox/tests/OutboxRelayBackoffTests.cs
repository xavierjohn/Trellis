namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using global::Trellis.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

#pragma warning disable CA1707 // readable xUnit test names

public sealed class OutboxRelayBackoffTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Relay_does_not_reclaim_a_failed_message_until_its_backoff_elapses()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(connection, time, o =>
        {
            o.RetryBackoff = TimeSpan.FromSeconds(30);
            o.RetryBackoffJitter = 0;
            o.MaxAttempts = 100;
        });
        await SeedPoisonAsync(provider, ct);
        var relay = Relay(provider);

        // First drain fails the poison once and leases it forward by the backoff.
        (await relay.DrainAsync(ct)).Should().Be(1);

        // A burst of immediate drains must NOT reclaim it: with a high MaxAttempts and no backoff the old
        // design would spin the attempt count up in a tight loop that hammers the database (a DoS).
        for (var i = 0; i < 5; i++)
            (await relay.DrainAsync(ct)).Should().Be(0, "the failed message is still within its backoff window");

        (await SingleRowAsync(provider, ct)).Attempts.Should().Be(1, "backoff prevented the burst of retries");

        // Once the backoff elapses it becomes eligible again and is retried.
        time.Advance(TimeSpan.FromSeconds(31));
        (await relay.DrainAsync(ct)).Should().Be(1, "the backoff window elapsed so the message is retried");
        (await SingleRowAsync(provider, ct)).Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Relay_grows_the_backoff_exponentially_and_caps_it()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(connection, time, o =>
        {
            o.RetryBackoff = TimeSpan.FromSeconds(30);
            o.MaxRetryBackoff = TimeSpan.FromMinutes(2);
            o.RetryBackoffJitter = 0;
            o.MaxAttempts = 100;
        });
        await SeedPoisonAsync(provider, ct);
        var relay = Relay(provider);

        // attempt 1 -> 30s, 2 -> 60s, 3 -> 120s (cap), 4 -> 120s (still capped).
        foreach (var seconds in new[] { 30, 60, 120, 120 })
        {
            var now = time.GetUtcNow().UtcDateTime;
            (await relay.DrainAsync(ct)).Should().Be(1);
            (await SingleRowAsync(provider, ct)).LockedUntil.Should().Be(now.AddSeconds(seconds));
            time.Advance(TimeSpan.FromSeconds(seconds));
        }
    }

    [Fact]
    public async Task Relay_applies_id_keyed_jitter_that_de_correlates_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(connection, time, o =>
        {
            o.RetryBackoff = TimeSpan.FromSeconds(30);
            o.MaxRetryBackoff = TimeSpan.FromHours(1);
            o.RetryBackoffJitter = 0.5;
            o.MaxAttempts = 100;
        });
        await SeedPoisonAsync(provider, ct, count: 8);
        var relay = Relay(provider);

        var start = time.GetUtcNow().UtcDateTime;
        (await relay.DrainAsync(ct)).Should().Be(8);

        var rows = await AllRowsAsync(provider, ct);
        foreach (var row in rows)
            row.LockedUntil.Should().Be(
                start + OutboxRetryBackoff.Compute(row.Id, 1, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), 0.5),
                "the relay schedules the retry via the id-keyed jitter");

        rows.Select(r => r.LockedUntil).Distinct().Count()
            .Should().BeGreaterThan(1, "jitter must not collapse the batch onto a single retry instant");
    }

    [Fact]
    public async Task Replay_resets_a_dead_lettered_message_so_the_relay_drains_it_again()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(connection, time, o =>
        {
            o.MaxAttempts = 2;
            o.RetryBackoffJitter = 0;
        });
        await SeedPoisonAsync(provider, ct);
        var relay = Relay(provider);

        await ParkAsync(relay, time, ct);

        Guid id;
        await using (var scope = provider.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IOutboxMaintenance>();
            var deadLettered = await maintenance.GetDeadLetteredAsync(cancellationToken: ct);
            deadLettered.Should().ContainSingle("the poison exhausted MaxAttempts and is dead-lettered");
            id = deadLettered[0].Id;

            (await maintenance.ReplayAsync(id, ct)).Should().Be(1);
        }

        var replayed = await SingleRowAsync(provider, ct);
        replayed.Attempts.Should().Be(0, "replay reset the attempt count");
        replayed.LockedUntil.Should().BeNull("replay cleared the lease so the scan re-picks it");
        replayed.LastError.Should().BeNull();
        replayed.ProcessedAt.Should().BeNull();

        // The relay drains the replayed row again (and it fails again -> attempt 1).
        (await relay.DrainAsync(ct)).Should().Be(1, "the replayed message is eligible again");
        (await SingleRowAsync(provider, ct)).Attempts.Should().Be(1);
    }

    [Fact]
    public async Task ReplayAll_resets_every_dead_lettered_message()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(connection, time, o =>
        {
            o.MaxAttempts = 2;
            o.RetryBackoffJitter = 0;
        });
        await SeedPoisonAsync(provider, ct, count: 3);
        var relay = Relay(provider);

        await ParkAsync(relay, time, ct);

        await using (var scope = provider.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IOutboxMaintenance>();
            (await maintenance.GetDeadLetteredAsync(cancellationToken: ct)).Should().HaveCount(3);
            (await maintenance.ReplayAllAsync(ct)).Should().Be(3);
        }

        var rows = await AllRowsAsync(provider, ct);
        rows.Should().OnlyContain(r => r.Attempts == 0 && r.LockedUntil == null && r.ProcessedAt == null);
    }

    [Fact]
    public async Task Relay_abandons_its_write_when_its_lease_is_stolen_mid_batch()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var logs = new List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<OutboxRelay<OutboxTestDbContext>>>(
            new FakeLogger<OutboxRelay<OutboxTestDbContext>>(logs));
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingCreated, LeaseStealingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "stolen", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = Relay(provider);

        // The handler reclaims the row's lease (a different LockedBy) while the relay is publishing; the
        // relay's bookkeeping UPDATE then conflicts, so it must abandon the write rather than clobber the
        // instance that now owns the row.
        (await relay.DrainAsync(ct)).Should().Be(0, "the only row in the batch was stolen, so nothing completed");

        var row = await SingleRowAsync(provider, ct);
        row.ProcessedAt.Should().BeNull("the relay abandoned MarkProcessed rather than clobber the new owner");
        row.LockedBy.Should().Be(LeaseStealingHandler.ThiefToken, "the instance that reclaimed the row still owns it");
        logs.Should().Contain(e => e.EventId.Name == "OutboxRelay.LeaseLost");
    }

    [Fact]
    public async Task Relay_drops_only_the_stolen_messages_integration_rows_and_enrols_its_siblings()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var logs = new List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<OutboxRelay<OutboxTestDbContext>>>(
            new FakeLogger<OutboxRelay<OutboxTestDbContext>>(logs));
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingCreated, StealAndTranslateHandler>();
        services.AddIntegrationEventDispatch();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            // Both messages translate an integration event; only the first has its lease stolen mid-batch.
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "steal-me", DateTimeOffset.UnixEpoch));
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "keep-me", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = Relay(provider);
        (await relay.DrainAsync(ct)).Should().Be(1, "one of the two domain rows in the batch was stolen");

        var rows = await AllRowsAsync(provider, ct);

        var stolen = rows.Single(r => r.Kind == OutboxMessageKind.Domain && r.Payload.Contains("steal-me"));
        stolen.ProcessedAt.Should().BeNull("the stolen domain row was abandoned, not clobbered");
        stolen.LockedBy.Should().Be(StealAndTranslateHandler.ThiefToken, "the instance that reclaimed it still owns it");

        var sibling = rows.Single(r => r.Kind == OutboxMessageKind.Domain && r.Payload.Contains("keep-me"));
        sibling.ProcessedAt.Should().NotBeNull("the non-stolen sibling was processed");

        // The crux: only the sibling's integration row is enrolled. The stolen row's produced integration
        // row is dropped (the instance that now owns it will re-produce it), so it is not double-enrolled.
        var integrationRows = rows.Where(r => r.Kind == OutboxMessageKind.Integration).ToList();
        integrationRows.Should().ContainSingle();
        integrationRows[0].Payload.Should().Contain("keep-me");
        integrationRows.Should().NotContain(r => r.Payload.Contains("steal-me"));

        logs.Should().Contain(e => e.EventId.Name == "OutboxRelay.LeaseLost");
    }

    // Drives a seeded poison message to its parked (dead-lettered) state with MaxAttempts = 2: one failure,
    // advance past the backoff, a second failure that reaches the attempt cap.
    private static async Task ParkAsync(OutboxRelay<OutboxTestDbContext> relay, FakeTimeProvider time, CancellationToken ct)
    {
        await relay.DrainAsync(ct);
        time.Advance(TimeSpan.FromMinutes(2));
        await relay.DrainAsync(ct);
    }

    private static ServiceProvider BuildProvider(SqliteConnection connection, FakeTimeProvider time, Action<OutboxOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddTrellisOutbox<OutboxTestDbContext>(configure);
        return services.BuildServiceProvider();
    }

    private static async Task SeedPoisonAsync(IServiceProvider provider, CancellationToken ct, int count = 1)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        await context.Database.EnsureCreatedAsync(ct);
        for (var i = 0; i < count; i++)
            context.Set<OutboxMessage>().Add(OutboxMessage.Create(
                Guid.NewGuid(), DateTimeOffset.UnixEpoch,
                "Nonexistent.Type, Nonexistent.Assembly", "{}", OutboxMessageKind.Domain));
        await context.SaveChangesAsync(ct);
    }

    private static OutboxRelay<OutboxTestDbContext> Relay(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<OutboxRelay<OutboxTestDbContext>>().Single();

    private static async Task<OutboxMessage> SingleRowAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        return await context.Set<OutboxMessage>().AsNoTracking().SingleAsync(ct);
    }

    private static async Task<List<OutboxMessage>> AllRowsAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        return await context.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.Sequence).ToListAsync(ct);
    }
}

// Simulates another relay instance reclaiming the row mid-publish: it stamps a different LockedBy (the
// optimistic concurrency token) so the original drain's bookkeeping UPDATE matches no row.
internal sealed class LeaseStealingHandler(OutboxTestDbContext context) : IDomainEventHandler<ThingCreated>
{
    public static readonly Guid ThiefToken = Guid.NewGuid();

    public async ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken) =>
        await context.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LockedBy, ThiefToken), cancellationToken);
}

// Translates an integration event for every message, and additionally steals the lease of the "steal-me"
// row (only) mid-publish — so one batch contains a stolen row and a non-stolen sibling, both producing
// integration rows. Guards that the relay drops only the stolen row's staged integration rows.
internal sealed class StealAndTranslateHandler(IIntegrationEventCollector collector, OutboxTestDbContext context)
    : IDomainEventHandler<ThingCreated>
{
    public static readonly Guid ThiefToken = Guid.NewGuid();

    public async ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        collector.Add(new ThingCreatedIntegrationEvent(domainEvent.Id.Value, domainEvent.Name, domainEvent.OccurredAt));

        if (domainEvent.Name == "steal-me")
            await context.Set<OutboxMessage>()
                .Where(m => m.ProcessedAt == null && m.Payload.Contains("steal-me"))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LockedBy, ThiefToken), cancellationToken);
    }
}
