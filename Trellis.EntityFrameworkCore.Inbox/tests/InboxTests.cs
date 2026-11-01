namespace Trellis.EntityFrameworkCore.Inbox.Tests;

using System.Diagnostics;
using global::Trellis.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        var first = await dispatcher.DispatchAsync(envelope, ct);
        var second = await dispatcher.DispatchAsync(envelope, ct); // redelivery of the same message

        first.Should().Be(InboxDispatchOutcome.Processed, "the first delivery runs the handlers");
        second.Should().Be(InboxDispatchOutcome.SkippedDuplicate, "the redelivery is a recognized no-op");

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
    public async Task Dispatch_without_a_registered_handler_throws_without_recording_the_message()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProviderWithoutHandlers(connection, "billing");
        await EnsureCreatedAsync(provider, ct);
        var envelope = Envelope();

        var act = async () => await provider.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no registered*handler*");

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
            (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(0, "an unhandled message must remain eligible for redelivery");
        }

        await using var configuredProvider = BuildProvider(connection, "billing");
        (await configuredProvider.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct))
            .Should().Be(InboxDispatchOutcome.Processed, "redelivery succeeds after the missing handler is registered");
    }

    [Fact]
    public async Task Dispatch_duplicate_skips_even_when_the_original_handler_is_no_longer_registered()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var envelope = Envelope();

        await using (var provider = BuildProvider(connection, "billing"))
        {
            await EnsureCreatedAsync(provider, ct);
            (await provider.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct))
                .Should().Be(InboxDispatchOutcome.Processed);
        }

        await using var providerWithoutHandlers = BuildProviderWithoutHandlers(connection, "billing");
        (await providerWithoutHandlers.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct))
            .Should().Be(InboxDispatchOutcome.SkippedDuplicate);
    }

    [Fact]
    public async Task Dispatch_rejects_an_inbox_store_override_that_cannot_share_its_EF_unit_of_work()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlite(connection));
        services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, ReceiptHandler>();
        services.AddTrellisInbox<InboxTestDbContext>(o => o.ConsumerId = "billing");
        services.AddScoped<IInboxStore, IndependentInboxStore>();
        await using var provider = services.BuildServiceProvider();
        await EnsureCreatedAsync(provider, ct);

        var act = async () => await provider.GetRequiredService<IInboxDispatcher>().DispatchAsync(Envelope(), ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*same unit of work*IInboxStore*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_HandlerFails_NoWritesCommittedAndRedeliverySucceeds(bool failAsynchronously)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var gate = new FailFirstGate(failAsynchronously);
        await using var provider = BuildProvider(connection, "billing", gate: gate);
        await EnsureCreatedAsync(provider, ct);
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var envelope = Envelope();

        var firstAttempt = dispatcher.DispatchAsync(envelope, ct);
        try
        {
            if (failAsynchronously)
            {
                await gate.AsynchronousFailureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                firstAttempt.IsCompleted.Should().BeFalse("dispatch must await the pending handler");
            }
        }
        finally
        {
            if (failAsynchronously)
                gate.PendingFailure.TrySetException(gate.Failure);

            var act = async () => await firstAttempt;
            var exception = await act.Should().ThrowAsync<InvalidOperationException>("the first attempt fails transiently");
            exception.Which.Should().BeSameAs(gate.Failure);
        }

        await using (var failedScope = provider.CreateAsyncScope())
        {
            var db = failedScope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
            (await db.Receipts.CountAsync(ct)).Should().Be(0, "the first sibling's writes must not commit");
            (await db.Ledgers.CountAsync(ct)).Should().Be(0, "the failing sibling's writes must not commit");
            (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(0, "a failed message remains eligible for redelivery");
        }

        (await dispatcher.DispatchAsync(envelope, ct)).Should().Be(InboxDispatchOutcome.Processed);

        await using var scope = provider.CreateAsyncScope();
        var committed = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await committed.Receipts.CountAsync(ct)).Should().Be(1, "the first sibling commits only on successful redelivery");
        (await committed.Ledgers.CountAsync(ct)).Should().Be(1, "the second sibling commits only on successful redelivery");
        (await committed.Set<InboxMessage>().CountAsync(ct)).Should().Be(1);
        gate.Calls.Should().Be(2);
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
    public async Task TryRecordAsync_requires_a_consumerId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var record = new InboxRecord(Guid.CreateVersion7(), "event", DateTimeOffset.UnixEpoch);
        var act = async () => await store.TryRecordAsync(" ", record, ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Inbox_store_rejects_a_consumerId_longer_than_the_key_column()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection, "billing");
        await EnsureCreatedAsync(provider, ct);

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var tooLong = new string('x', InboxOptions.MaxConsumerIdLength + 1);
        var record = new InboxRecord(Guid.CreateVersion7(), "event", DateTimeOffset.UnixEpoch);

        var recordAct = async () => await store.TryRecordAsync(tooLong, record, ct);
        var filterAct = async () => await store.FilterUnprocessedAsync(tooLong, [record.MessageId], ct);

        await recordAct.Should().ThrowAsync<ArgumentException>().WithMessage($"*{InboxOptions.MaxConsumerIdLength}*");
        await filterAct.Should().ThrowAsync<ArgumentException>().WithMessage($"*{InboxOptions.MaxConsumerIdLength}*");
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

    [Fact]
    public async Task Dispatch_continues_the_remote_trace_and_exposes_inbound_lineage_through_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var probe = new ProcessingProbe();
        var interceptor = new ProcessingCommitInterceptor();

        // A uniquely-named, test-owned source rather than the dispatcher's shared default: an
        // ActivityListener subscribes process-wide by source name, so sharing the default with a
        // concurrently-running test (e.g. InboxSqlServerIntegrationTests) risks this listener also
        // observing that test's unrelated activities.
        using var testActivitySource = new ActivitySource("InboxTests.RemoteTrace");
        await using var provider = BuildProvider(connection, "billing", probe: probe, interceptor: interceptor, activitySource: testActivitySource);
        await EnsureCreatedAsync(provider, ct);

        bool? sampledRemoteParent = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, testActivitySource),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                sampledRemoteParent = options.Parent.IsRemote;
                return ActivitySamplingResult.AllDataAndRecorded;
            },
        };
        ActivitySource.AddActivityListener(listener);

        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceParent = $"00-{traceId}-{spanId}-01";
        var messageId = Guid.CreateVersion7();
        var envelope = Envelope() with { MessageId = messageId, TraceParent = traceParent, TraceState = "vendor=key" };
        envelope = envelope with { CorrelationId = "workflow-42" };

        var priorActivity = Activity.Current;
        using (IntegrationMessageContext.BeginCorrelation("outer-workflow"))
        {
            (await provider.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct))
                .Should().Be(InboxDispatchOutcome.Processed);

            probe.Attempts.Should().ContainSingle();
            var seen = probe.Attempts.Single();
            seen.MessageId.Should().Be(messageId);
            seen.CorrelationId.Should().Be("workflow-42");
            seen.TraceParent.Should().Be(traceParent);
            seen.TraceState.Should().Be("vendor=key");
            seen.ActivityTraceId.Should().Be(traceId);
            seen.ParentSpanId.Should().Be(spanId);
            seen.ActivityTraceState.Should().Be("vendor=key");
            sampledRemoteParent.Should().BeTrue();
            interceptor.MessageIdAtCommit.Should().Be(messageId);
            interceptor.CorrelationIdAtCommit.Should().Be("workflow-42");
            IntegrationMessageContext.CurrentMessageId.Should().BeNull();
            IntegrationMessageContext.CorrelationId.Should().Be("outer-workflow");
        }

        IntegrationMessageContext.CurrentMessageId.Should().BeNull();
        IntegrationMessageContext.CorrelationId.Should().BeNull();
        Activity.Current.Should().BeSameAs(priorActivity);
    }

    [Fact]
    public async Task Dispatch_retry_and_redelivery_restore_ambient_context()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var probe = new ProcessingProbe(failFirst: true);
        await using var provider = BuildProvider(connection, "billing", probe: probe);
        await EnsureCreatedAsync(provider, ct);
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var envelope = Envelope() with { CorrelationId = "workflow-retry" };

        using (IntegrationMessageContext.BeginCorrelation("outer"))
        {
            var first = async () => await dispatcher.DispatchAsync(envelope, ct);
            await first.Should().ThrowAsync<InvalidOperationException>();
            IntegrationMessageContext.CurrentMessageId.Should().BeNull();
            IntegrationMessageContext.CorrelationId.Should().Be("outer");

            (await dispatcher.DispatchAsync(envelope, ct)).Should().Be(InboxDispatchOutcome.Processed);
            (await dispatcher.DispatchAsync(envelope, ct)).Should().Be(InboxDispatchOutcome.SkippedDuplicate);
            IntegrationMessageContext.CurrentMessageId.Should().BeNull();
            IntegrationMessageContext.CorrelationId.Should().Be("outer");
        }

        probe.Attempts.Should().HaveCount(2);
        probe.Attempts.Should().OnlyContain(a => a.MessageId == envelope.MessageId && a.CorrelationId == "workflow-retry");
        IntegrationMessageContext.CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task Dispatch_uses_explicit_correlation_when_inbound_correlation_is_blank()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var probe = new ProcessingProbe();
        await using var provider = BuildProvider(connection, "billing", probe: probe);
        await EnsureCreatedAsync(provider, ct);
        var envelope = Envelope() with { CorrelationId = " " };

        using (IntegrationMessageContext.BeginCorrelation("application-workflow"))
        {
            await provider.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct);
            probe.Attempts.Single().CorrelationId.Should().Be("application-workflow");
        }

        IntegrationMessageContext.CorrelationId.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("invalid-traceparent", "invalid-tracestate")]
    [InlineData("", "")]
    public async Task Dispatch_missing_or_malformed_trace_metadata_still_processes(
        string? traceParent, string? traceState)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var probe = new ProcessingProbe();
        await using var provider = BuildProvider(connection, "billing", probe: probe);
        await EnsureCreatedAsync(provider, ct);
        var envelope = Envelope() with { TraceParent = traceParent, TraceState = traceState };

        var priorActivity = Activity.Current;
        (await provider.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct))
            .Should().Be(InboxDispatchOutcome.Processed);

        probe.Attempts.Should().ContainSingle();
        probe.Attempts.Single().CorrelationId.Should().BeNull();
        IntegrationMessageContext.CurrentMessageId.Should().BeNull();
        IntegrationMessageContext.CorrelationId.Should().BeNull();
        Activity.Current.Should().BeSameAs(priorActivity);
    }

    [Fact]
    public async Task Dispatch_invalid_tracestate_does_not_prevent_processing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var probe = new ProcessingProbe();
        await using var provider = BuildProvider(connection, "billing", probe: probe);
        await EnsureCreatedAsync(provider, ct);
        var validParent = $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
        var envelope = Envelope() with { TraceParent = validParent, TraceState = "invalid=;" };

        (await provider.GetRequiredService<IInboxDispatcher>().DispatchAsync(envelope, ct))
            .Should().Be(InboxDispatchOutcome.Processed);

        probe.Attempts.Should().ContainSingle();
        IntegrationMessageContext.CurrentMessageId.Should().BeNull();
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
        SqliteConnection connection, string consumerId, FailFirstGate? gate = null,
        ProcessingProbe? probe = null, ProcessingCommitInterceptor? interceptor = null,
        ActivitySource? activitySource = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o =>
        {
            o.UseSqlite(connection);
            if (interceptor is not null)
                o.AddInterceptors(interceptor);
        });

        if (probe is not null)
        {
            services.AddSingleton(probe);
            services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, ProcessingProbeHandler>();
        }
        else if (gate is not null)
        {
            services.AddSingleton(gate);
            services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, ReceiptHandler>();
            services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, FailFirstHandler>();
        }
        else
        {
            services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, ReceiptHandler>();
        }

        services.AddTrellisInbox<InboxTestDbContext>(o =>
        {
            o.ConsumerId = consumerId;
            if (activitySource is not null)
                o.ActivitySource = activitySource;
        });
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildProviderWithoutHandlers(SqliteConnection connection, string consumerId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlite(connection));
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

