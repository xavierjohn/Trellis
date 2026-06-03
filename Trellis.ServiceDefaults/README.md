# Trellis.ServiceDefaults

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.ServiceDefaults.svg)](https://www.nuget.org/packages/Trellis.ServiceDefaults)

Opinionated composition defaults for Trellis web services.

## Installation
```bash
dotnet add package Trellis.ServiceDefaults
```

## Quick Example
```csharp
using Trellis.ServiceDefaults;

builder.Services.AddTrellis(options => options
    .UseAsp()
    .UseScalarValueValidation()
    .UseProblemDetails()
    .UseMediator()
    .UseFluentValidation(typeof(Program).Assembly)
    .UseClaimsActorProvider()
    .UseResourceAuthorization(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```

`UseEntityFrameworkUnitOfWork<TContext>()` is always applied last so the transactional command behavior runs innermost. `AddDbContext<TContext>(...)` and `AddMediator(...)` remain application-owned registrations.

`UseFluentValidation()` and `UseResourceAuthorization()` both support no-assembly calls for explicit, no-scanning composition; pass assemblies only when you want Trellis to discover validators/resource loaders automatically.

`UseScalarValueValidation()` is independent of `UseAsp()` — it registers the scalar-value model binders, JSON converters, and `SuppressModelStateInvalidFilter` toggle that mutate global `MvcOptions` / `JsonOptions` for both MVC and Minimal API JSON pipelines. Hosts that only need error-to-status mapping (e.g. an MVC site that does not bind value-object DTOs) can call `UseAsp()` alone and skip the binder / converter wiring. Minimal API hosts must still call `app.UseScalarValueValidation()` middleware and chain `.WithScalarValueValidation()` per endpoint.

`UseProblemDetails()` is independent of `UseAsp()` — it registers Trellis ProblemDetails customization (`traceId` on every error, 405 `Allow` header projected as `extensions.allow`, 500 detail rewrite) without pulling in Trellis MVC/result-mapping infrastructure. Composing it with a direct `services.AddTrellisProblemDetails()` call is idempotent — exactly one Trellis post-configure layer ends up registered.

`UseIdempotency(opt => ...)` wires the opt-in IETF `Idempotency-Key` middleware (options + scope resolver + marker). Composition is explicit — the slot does not register a store, so callers add `services.AddInMemoryIdempotencyStore()` (dev / tests) or an EF-backed store (production) and mount the middleware with `app.UseTrellisIdempotency()`. Endpoints opt in with `[Idempotent]`. The slot is also independent of `UseAsp()`.

## Key Features
- One composition root for the typical Trellis web service: `AddTrellis(...)` chains every framework slot (`UseAsp`, `UseProblemDetails`, `UseMediator`, `UseFluentValidation`, an actor provider, `UseResourceAuthorization`, `UseEntityFrameworkUnitOfWork`) so consumers don't have to remember per-package wiring order.
- Mediator pipeline order is owned by `Trellis.Mediator` (outermost → innermost: `ExceptionBehavior`, `TracingBehavior`, `LoggingBehavior`, `AuthorizationBehavior`, `ResourceAuthorizationBehavior` (opt-in), `ValidationBehavior`, `TransactionalCommandBehavior` (opt-in)). `Trellis.ServiceDefaults` preserves that order across its helpers: `UseEntityFrameworkUnitOfWork<TContext>()` is always applied last so the transactional commit runs innermost; domain events also register before UoW when enabled.
- Actor-provider selectors (`UseClaimsActorProvider`, `UseEntraActorProvider`, `UseDevelopmentActorProvider`, `UseCachingActorProvider<T>`) replace the `IActorProvider` slot atomically — calling more than one leaves exactly one provider registered (last call wins) per the `Trellis.Asp.Authorization` contract.
- `UseWorkerActor(systemActor)` composes the selected actor provider with a worker/system fallback for background scopes that have no `HttpContext`. It is applied after the actor-provider selection and the optional caching wrap, so HTTP requests still resolve through the inner provider (and its cache) and `BackgroundService` ticks resolve to the supplied system actor without traversing caching.

## AOT compatibility

`Trellis.ServiceDefaults` is **AOT- and trim-compatible**. The package enables the AOT and trim analyzers (`IsAotCompatible`, `IsTrimmable`, `EnableAotAnalyzer`, `EnableTrimAnalyzer`) and keeps the default composition slots safe when you choose explicit overloads.

AOT-safe builder shapes are `UseFluentValidation()` plus `UseFluentValidation<TValidator, TMessage>()` per validator, `UseResourceAuthorization()` plus `UseResourceAuthorization<TMessage, TResource, TResponse>()` per command, and `UseDomainEvents()` or `UseTrackedAggregateDomainEvents()` plus their `<TEvent, THandler>()` per-handler overloads.

The assembly-scanning overloads (`UseFluentValidation(asm)`, `UseResourceAuthorization(asm)`, `UseDomainEvents(asm)`, `UseTrackedAggregateDomainEvents(asm)`) remain convenience APIs for non-AOT consumers. They are annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so trimmed/AOT applications must either switch to explicit registrations or make that choice visible at the consumer call site, for example by annotating the composition method or suppressing the analyzer warning.

`UseEntityFrameworkUnitOfWork<TContext>()` is also annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` because the EF Core integration depends on runtime reflection and is excluded from the Trellis AOT publish gate. AOT consumers can still use the ASP, Mediator, FluentValidation, and authorization slots through this builder, but should compose data access separately.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-servicedefaults.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
