namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using global::Trellis.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    [Fact]
    public async Task Relay_retries_then_parks_a_poison_message_without_blocking_others()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var captured = new List<ThingCreated>();
        var logs = new List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<OutboxRelay<OutboxTestDbContext>>>(
            new FakeLogger<OutboxRelay<OutboxTestDbContext>>(logs));
        services.AddSingleton(captured);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingCreated, CapturingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.MaxAttempts = 2);

        await using var provider = services.BuildServiceProvider();

        var poisonId = Guid.CreateVersion7();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);

            // Poison first (lowest Sequence) so it is drained before the good message — its event type
            // cannot be resolved, so the relay records a failure rather than marking it processed.
            context.Set<OutboxMessage>().Add(OutboxMessage.Create(
                poisonId,
                DateTimeOffset.UnixEpoch,
                "Trellis.Outbox.Tests.NoSuchEvent, Trellis.Outbox.Tests.NoSuchAssembly",
                "{}",
                OutboxMessageKind.Domain));
            await context.SaveChangesAsync(ct);

            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "good", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        // Drain 1: poison fails (Attempts -> 1, transient); the good message behind it is still dispatched.
        await relay.DrainAsync(ct);
        captured.Should().ContainSingle().Which.Name.Should().Be("good");

        // Drain 2: poison fails again (Attempts -> 2 == MaxAttempts) and is parked thereafter.
        await relay.DrainAsync(ct);

        // Drain 3: poison is now skipped (Attempts == MaxAttempts) and the good row is processed — nothing left.
        (await relay.DrainAsync(ct)).Should().Be(0);

        await using (var verify = provider.CreateAsyncScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var rows = await context.Set<OutboxMessage>().AsNoTracking().ToListAsync(ct);

            var good = rows.Single(r => r.EventType.Contains(nameof(ThingCreated)));
            var poison = rows.Single(r => r.EventType.Contains("NoSuchEvent"));

            good.ProcessedAt.Should().NotBeNull();
            poison.ProcessedAt.Should().BeNull("the poison message exhausted its attempts and is parked");
            poison.Attempts.Should().Be(2);
            poison.LastError.Should().NotBeNullOrEmpty();
        }

        // Production supportability: the first failure logs as a retryable Warning; exhausting
        // MaxAttempts logs a distinct, alertable Error that identifies the parked message — both with
        // the underlying exception attached so on-call can triage from the log alone.
        var attemptFailed = logs.Where(e => e.EventId.Name == "OutboxRelay.RelayAttemptFailed").ToList();
        attemptFailed.Should().ContainSingle();
        attemptFailed[0].Level.Should().Be(LogLevel.Warning);
        attemptFailed[0].Message.Should().Contain("attempt 1 of 2");
        attemptFailed[0].Exception.Should().NotBeNull();

        var parked = logs.Where(e => e.EventId.Name == "OutboxRelay.MessageParked").ToList();
        parked.Should().ContainSingle();
        parked[0].Level.Should().Be(LogLevel.Error);
        parked[0].Message.Should().Contain(poisonId.ToString());
        parked[0].Message.Should().Contain("NoSuchEvent");
        parked[0].Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task Relay_marks_processed_when_a_handler_throws()
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
        services.AddDomainEventHandler<ThingCreated, ThrowingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "boom", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        var drained = await relay.DrainAsync(ct);

        // Delivery, not handler success: the publisher swallows the handler exception, so the relay
        // marks the message processed (no retry) rather than recording a failure.
        drained.Should().Be(1);
        await using (var verify = provider.CreateAsyncScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var row = await context.Set<OutboxMessage>().SingleAsync(ct);
            row.ProcessedAt.Should().NotBeNull();
            row.Attempts.Should().Be(0);
        }

        // A swallowed handler exception must NOT surface as a relay failure/parked log — that noise
        // would mislead on-call into chasing a delivery problem that does not exist.
        logs.Where(e => e.EventId.Name is "OutboxRelay.RelayAttemptFailed" or "OutboxRelay.MessageParked")
            .Should().BeEmpty();
    }

    [Fact]
    public void AddTrellisOutbox_WithInvalidOptions_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var zeroBatch = () => services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 0);
        zeroBatch.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("BatchSize");

        var negativePoll = () => services.AddTrellisOutbox<OutboxTestDbContext>(o => o.PollInterval = TimeSpan.FromSeconds(-1));
        negativePoll.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("PollInterval");

        var zeroAttempts = () => services.AddTrellisOutbox<OutboxTestDbContext>(o => o.MaxAttempts = 0);
        zeroAttempts.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxAttempts");
    }

    [Fact]
    public async Task Relay_translates_a_domain_event_into_an_integration_event_and_publishes_it()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var publishedIntegration = new List<ThingCreatedIntegrationEvent>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(publishedIntegration);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        // The translator is a domain-event handler that emits an integration event into the collector.
        services.AddDomainEventHandler<ThingCreated, ThingCreatedTranslator>();
        services.AddIntegrationEventHandler<ThingCreatedIntegrationEvent, IntegrationCapturingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        var id = ThingId.NewUniqueV7();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            context.Things.Add(Thing.Create(id, "translate-me", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        // Drain 1: the domain row is dispatched, the translator runs, and the integration event it
        // produced is staged as a new Integration row — not yet published.
        await relay.DrainAsync(ct);
        publishedIntegration.Should().BeEmpty();

        await using (var verify = provider.CreateAsyncScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var rows = await context.Set<OutboxMessage>().AsNoTracking().OrderBy(r => r.Sequence).ToListAsync(ct);
            rows.Should().HaveCount(2);
            var domain = rows.Single(r => r.Kind == OutboxMessageKind.Domain);
            var integration = rows.Single(r => r.Kind == OutboxMessageKind.Integration);
            domain.ProcessedAt.Should().NotBeNull("the domain event was dispatched");
            integration.ProcessedAt.Should().BeNull("the integration event is staged but not yet relayed");
            integration.EventType.Should().Contain(nameof(ThingCreatedIntegrationEvent));
        }

        // Drain 2: the staged Integration row is published to the integration handler.
        await relay.DrainAsync(ct);

        publishedIntegration.Should().ContainSingle().Which.Name.Should().Be("translate-me");
        await using (var verify = provider.CreateAsyncScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var integration = await context.Set<OutboxMessage>().AsNoTracking()
                .SingleAsync(r => r.Kind == OutboxMessageKind.Integration, ct);
            integration.ProcessedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Relay_does_not_stage_partial_integration_rows_when_one_fails_to_serialize()
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
        // The translator emits a serializable integration event AND one that throws while serializing.
        services.AddDomainEventHandler<ThingCreated, PartiallyFailingTranslator>();
        services.AddIntegrationEventDispatch();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "partial", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        await relay.DrainAsync(ct);

        // Per-message atomicity: because the second produced event fails to serialize, NONE of the
        // message's integration rows are staged, and the domain message is recorded as failed (not
        // processed) so a retry re-translates the whole set rather than duplicating the first event.
        await using (var verify = provider.CreateAsyncScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var rows = await context.Set<OutboxMessage>().AsNoTracking().ToListAsync(ct);
            rows.Should().ContainSingle("only the original domain row exists; no partial integration rows were staged");
            var domain = rows.Single();
            domain.Kind.Should().Be(OutboxMessageKind.Domain);
            domain.ProcessedAt.Should().BeNull("the domain message failed and must retry");
            domain.Attempts.Should().Be(1);
        }
    }

    [Fact]
    public async Task Relay_roundtrips_a_domain_event_with_a_None_optional_member()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var captured = new List<ThingTagged>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(captured);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingTagged, TaggedCapturingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            var thing = Thing.Create(ThingId.NewUniqueV7(), "untagged", DateTimeOffset.UnixEpoch);
            context.Things.Add(thing);
            await context.SaveChangesAsync(ct);

            // Capturing an event whose Maybe<T> member is None must not throw: the default serializer
            // walked Maybe<T>.Value and threw on the absent value.
            thing.Tag(Maybe<TeamId>.None, DateTimeOffset.UnixEpoch);
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        await relay.DrainAsync(ct);

        var tagged = captured.Should().ContainSingle().Subject;
        tagged.OwningTeam.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task Relay_roundtrips_a_domain_event_with_a_Some_optional_member()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var captured = new List<ThingTagged>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(captured);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<ThingTagged, TaggedCapturingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        var teamId = TeamId.NewUniqueV7();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            var thing = Thing.Create(ThingId.NewUniqueV7(), "tagged", DateTimeOffset.UnixEpoch);
            context.Things.Add(thing);
            await context.SaveChangesAsync(ct);

            thing.Tag(Maybe<TeamId>.From(teamId), DateTimeOffset.UnixEpoch);
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        await relay.DrainAsync(ct);

        var tagged = captured.Should().ContainSingle().Subject;
        tagged.OwningTeam.HasValue.Should().BeTrue();
        tagged.OwningTeam.Value.Should().Be(teamId);
    }

    [Fact]
    public async Task Relay_roundtrips_an_integration_event_with_optional_members()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var captured = new List<ThingTaggedIntegrationEvent>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(captured);
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());
        services.AddDomainEventDispatch();
        // The translator emits an integration event carrying the domain event's Maybe<TeamId> member,
        // so the relay's CreateIntegrationRow (serialize) and Deserialize must round-trip it too.
        services.AddDomainEventHandler<ThingTagged, ThingTaggedTranslator>();
        services.AddIntegrationEventHandler<ThingTaggedIntegrationEvent, IntegrationTaggedCapturingHandler>();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        var teamId = TeamId.NewUniqueV7();
        var noneId = ThingId.NewUniqueV7();
        var someId = ThingId.NewUniqueV7();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);

            var none = Thing.Create(noneId, "none", DateTimeOffset.UnixEpoch);
            context.Things.Add(none);
            await context.SaveChangesAsync(ct);
            none.Tag(Maybe<TeamId>.None, DateTimeOffset.UnixEpoch);
            await context.SaveChangesAsync(ct);

            var some = Thing.Create(someId, "some", DateTimeOffset.UnixEpoch);
            context.Things.Add(some);
            await context.SaveChangesAsync(ct);
            some.Tag(Maybe<TeamId>.From(teamId), DateTimeOffset.UnixEpoch);
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        // Drain 1: domain rows dispatch and the translator stages an Integration row per ThingTagged.
        // Drain 2: the staged Integration rows are deserialized and published to the integration handler.
        await relay.DrainAsync(ct);
        await relay.DrainAsync(ct);

        captured.Should().HaveCount(2);
        captured.Single(e => e.Id == noneId.Value).OwningTeam.HasValue.Should().BeFalse();
        captured.Single(e => e.Id == someId.Value).OwningTeam.Value.Should().Be(teamId);
    }

    [Fact]
    public async Task Relay_does_not_publish_rows_held_under_another_instances_active_lease()
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
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "beta", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);

            // Simulate another relay instance holding a live lease on the pending row.
            await context.Set<OutboxMessage>()
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LockedUntil, DateTime.UtcNow.AddMinutes(10))
                    .SetProperty(m => m.LockedBy, Guid.NewGuid()), ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        var drained = await relay.DrainAsync(ct);

        drained.Should().Be(0, "a row under another instance's live lease must not be re-claimed");
        captured.Should().BeEmpty("the leased row must not be double-published by this instance");
    }

    [Fact]
    public async Task Relay_reclaims_rows_whose_lease_has_expired()
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
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            context.Things.Add(Thing.Create(ThingId.NewUniqueV7(), "gamma", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);

            // Simulate a crashed instance that left an expired lease behind.
            await context.Set<OutboxMessage>()
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LockedUntil, DateTime.UtcNow.AddMinutes(-10))
                    .SetProperty(m => m.LockedBy, Guid.NewGuid()), ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        var drained = await relay.DrainAsync(ct);

        drained.Should().Be(1, "an expired lease must be reclaimed by the relay");
        captured.Should().ContainSingle().Which.Name.Should().Be("gamma");
    }

    [Fact]
    public async Task Relay_releases_the_lease_when_a_message_fails_to_relay()
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
        services.AddTrellisOutbox<OutboxTestDbContext>();

        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync(ct);
            // A row whose event type cannot be resolved fails deserialization in the relay.
            context.Set<OutboxMessage>().Add(OutboxMessage.Create(
                Guid.NewGuid(), DateTimeOffset.UnixEpoch,
                "Nonexistent.Type, Nonexistent.Assembly", "{}", OutboxMessageKind.Domain));
            await context.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>()
            .OfType<OutboxRelay<OutboxTestDbContext>>()
            .Single();

        var drained = await relay.DrainAsync(ct);

        drained.Should().Be(1);
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var row = await context.Set<OutboxMessage>().SingleAsync(ct);
            row.Attempts.Should().Be(1, "the failed relay attempt was recorded");
            row.ProcessedAt.Should().BeNull("a failed message stays pending");
            row.LockedUntil.Should().BeNull("a failed message releases its lease so any instance can retry it");
            row.LockedBy.Should().BeNull();
        }
    }
}

