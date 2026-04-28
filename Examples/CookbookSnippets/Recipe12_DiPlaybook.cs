// Cookbook Recipe 12 — DI wiring playbook.
namespace CookbookSnippets.Recipe12;

using CookbookSnippets.Recipe01;
using CookbookSnippets.Recipe02;
using CookbookSnippets.Recipe07;
using CookbookSnippets.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Asp.Routing;
using Trellis.EntityFrameworkCore;
using Trellis.FluentValidation;
using Trellis.Mediator;

public static class CompositionRoot
{
    public static IServiceCollection AddApp(this IServiceCollection services, string connectionString)
    {
        // 1. Mediator pipeline (outermost behaviors first).
        services.AddTrellisBehaviors();

        // 2. FluentValidation plug-in. Idempotent; safe to call after AddTrellisBehaviors.
        services.AddTrellisFluentValidation(typeof(PlaceOrderValidator).Assembly);

        // 3. ASP layer: Problem Details mapping + scalar-value validation pipeline.
        services.AddTrellisAsp();

        // 4. ASP authorization actor providers.
        services.AddClaimsActorProvider();
        services.AddResourceAuthorization(typeof(UpdateOrderCommand).Assembly);

        // 5. EF Core context with Trellis interceptors + conventions.
        services.AddDbContext<AppDbContext>(opts => opts
            .UseInMemoryDatabase("CookbookSample")
            .AddTrellisInterceptors());

        // 6. EF unit of work LAST so TransactionalCommandBehavior lands innermost.
        services.AddTrellisUnitOfWork<AppDbContext>();

        // 7. Optional: route constraints for value-object IDs (reflection-based).
        services.AddTrellisRouteConstraints(typeof(OrderId).Assembly);

        // 8. Application services.
        services.AddScoped<IOrderRepository, EfOrderRepository>();

        return services;
    }
}