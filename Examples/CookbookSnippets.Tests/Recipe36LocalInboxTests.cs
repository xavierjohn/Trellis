namespace CookbookSnippets.Tests;

using CookbookSnippets.Recipe36;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

public sealed class Recipe36LocalInboxTests
{
    [Fact]
    public async Task WireLocalInbox_HandlerFailure_RetryCommitsOnceAndRedeliverySkips()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);

        var services = new ServiceCollection().AddLogging();
        IntegrationEventWiring.WireLocalInbox(services, options => options.UseSqlite(connection));
        var failOnce = new FailOnceHandler();
        // Register after StageShipmentHandler so the failure occurs after staging the shipment.
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedIntegrationEvent>>(failOnce);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync(ct);

        var integrationEvent = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(), "customer@example.com", 12.50m, DateTimeOffset.UnixEpoch);
        var message = new OutboundIntegrationMessage(Guid.CreateVersion7(), integrationEvent);
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(message, ct).AsTask());
        (await db.ShipmentRequests.AnyAsync(ct)).Should().BeFalse();
        (await db.Set<InboxMessage>().AnyAsync(ct)).Should().BeFalse();

        await publisher.PublishAsync(message, ct);
        await publisher.PublishAsync(message, ct);

        var shipment = await db.ShipmentRequests.SingleAsync(ct);
        shipment.OrderId.Should().Be(integrationEvent.OrderId);
        var receipt = await db.Set<InboxMessage>().SingleAsync(ct);
        receipt.ConsumerId.Should().Be("shipping");
        receipt.MessageId.Should().Be(message.MessageId);
        failOnce.Calls.Should().Be(2);
    }

    private sealed class FailOnceHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
    {
        public int Calls { get; private set; }

        public ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            if (++Calls == 1)
                throw new InvalidOperationException("Simulated failure after the shipment is staged.");

            return ValueTask.CompletedTask;
        }
    }
}