// ── Test domain model ──────────────────────────────────────────────────────────

internal sealed partial class ThingId : RequiredGuid<ThingId>;

internal sealed partial class TeamId : RequiredGuid<TeamId>;

internal sealed record ThingCreated(ThingId Id, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record ThingTagged(ThingId Id, Maybe<TeamId> OwningTeam, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record ThingCreatedIntegrationEvent(Guid Id, string Name, DateTimeOffset OccurredAt) : IIntegrationEvent;

internal sealed class ThingCreatedTranslator(IIntegrationEventCollector collector) : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        collector.Add(new ThingCreatedIntegrationEvent(domainEvent.Id.Value, domainEvent.Name, domainEvent.OccurredAt));
        return ValueTask.CompletedTask;
    }
}

internal sealed class IntegrationCapturingHandler(List<ThingCreatedIntegrationEvent> captured)
    : IIntegrationEventHandler<ThingCreatedIntegrationEvent>
{
    public ValueTask HandleAsync(ThingCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        captured.Add(integrationEvent);
        return ValueTask.CompletedTask;
    }
}

internal sealed record ThingTaggedIntegrationEvent(Guid Id, Maybe<TeamId> OwningTeam, DateTimeOffset OccurredAt) : IIntegrationEvent;

internal sealed class ThingTaggedTranslator(IIntegrationEventCollector collector) : IDomainEventHandler<ThingTagged>
{
    public ValueTask HandleAsync(ThingTagged domainEvent, CancellationToken cancellationToken)
    {
        collector.Add(new ThingTaggedIntegrationEvent(domainEvent.Id.Value, domainEvent.OwningTeam, domainEvent.OccurredAt));
        return ValueTask.CompletedTask;
    }
}

