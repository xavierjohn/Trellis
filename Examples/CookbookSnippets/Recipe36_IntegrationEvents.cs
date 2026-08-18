// Cookbook Recipe 36 — Translating a domain event into an integration event.
namespace CookbookSnippets.Recipe36;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
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

// 2. The translator - a domain-event handler that emits the contract.
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

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public static class IntegrationEventWiring
{
    // 4. Wire it - translators are domain-event handlers; the outbox delivers both kinds.
    public static IServiceCollection Wire(IServiceCollection services) =>
        services.AddTrellis(trellis => trellis
            .UseDomainEvents(typeof(IntegrationEventWiring).Assembly)
            .UseIntegrationEvents(typeof(IntegrationEventWiring).Assembly)
            .UseEntityFrameworkUnitOfWork<AppDbContext>()
            .UseOutbox<AppDbContext>());
}
