// Cookbook Recipe 36 — Translating a domain event into an integration event.
namespace CookbookSnippets.Recipe36;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.ServiceDefaults;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed partial class CustomerEmail : RequiredString<CustomerEmail>;

public sealed record Money(decimal Amount, string Currency);

public sealed record OrderPlaced(
    OrderId OrderId,
    CustomerEmail CustomerEmail,
    Money Total,
    DateTimeOffset OccurredAt) : IDomainEvent;

// 1. The external contract - primitive/nullable members, no internal value objects.
public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId,
    string CustomerEmail,
    decimal Total,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

// 2. The relay invokes this translator with an active collection lease.
// Calling it directly or through ordinary in-process domain dispatch is rejected.
public sealed class OrderPlacedTranslator(IIntegrationEventCollector collector)
    : IDomainEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        collector.Add(new OrderPlacedIntegrationEvent(
            domainEvent.OrderId.Value,
            domainEvent.CustomerEmail.Value,
            domainEvent.Total.Amount,
            domainEvent.OccurredAt));
        return ValueTask.CompletedTask;
    }
}

// 3. An in-process consumer (or replace IIntegrationEventPublisher with a broker adapter).
public sealed class NotifyShippingHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(
        OrderPlacedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed record ShipmentRequest(Guid Id, Guid OrderId);

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShipmentRequest> ShipmentRequests => Set<ShipmentRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddTrellisOutbox();
        modelBuilder.AddTrellisInbox();
    }
}

public sealed class StageShipmentHandler(AppDbContext db)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        db.ShipmentRequests.Add(new ShipmentRequest(Guid.NewGuid(), integrationEvent.OrderId));
        return ValueTask.CompletedTask;
    }
}

public static class IntegrationEventWiring
{
    // 4. Add to Recipe 35's persistence/capture setup; the outbox delivers both kinds.
    public static IServiceCollection Wire(IServiceCollection services) =>
        services.AddTrellis(trellis => trellis
            .UseDomainEvents<OrderPlaced, OrderPlacedTranslator>()
            .UseIntegrationEvents<OrderPlacedIntegrationEvent, NotifyShippingHandler>()
            .UseEntityFrameworkUnitOfWork<AppDbContext>()
            .UseOutbox<AppDbContext>());

    public static IServiceCollection WireLocalInbox(
        IServiceCollection services, Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            configureDatabase(options);
            options.AddTrellisInterceptors().AddTrellisOutboxInterceptor();
        });

        return services.AddTrellis(trellis => trellis
            .UseDomainEvents<OrderPlaced, OrderPlacedTranslator>()
            .UseIntegrationEvents<OrderPlacedIntegrationEvent, StageShipmentHandler>()
            .UseEntityFrameworkUnitOfWork<AppDbContext>()
            .UseOutbox<AppDbContext>()
            .UseInbox<AppDbContext>(options => options.ConsumerId = "shipping")
            .UseLocalInboxDelivery());
    }
}