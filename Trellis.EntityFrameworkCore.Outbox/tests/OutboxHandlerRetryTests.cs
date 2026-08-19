namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using global::Trellis.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

#pragma warning disable CA1707 // readable xUnit test names

/// <summary>
/// Guards the per-handler retry contract: a domain message is delivered only once every handler has
/// completed, and a retry re-invokes only the handlers that actually failed.
/// </summary>
public sealed class OutboxHandlerRetryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Retry_reinvokes_only_the_failed_handler_and_not_its_succeeded_sibling()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var stable = new HandlerCallCount();
        var flaky = new FlakyHandlerState();
        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(connection, time, services =>
        {
            services.AddSingleton(stable);
            services.AddSingleton(flaky);
            services.AddDomainEventHandler<ThingCreated, StableHandler>();
            services.AddDomainEventHandler<ThingCreated, FlakyHandler>();
        });

        await SeedThingAsync(provider, ct);
        var relay = Relay(provider);

        // Attempt 1: both handlers run, one fails. The message must stay pending.
        (await relay.DrainAsync(ct)).Should().Be(1);
        stable.Invocations.Should().Be(1);
        flaky.Invocations.Should().Be(1);

        var afterFirst = await SingleDomainRowAsync(provider, ct);
        afterFirst.ProcessedAt.Should().BeNull();
        afterFirst.Attempts.Should().Be(1);
        afterFirst.CompletedHandlers.Should().Equal([DomainEventDispatchReport.HandlerIdentity(typeof(StableHandler))]);

        // Attempt 2, after the backoff: only the failed handler is re-invoked. This is the whole point of
        // per-handler tracking — a succeeded handler's side effect is never duplicated because an
        // unrelated sibling failed.
        flaky.ShouldThrow = false;
        time.Advance(TimeSpan.FromSeconds(31));
        (await relay.DrainAsync(ct)).Should().Be(1);

        stable.Invocations.Should().Be(1, "it already completed, so the retry must skip it");
        flaky.Invocations.Should().Be(2, "it is the only handler that still had work to do");

        var afterSecond = await SingleDomainRowAsync(provider, ct);
        afterSecond.ProcessedAt.Should().NotBeNull("every handler has now completed");
        afterSecond.LastError.Should().BeNull();
        afterSecond.CompletedHandlers.Should().BeEquivalentTo(
            [DomainEventDispatchReport.HandlerIdentity(typeof(StableHandler)), DomainEventDispatchReport.HandlerIdentity(typeof(FlakyHandler))]);
    }

    [Fact]
    public async Task Integration_events_from_a_succeeded_handler_are_staged_once_despite_a_failing_sibling()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var flaky = new FlakyHandlerState();
        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(connection, time, services =>
        {
            services.AddSingleton(flaky);
            services.AddSingleton(new List<ThingCreatedIntegrationEvent>());
            services.AddDomainEventHandler<ThingCreated, ThingCreatedTranslator>();
            services.AddIntegrationEventHandler<ThingCreatedIntegrationEvent, IntegrationCapturingHandler>();
            services.AddDomainEventHandler<ThingCreated, FlakyHandler>();
        });

        await SeedThingAsync(provider, ct);
        var relay = Relay(provider);

        // The translator succeeded, so its integration row is staged even though the drain records a
        // failure for the domain message. Discarding it would lose the event outright, because the retry
        // skips the translator.
        await relay.DrainAsync(ct);
        (await IntegrationRowsAsync(provider, ct)).Should().ContainSingle(
            "the succeeded translator's output must survive its sibling's failure");

        flaky.ShouldThrow = false;
        time.Advance(TimeSpan.FromSeconds(31));
        await relay.DrainAsync(ct);

        (await IntegrationRowsAsync(provider, ct)).Should().ContainSingle(
            "the retry skipped the translator, so it must not stage a duplicate");
        (await SingleDomainRowAsync(provider, ct)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_permanently_failing_handler_parks_the_message_after_MaxAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var logs = new List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)>();
        var flaky = new FlakyHandlerState();
        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(
            connection,
            time,
            services =>
            {
                services.AddSingleton(flaky);
                services.AddSingleton<ILogger<OutboxRelay<OutboxTestDbContext>>>(
                    new FakeLogger<OutboxRelay<OutboxTestDbContext>>(logs));
                services.AddDomainEventHandler<ThingCreated, FlakyHandler>();
            },
            o => o.MaxAttempts = 2);

        await SeedThingAsync(provider, ct);
        var relay = Relay(provider);

        await relay.DrainAsync(ct);
        time.Advance(TimeSpan.FromMinutes(5));
        await relay.DrainAsync(ct);

        var parked = await SingleDomainRowAsync(provider, ct);
        parked.Attempts.Should().Be(2);
        parked.ProcessedAt.Should().BeNull();

        logs.Where(e => e.EventId.Name == "OutboxRelay.MessageParked").Should().ContainSingle(
            "a handler that never succeeds must raise the alertable dead-letter signal");

        // Parked means "skipped by the scan", so it never blocks later messages.
        time.Advance(TimeSpan.FromHours(24));
        (await relay.DrainAsync(ct)).Should().Be(0);
        flaky.Invocations.Should().Be(2, "a parked message is not retried");
    }

    [Fact]
    public async Task Replaying_a_parked_message_does_not_rerun_handlers_that_already_succeeded()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var stable = new HandlerCallCount();
        var flaky = new FlakyHandlerState();
        var time = new FakeTimeProvider(Start);
        await using var provider = BuildProvider(
            connection,
            time,
            services =>
            {
                services.AddSingleton(stable);
                services.AddSingleton(flaky);
                services.AddDomainEventHandler<ThingCreated, StableHandler>();
                services.AddDomainEventHandler<ThingCreated, FlakyHandler>();
            },
            o => o.MaxAttempts = 1);

        await SeedThingAsync(provider, ct);
        var relay = Relay(provider);

        await relay.DrainAsync(ct);
        (await SingleDomainRowAsync(provider, ct)).Attempts.Should().Be(1, "it is parked at MaxAttempts = 1");

        // An operator fixes the downstream cause and replays. Replay must not re-run the handler that
        // already succeeded, so the progress record deliberately survives the reset.
        flaky.ShouldThrow = false;
        await using (var scope = provider.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IOutboxMaintenance>();
            (await maintenance.ReplayAllAsync(ct)).Should().Be(1);
        }

        time.Advance(TimeSpan.FromSeconds(31));
        (await relay.DrainAsync(ct)).Should().Be(1);

        stable.Invocations.Should().Be(1, "replay must not duplicate a side effect that already happened");
        flaky.Invocations.Should().Be(2);
        (await SingleDomainRowAsync(provider, ct)).ProcessedAt.Should().NotBeNull();
    }

    private static ServiceProvider BuildProvider(
        SqliteConnection connection,
        FakeTimeProvider time,
        Action<IServiceCollection> configureHandlers,
        Action<OutboxOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        configureHandlers(services);
        services.AddTrellisOutbox<OutboxTestDbContext>(o =>
        {
            o.RetryBackoff = TimeSpan.FromSeconds(30);
            o.RetryBackoffJitter = 0;
            configureOptions?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    private static async Task SeedThingAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        await context.Database.EnsureCreatedAsync(ct);
        context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "thing", DateTimeOffset.UnixEpoch));
        await context.SaveChangesAsync(ct);
    }

    private static OutboxRelay<OutboxTestDbContext> Relay(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<OutboxRelay<OutboxTestDbContext>>().Single();

    private static async Task<OutboxMessage> SingleDomainRowAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        return await context.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Kind == OutboxMessageKind.Domain)
            .SingleAsync(ct);
    }

    private static async Task<List<OutboxMessage>> IntegrationRowsAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        return await context.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Kind == OutboxMessageKind.Integration)
            .OrderBy(m => m.Sequence)
            .ToListAsync(ct);
    }
}

internal sealed class HandlerCallCount
{
    public int Invocations { get; set; }
}

internal sealed class FlakyHandlerState
{
    public int Invocations { get; set; }

    public bool ShouldThrow { get; set; } = true;
}

// Always succeeds; its invocation count proves a retry does not re-run it.
internal sealed class StableHandler(HandlerCallCount count) : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        count.Invocations++;
        return ValueTask.CompletedTask;
    }
}

// Fails until the test flips ShouldThrow, standing in for a transient downstream outage.
internal sealed class FlakyHandler(FlakyHandlerState state) : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        state.Invocations++;
        return state.ShouldThrow
            ? throw new InvalidOperationException("downstream unavailable")
            : ValueTask.CompletedTask;
    }
}
