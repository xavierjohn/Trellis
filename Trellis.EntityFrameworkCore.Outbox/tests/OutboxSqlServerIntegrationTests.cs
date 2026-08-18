namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using global::Trellis.Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#pragma warning disable CA1707 // readable xUnit test names

/// <summary>
/// End-to-end SQL Server integration test for the transactional outbox. Unlike the in-memory unit
/// tests (which call <c>DrainAsync</c> directly), this exercises the full path against a real
/// database: a committed SQL Server transaction captures the event, and the real hosted
/// <c>OutboxRelay</c> background loop polls, drains, and dispatches it to a handler on its own.
/// Excluded from default runs — use <c>dotnet test --filter-trait "Category=Integration"</c>
/// (requires SQL Server LocalDB).
/// </summary>
[Trait("Category", "Integration")]
public sealed class OutboxSqlServerIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=TrellisOutboxIntegrationTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static DbContextOptions<OutboxTestDbContext> SchemaOptions() =>
        new DbContextOptionsBuilder<OutboxTestDbContext>().UseSqlServer(ConnectionString).Options;

    public async ValueTask InitializeAsync()
    {
        await using var context = new OutboxTestDbContext(SchemaOptions());
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = new OutboxTestDbContext(SchemaOptions());
        await context.Database.EnsureDeletedAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Hosted_relay_dispatches_a_committed_event_end_to_end()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatched = new TaskCompletionSource<ThingCreated>(TaskCreationOptions.RunContinuationsAsynchronously);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dispatched);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlServer(ConnectionString)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingCreated, SignalingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.PollInterval = TimeSpan.FromMilliseconds(100));

        await using var provider = services.BuildServiceProvider();

        // Start the real hosted relay (BackgroundService.ExecuteAsync begins polling).
        var relay = provider.GetServices<IHostedService>().Single();
        await relay.StartAsync(ct);
        try
        {
            var id = ThingId.NewUniqueV7();

            // Commit an aggregate through a real SQL Server transaction; the interceptor captures the
            // event into the outbox table atomically with the aggregate row.
            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
                context.Things.Add(Thing.Create(id, "integration", DateTimeOffset.UnixEpoch));
                await context.SaveChangesAsync(ct);
            }

            // No DrainAsync call — the hosted relay loop must pick the row up and dispatch on its own.
            var received = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            received.Id.Should().Be(id);
            received.Name.Should().Be("integration");

            // The handler signals during publish, before the relay persists MarkProcessed, so poll the
            // row until the relay's bookkeeping save lands rather than racing it.
            var row = await WaitForOutboxRowAsync(provider, m => m.ProcessedAt != null, ct);
            row.Attempts.Should().Be(0);
        }
        finally
        {
            await relay.StopAsync(ct);
        }
    }

    [Fact]
    public async Task Hosted_relay_parks_a_failing_message_while_still_delivering_a_good_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatched = new TaskCompletionSource<ThingCreated>(TaskCreationOptions.RunContinuationsAsynchronously);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dispatched);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlServer(ConnectionString)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingCreated, SignalingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(100);
            o.MaxAttempts = 2;
        });

        await using var provider = services.BuildServiceProvider();

        var poisonId = Guid.CreateVersion7();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();

            // Poison first (lowest Sequence): an unresolvable event type the relay cannot deserialize.
            context.Set<OutboxMessage>().Add(OutboxMessage.Create(
                poisonId,
                DateTimeOffset.UnixEpoch,
                "Trellis.Outbox.Tests.NoSuchEvent, Trellis.Outbox.Tests.NoSuchAssembly",
                "{}",
                OutboxMessageKind.Domain));
            await context.SaveChangesAsync(ct);

            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "good-after-poison", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>().Single();
        await relay.StartAsync(ct);
        try
        {
            // A failing message at the head of the queue must not block delivery of the good one.
            var received = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            received.Name.Should().Be("good-after-poison");

            // The poison is retried by the hosted loop and parked once it reaches MaxAttempts: still
            // unprocessed, with the failure recorded for support.
            var poison = await WaitForOutboxRowAsync(provider, m => m.Id == poisonId && m.Attempts >= 2, ct);
            poison.ProcessedAt.Should().BeNull();
            poison.Attempts.Should().Be(2);
            poison.LastError.Should().NotBeNullOrEmpty();
        }
        finally
        {
            await relay.StopAsync(ct);
        }
    }

    [Fact]
    public async Task Two_concurrent_relays_publish_each_message_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        const int messageCount = 50;
        var delivered = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        ServiceProvider BuildRelayProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(delivered);
            services.AddDbContext<OutboxTestDbContext>(o => o
                .UseSqlServer(ConnectionString)
                .AddTrellisInterceptors()
                .AddTrellisOutboxInterceptor());
            services.AddDomainEventDispatch();
            services.AddDomainEventHandler<ThingCreated, CountingHandler>();
            // Small batches so the two relays interleave and actually race for rows.
            services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 5);
            return services.BuildServiceProvider();
        }

        // Seed N committed events (N outbox rows).
        await using (var seed = BuildRelayProvider())
        {
            await using var scope = seed.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            for (var i = 0; i < messageCount; i++)
                context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), $"msg-{i}", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        // Two independent relay instances drain the same outbox concurrently.
        await using var providerA = BuildRelayProvider();
        await using var providerB = BuildRelayProvider();

        async Task DrainLoopAsync(ServiceProvider provider)
        {
            var relay = provider.GetServices<IHostedService>()
                .OfType<OutboxRelay<OutboxTestDbContext>>()
                .Single();
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var drained = await relay.DrainAsync(ct);

                await using var scope = provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
                var pending = await context.Set<OutboxMessage>().AnyAsync(m => m.ProcessedAt == null, ct);
                if (!pending)
                    return;
                if (drained == 0)
                    await Task.Delay(20, ct); // lost the race for the in-flight batch; back off briefly
            }

            throw new TimeoutException("Outbox did not drain within the timeout; a regression likely stalled the relay.");
        }

        await Task.WhenAll(DrainLoopAsync(providerA), DrainLoopAsync(providerB));

        // The atomic claim means every message is published exactly once across both instances —
        // no double-publish despite concurrent draining.
        delivered.Should().HaveCount(messageCount);
        delivered.Distinct().Should().HaveCount(messageCount);
    }

    private static async Task<OutboxMessage> WaitForOutboxRowAsync(
        IServiceProvider provider, Func<OutboxMessage, bool> predicate, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var rows = await context.Set<OutboxMessage>().AsNoTracking().ToListAsync(cancellationToken);
            var match = rows.FirstOrDefault(predicate);
            if (match is not null)
                return match;

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("The outbox row did not reach the expected state within the timeout.");
    }
}

internal sealed class SignalingHandler(TaskCompletionSource<ThingCreated> signal) : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        signal.TrySetResult(domainEvent);
        return ValueTask.CompletedTask;
    }
}

internal sealed class CountingHandler(System.Collections.Concurrent.ConcurrentBag<Guid> delivered)
    : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        delivered.Add(domainEvent.Id.Value);
        return ValueTask.CompletedTask;
    }
}

#pragma warning restore CA1707