internal sealed class FailFirstGate(bool failAsynchronously)
{
    public int Calls;
    public bool FailAsynchronously { get; } = failAsynchronously;
    public InvalidOperationException Failure { get; } = new("transient boom");
    public TaskCompletionSource AsynchronousFailureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource PendingFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class IndependentInboxStore : IInboxStore
{
    public Task<bool> TryRecordAsync(string consumerId, InboxRecord record, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task<IReadOnlyList<Guid>> FilterUnprocessedAsync(
        string consumerId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. messageIds]);
}

internal sealed record ProcessingSnapshot(
    Guid? MessageId, string? CorrelationId, string? TraceParent, string? TraceState,
    ActivityTraceId? ActivityTraceId, ActivitySpanId? ParentSpanId, string? ActivityTraceState);

internal sealed class ProcessingProbe(bool failFirst = false)
{
    public List<ProcessingSnapshot> Attempts { get; } = [];
    public bool FailFirst { get; } = failFirst;
}

internal sealed class ProcessingProbeHandler(ProcessingProbe probe) : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        probe.Attempts.Add(new ProcessingSnapshot(
            IntegrationMessageContext.CurrentMessageId, IntegrationMessageContext.CorrelationId,
            IntegrationMessageContext.TraceParent, IntegrationMessageContext.TraceState,
            activity?.TraceId, activity?.ParentSpanId, activity?.TraceStateString));
        if (probe.FailFirst && probe.Attempts.Count == 1)
            throw new InvalidOperationException("transient handler failure");
        return ValueTask.CompletedTask;
    }
}

internal sealed class ProcessingCommitInterceptor : SaveChangesInterceptor
{
    public Guid? MessageIdAtCommit { get; private set; }
    public string? CorrelationIdAtCommit { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        MessageIdAtCommit = IntegrationMessageContext.CurrentMessageId;
        CorrelationIdAtCommit = IntegrationMessageContext.CorrelationId;
        return ValueTask.FromResult(result);
    }
}

// Fails the first delivery, succeeds the second — proving a redelivery reprocesses a previously-failed message.
internal sealed class FailFirstHandler(InboxTestDbContext context, FailFirstGate gate)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        context.Ledgers.Add(new Ledger { Id = Guid.NewGuid(), Entry = "redelivered" });
        if (gate.Calls++ != 0)
            return ValueTask.CompletedTask;
        if (gate.FailAsynchronously)
        {
            gate.AsynchronousFailureStarted.SetResult();
            return new ValueTask(gate.PendingFailure.Task);
        }

        throw gate.Failure;
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