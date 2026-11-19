namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Trellis.Mediator;

public sealed class LocalInboxDeliveryIntegrationTests
{
    [Fact]
    public async Task DrainAsync_LocalInboxWithNoHandler_ParksWithoutCreatingReceipt()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<DeliveryContext>(options => options.UseSqlite(connection))
            .AddDomainEventDispatch()
            .AddTrellisOutbox<DeliveryContext>(options => options.MaxAttempts = 1)
            .AddTrellisInbox<DeliveryContext>(options => options.ConsumerId = "local")
            .AddLocalInboxDelivery()
            .BuildServiceProvider(validateScopes: true);
        var message = new LocalEvent(DateTimeOffset.UnixEpoch);
        await using (var seed = provider.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<DeliveryContext>();
            await db.Database.EnsureCreatedAsync(ct);
            db.Add(OutboxMessage.Create(Guid.CreateVersion7(), message.OccurredAt,
                typeof(LocalEvent).AssemblyQualifiedName!, JsonSerializer.Serialize(message),
                OutboxMessageKind.Integration));
            await db.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>().OfType<OutboxRelay<DeliveryContext>>().Single();
        (await relay.DrainAsync(ct)).Should().Be(1);
        (await relay.DrainAsync(ct)).Should().Be(0);

        await using var scope = provider.CreateAsyncScope();
        var persisted = scope.ServiceProvider.GetRequiredService<DeliveryContext>();
        var row = await persisted.Set<OutboxMessage>().SingleAsync(ct);
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("has no registered").And.Contain(nameof(LocalEvent));
        (await persisted.Set<InboxMessage>().CountAsync(ct)).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DrainAsync_LocalInboxFailure_RetriesSameRowAndDeduplicatesRedelivery(bool failInHandler)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var state = new ConsumerState();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(state)
            .AddSingleton<TimeProvider>(time)
            .AddDbContext<DeliveryContext>(options => options.UseSqlite(connection))
            .AddDomainEventDispatch()
            .AddIntegrationEventHandler<LocalEvent, LocalHandler>()
            .AddTrellisOutbox<DeliveryContext>(options =>
            {
                options.RetryBackoff = TimeSpan.FromSeconds(30);
                options.RetryBackoffJitter = 0;
            })
            .AddTrellisInbox<DeliveryContext>(options => options.ConsumerId = "local")
            .AddLocalInboxDelivery();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var messageId = Guid.CreateVersion7();
        var message = new LocalEvent(time.GetUtcNow());
        var causationId = Guid.CreateVersion7();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryContext>();
            await db.Database.EnsureCreatedAsync(ct);
            db.Add(OutboxMessage.Create(messageId, message.OccurredAt,
                typeof(LocalEvent).AssemblyQualifiedName!, JsonSerializer.Serialize(message),
                OutboxMessageKind.Integration, "producer", causationId, "workflow"));
            if (!failInHandler)
                db.Add(new ConsumerWrite { Id = ConsumerWrite.BlockedId });
            await db.SaveChangesAsync(ct);
        }

        var relay = provider.GetServices<IHostedService>().OfType<OutboxRelay<DeliveryContext>>().Single();
        state.Fail = failInHandler;

        (await relay.DrainAsync(ct)).Should().Be(1);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryContext>();
            var row = await db.Set<OutboxMessage>().SingleAsync(ct);
            row.Id.Should().Be(messageId);
            row.ProcessedAt.Should().BeNull();
            row.Attempts.Should().Be(1);
            row.LastError.Should().NotBeNullOrEmpty();
            row.LockedUntil.Should().Be(time.GetUtcNow().UtcDateTime.AddSeconds(30));
            (await db.Set<InboxMessage>().CountAsync(ct)).Should().Be(0);
            (await db.Set<ConsumerWrite>().CountAsync(ct)).Should().Be(failInHandler ? 0 : 1);
            if (!failInHandler)
                await db.Set<ConsumerWrite>().ExecuteDeleteAsync(ct);
        }

        (await relay.DrainAsync(ct)).Should().Be(0);
        state.Fail = false;
        time.Advance(TimeSpan.FromSeconds(30));
        (await relay.DrainAsync(ct)).Should().Be(1);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryContext>();
            var row = await db.Set<OutboxMessage>().SingleAsync(ct);
            row.ProcessedAt.Should().Be(time.GetUtcNow());
            row.Attempts.Should().Be(1);
            row.LastError.Should().BeNull();
            var receipt = await db.Set<InboxMessage>().SingleAsync(ct);
            receipt.ConsumerId.Should().Be("local");
            receipt.MessageId.Should().Be(messageId);
            receipt.MessageSource.Should().Be("producer");
            receipt.CausationId.Should().Be(causationId);
            receipt.CorrelationId.Should().Be("workflow");
            (await db.Set<ConsumerWrite>().CountAsync(ct)).Should().Be(1);

            // Repeat the publication with the same id, as a relay would after a lost bookkeeping save.
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new OutboundIntegrationMessage(messageId, message), ct);
        }

        state.Calls.Should().Be(2);
    }

    private sealed class DeliveryContext(DbContextOptions<DeliveryContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddTrellisOutbox();
            modelBuilder.AddTrellisInbox();
            modelBuilder.Entity<ConsumerWrite>().Property(write => write.Id).ValueGeneratedNever();
        }
    }

    private sealed class ConsumerWrite
    {
        public const int BlockedId = 1;
        public int Id { get; set; }
    }

    private sealed class ConsumerState
    {
        public bool Fail { get; set; }
        public int Calls { get; set; }
    }

    private sealed record LocalEvent(DateTimeOffset OccurredAt) : IIntegrationEvent;

    private sealed class LocalHandler(DeliveryContext db, ConsumerState state) : IIntegrationEventHandler<LocalEvent>
    {
        public ValueTask HandleAsync(LocalEvent integrationEvent, CancellationToken cancellationToken)
        {
            state.Calls++;
            db.Add(new ConsumerWrite { Id = ConsumerWrite.BlockedId });
            if (state.Fail)
                throw new InvalidOperationException("Consumer failed.");

            return ValueTask.CompletedTask;
        }
    }
}