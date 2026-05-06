// Cookbook Recipe 12 — DI wiring playbook.
namespace CookbookSnippets.Recipe12;

using CookbookSnippets.Recipe01;
using CookbookSnippets.Recipe02;
using CookbookSnippets.Recipe07;
using CookbookSnippets.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Routing;
using Trellis.EntityFrameworkCore;
using Trellis.ServiceDefaults;

public static class CompositionRoot
{
    public static IServiceCollection AddApp(this IServiceCollection services, string connectionString)
    {
        // 1. Trellis composition root: ASP, Mediator behaviors, FluentValidation, claims-based
        //    actor provider, resource authorization, and EF unit of work in canonical order.
        //    UseEntityFrameworkUnitOfWork<TContext> is applied last so TransactionalCommandBehavior
        //    lands innermost in the mediator pipeline.
        services.AddTrellis(options => options
            .UseAsp()
            .UseMediator()
            .UseFluentValidation(typeof(PlaceOrderValidator).Assembly)
            .UseClaimsActorProvider()
            .UseResourceAuthorization(typeof(UpdateOrderCommand).Assembly)
            .UseEntityFrameworkUnitOfWork<AppDbContext>());

        // 2. EF Core context with Trellis interceptors + conventions.
        //    DbContext registration is application-owned (provider, connection string, pooling).
        services.AddDbContext<AppDbContext>(opts => opts
            .UseInMemoryDatabase("CookbookSample")
            .AddTrellisInterceptors());

        // 3. Optional: route constraints for value-object IDs (reflection-based; not part of
        //    AddTrellis because route parameter names are application-owned).
        services.AddTrellisRouteConstraints(typeof(OrderId).Assembly);

        // 4. Application services.
        services.AddScoped<IOrderRepository, EfOrderRepository>();

        return services;
    }
}