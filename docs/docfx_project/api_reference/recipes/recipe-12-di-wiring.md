---
name: recipe-12-di-wiring
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows the AddTrellis builder composition root: module order for ASP, Mediator and validation. Use when wiring Program.cs or deciding what stays outside the builder."
---
# Recipe 12 — DI wiring playbook: `AddTrellis` composition builder

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.EntityFrameworkCore`, `Trellis.ServiceDefaults`.

**Problem.** Compose Trellis service modules in the correct order so behaviors stack properly without forcing simple apps to install every package.

**Preferred: tiered builder.** Use `Trellis.ServiceDefaults` from the API/composition root. The builder records intent first, then applies modules in the canonical order. Pipeline position comes from the Mediator behavior slots, not from call order: `TransactionalCommandBehavior<,>` sits inside telemetry and event dispatch and outside any inside-transaction application behaviors (`UseInsideTransactionBehavior`). `UseEntityFrameworkUnitOfWork<TContext>()`, when selected, is registered after those behaviors; the outbox, outbox-readiness and inbox registrations follow it and add no Mediator behavior.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.EntityFrameworkCore;
using Trellis.ServiceDefaults;

public static class CompositionRoot
{
    public static IServiceCollection AddApp(this IServiceCollection services, string connectionString)
    {
        // App-owned: provider, connection string, migrations, pooling, and Mediator registration.
        services.AddDbContext<AppDbContext>(opts => opts
            .UseSqlServer(connectionString)
            .AddTrellisInterceptors());

        services.AddMediator(options =>
        {
            options.Assemblies = [typeof(PlaceOrderCommand).Assembly];
            options.ServiceLifetime = ServiceLifetime.Scoped;
        });

        services.AddTrellis(options => options
            .UseAsp()
            .UseScalarValueValidation()
            .UseMediator()
            .UseFluentValidation(typeof(PlaceOrderValidator).Assembly)
            .UseClaimsActorProvider()
            .UseResourceAuthorization(typeof(UpdateOrderCommand).Assembly)
            .UseEntityFrameworkUnitOfWork<AppDbContext>());

        services.AddScoped<IOrderRepository, EfOrderRepository>();

        return services;
    }
}
```

**Builder modules, summarized.**

| Module | What it applies | Notes |
| ---- | ---- | ----------------- |
| `UseAsp()` | `AddTrellisAsp()` | Error → status mapping and `ResourceCollectionNameRegistry`. **Does NOT register scalar-value JSON / model-binding validation** — compose with `UseScalarValueValidation()` when binding value-object DTOs. |
| `UseScalarValueValidation()` | `AddScalarValueValidation()` | Configures both MVC and Minimal API JSON pipelines (model binders + JSON converters + `SuppressModelStateInvalidFilter` toggle). Independent of `UseAsp()`. Minimal API hosts must still call `app.UseScalarValueValidation()` middleware and chain `.WithScalarValueValidation()` per endpoint. |
| `UseMediator()` | `AddTrellisBehaviors()` | Registers the canonical Result-aware pipeline behaviors. |
| `UseInsideTransactionBehavior<TMessage,TResponse,TBehavior>()` | `AddInsideTransactionBehavior<TMessage,TResponse,TBehavior>()` | Implies `UseMediator()`; places an application behavior before the commit decision. Both support constrained open `Type` overloads. Register `IUnitOfWork` separately. See Recipe 16. |
| `UseFluentValidation(...)` | `AddTrellisFluentValidation(...)` (from `Trellis.Mediator.FluentValidation`) | Implies `UseMediator()`. Pass assemblies to scan, or omit assemblies when validators are registered explicitly. |
| `UseClaimsActorProvider()` / `UseEntraActorProvider()` / `UseDevelopmentActorProvider()` | One ASP actor provider | The builder rejects multiple actor providers. |
| `UseResourceAuthorization(...)` | `AddResourceAuthorization(...)` | Implies `UseMediator()` and scans for resource auth/loaders. |
| `UseDomainEvents(...)` | `AddDomainEventDispatch(...)` | Implies `UseMediator()`. Response-shape dispatch uses strict snapshot validation; handlers must be side-effect-only. Mutually exclusive with tracked dispatch. |
| `UseTrackedAggregateDomainEvents(...)` | `AddTrackedAggregateDomainEventDispatch(...)` | Implies `UseMediator()`. Dispatches committed aggregate snapshots for outcome-DTO commands and throws on same-aggregate or cross-aggregate cascade. Mutually exclusive with response-shape dispatch. |
| `UseEntityFrameworkUnitOfWork<TContext>()` | `AddTrellisUnitOfWork<TContext>()` | Implies `UseMediator()`. Registered after the inside-transaction behaviors and before the outbox, readiness and inbox registrations; the transaction stays outside inside-transaction behaviors. |
| `UseOutboxReadiness<TContext>(profile)` | `AddTrellisOutboxReadiness<TContext>(profile)` | Explicitly selected bounded metadata with optional local-relay monitoring, applied after selected outbox registration without changing Mediator order. Local mode requires a matching relay; metadata-only requires the worker retry limit. Endpoint authorization/selection remains separate. |

**Still app-owned.** `AddTrellis(...)` does **not** call `AddDbContext`, `AddMediator`, or route-constraint registration. Those choices depend on provider, connection string, source-generator setup, migrations, route template names, and hosting style.

Minimal API hosts add validation middleware and endpoint filters so collected scalar and
composite JSON failures short-circuit before the handler. No `ThrowOnBadRequest` override is
required. See [the ASP JSON preflight](../trellis-api-asp.md#minimal-api-semantic-json-validation).

For versioned pagination, install `Trellis.Asp.ApiVersioning`, register
`Asp.Versioning` normally, and replace `.UseAsp()` with
`.UseAsp(asp => asp.UseVersionedPageUrls())` (import `Trellis.Asp.ApiVersioning`).
Unversioned hosts keep `.UseAsp()`; both use the same `HttpContext.PageUrl` expression.
The optional policy uses the existing options slot, so ServiceDefaults does not acquire
a versioning-SDK dependency. Location still uses `.WithVersionedRoute()` per response.

> **Set `options.ServiceLifetime = ServiceLifetime.Scoped` on `AddMediator(...)`** in any host that creates a request/execution scope (ASP.NET Core, workers). Mediator's default lifetime is `Singleton`, but the Trellis pipeline behaviors depend on per-request services (`IActorProvider`, `IUnitOfWork`, `IMessageValidator<>`), so a singleton handler/behavior fails the DI root-scope validation the moment it resolves a scoped dependency — a build-clean service that throws at startup. (Same guidance: `Trellis.Mediator` README and the [Mediator integration article](https://xavierjohn.github.io/Trellis/articles/integration-mediator.html).)