internal sealed class IntegrationTaggedCapturingHandler(List<ThingTaggedIntegrationEvent> captured)
    : IIntegrationEventHandler<ThingTaggedIntegrationEvent>
{
    public ValueTask HandleAsync(ThingTaggedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        captured.Add(integrationEvent);
        return ValueTask.CompletedTask;
    }
}

// An integration event whose property throws while System.Text.Json serializes it, so CreateIntegrationRow fails.
internal sealed record UnserializableIntegrationEvent(DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string Explode => throw new InvalidOperationException($"cannot serialize integration event from {OccurredAt:O}");
}

internal sealed class PartiallyFailingTranslator(IIntegrationEventCollector collector) : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        // A serializable event first, then one that throws during serialization.
        collector.Add(new ThingCreatedIntegrationEvent(domainEvent.Id.Value, domainEvent.Name, domainEvent.OccurredAt));
        collector.Add(new UnserializableIntegrationEvent(domainEvent.OccurredAt));
        return ValueTask.CompletedTask;
    }
}

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

    public void Tag(Maybe<TeamId> owningTeam, DateTimeOffset occurredAt) =>
        DomainEvents.Add(new ThingTagged(Id, owningTeam, occurredAt));
}

internal sealed class CapturingHandler(List<ThingCreated> captured) : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken)
    {
        captured.Add(domainEvent);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TaggedCapturingHandler(List<ThingTagged> captured) : IDomainEventHandler<ThingTagged>
{
    public ValueTask HandleAsync(ThingTagged domainEvent, CancellationToken cancellationToken)
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

internal sealed class ThrowingHandler : IDomainEventHandler<ThingCreated>
{
    public ValueTask HandleAsync(ThingCreated domainEvent, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("handler boom");
}

internal sealed class FakeLogger<T>(List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> entries)
    : ILogger<T>
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => entries.Add((logLevel, eventId, formatter(state, exception), exception));

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
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
