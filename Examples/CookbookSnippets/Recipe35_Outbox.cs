// Cookbook Recipe 35 — Transactional outbox for crash-safe domain events.
namespace CookbookSnippets.Recipe35;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.ServiceDefaults;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed record OrderPlaced(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id) : base(id) { }

    public void Place(DateTimeOffset now) => DomainEvents.Add(new OrderPlaced(Id, now));
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    // 1. Map the outbox table alongside your own configuration.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Order>().HasKey(o => o.Id);
        modelBuilder.AddTrellisOutbox();
    }
}

public static class OutboxWiring
{
    public static IServiceCollection Wire(IServiceCollection services)
    {
        // 2. Add the capture interceptor on the context options, after the provider call.
        services.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase("orders")
            .AddTrellisInterceptors()
            .AddTrellisOutboxInterceptor());

        // 3. Register the relay (UseOutbox) alongside your handlers (UseDomainEvents).
        services.AddTrellis(trellis => trellis
            .UseDomainEvents(typeof(OutboxWiring).Assembly)
            .UseEntityFrameworkUnitOfWork<AppDbContext>()
            .UseOutbox<AppDbContext>());

        return services;
    }
}