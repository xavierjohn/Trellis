---
title: Service Defaults (Composition Root)
package: Trellis.ServiceDefaults
topics: [composition-root, service-collection, di-wiring, builder, pipeline-ordering]
related_api_reference: [trellis-api-servicedefaults.md, trellis-api-mediator.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-fluentvalidation.md, trellis-api-cookbook.md]
last_verified: 2026-05-05
audience: [developer]
---
# Service Defaults (Composition Root)

`Trellis.ServiceDefaults` provides one fluent builder — `services.AddTrellis(o => ...)` — that wires the Trellis integration modules in the canonical order so the mediator pipeline ends up with the right behaviors at the right positions.

It is **not** a mandatory dependency. If you only need one integration package, calling that package's direct registration helper (`AddTrellisAsp`, `AddTrellisBehaviors`, `AddTrellisFluentValidation`, etc.) remains a valid choice. The builder exists to make composition-root code easier to read when several integrations are combined.

## Quick start

```csharp
builder.Services.AddTrellis(options => options
    .UseAsp()
    .UseMediator()
    .UseFluentValidation(typeof(Program).Assembly)
    .UseClaimsActorProvider()
    .UseResourceAuthorization(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
```

`AddTrellis` does NOT register your `DbContext`, your mediator handlers, or your route constraints — those are always application-owned. Validators, resource loaders, and domain event handlers are registered only when you opt into assembly scanning via the params-`Assembly[]` overloads (`UseFluentValidation(asm)`, `UseResourceAuthorization(asm)`, `UseDomainEvents(asm)`); the parameterless overloads register only the adapter / pipeline behavior and leave per-type registrations to you.

## Canonical order

`AddTrellis(o => ...)` records the requested modules during the configure callback, then applies them in this order:

1. **ASP integration** (`UseAsp`) — `AddTrellisAsp(...)`.
2. **Actor provider** (`UseClaimsActorProvider` / `UseEntraActorProvider` / `UseDevelopmentActorProvider`) and the **caching wrap** (`UseCachingActorProvider<T>`).
3. **Mediator behaviors** (`UseMediator`) — `AddTrellisBehaviors(...)`. Always present when any other Use that implies it is selected (FluentValidation, ResourceAuthorization, DomainEvents, EntityFrameworkUnitOfWork).
4. **Resource authorization** scanning (`UseResourceAuthorization(asm)`) when assemblies are supplied.
5. **FluentValidation** adapter and (optionally) scanning (`UseFluentValidation`).
6. **Domain event dispatch** (`UseDomainEvents`) — `DomainEventDispatchBehavior<,>` + default `IDomainEventPublisher` + scanned handlers.
7. **EF Core Unit of Work** (`UseEntityFrameworkUnitOfWork<TContext>`) — applied last so `TransactionalCommandBehavior<,>` is the innermost behavior.

This sequence preserves the central pipeline invariant: the transactional behavior is closest to the handler, so commit failures remain visible to outer logging/tracing/exception behaviors. Domain events fire after the transaction commits because dispatch is registered before the unit of work.

## Mutually-exclusive slots

| Slot | Single-selection rule |
|---|---|
| Actor provider | At most one of `UseClaimsActorProvider`, `UseEntraActorProvider`, `UseDevelopmentActorProvider`. Throws `InvalidOperationException` on duplicate (same kind or different). |
| Caching actor wrap | At most one `UseCachingActorProvider<T>()`. Throws `InvalidOperationException` on duplicate. |
| Unit of work | At most one `UseEntityFrameworkUnitOfWork<TContext>()` per composition. Throws `InvalidOperationException` on duplicate (same `TContext` or different). Read/write context splits should run as separate composition roots or use a multi-tenant `DbContext`. |

## Per-request actor caching

`UseCachingActorProvider<T>()` wraps the inner `IActorProvider` with a per-request caching decorator. Chain it AFTER the matching `UseXxxActorProvider(...)` so the inner provider's `IOptions<TOptions>` is configured before the wrap replaces the `IActorProvider` slot.

```csharp
builder.Services.AddTrellis(options => options
    .UseClaimsActorProvider(o => o.ActorIdClaim = "sub")
    .UseCachingActorProvider<ClaimsActorProvider>());
```

## Order-independence for explicit resource-authorization registrations

The no-assembly form `UseResourceAuthorization()` is for AOT consumers who register each `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` explicitly via `services.AddResourceAuthorization<TMessage, TResource, TResponse>()`. Those explicit calls can be made **before** `AddTrellis(...)`; `AddTrellisBehaviors()` (called by `UseMediator()`) detects pre-existing closed-generic resource-auth behaviors and re-positions them to sit immediately before `ValidationBehavior<,>` in the canonical pipeline envelope. This mirrors the same symmetry between `AddTrellisUnitOfWork<TContext>` and `AddDomainEventDispatch`: pipeline-position-aware registrations are order-independent regardless of which one runs first.

```csharp
// Either order works; resource-auth ends up in the canonical position.
builder.Services.AddResourceAuthorization<UpdateOrderCommand, Order, Result<string>>();
builder.Services.AddScoped<IResourceLoader<UpdateOrderCommand, Order>, OrderLoader>();
builder.Services.AddTrellis(options => options.UseResourceAuthorization());
```

## AOT compatibility

`Trellis.ServiceDefaults` itself is **not** AOT- or trim-compatible. The fluent assembly-scanning methods wrap underlying `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` APIs without propagating the attributes, and the package opts out of AOT/trim analyzers (`<IsAotCompatible>false</IsAotCompatible>`).

For AOT/trim consumers, use the per-package direct APIs that DO propagate the attributes:

| Builder method | AOT-friendly direct call |
|---|---|
| `o.UseFluentValidation(asm)` | `services.AddTrellisFluentValidation()` plus per-validator `services.AddScoped<IValidator<T>, TValidator>()` |
| `o.UseResourceAuthorization(asm)` | `services.AddResourceAuthorization<TMessage, TResource, TResponse>()` plus `services.AddScoped<IResourceLoader<TMessage, TResource>, TLoader>()` |
| `o.UseDomainEvents(asm)` | `services.AddDomainEventDispatch()` plus `services.AddDomainEventHandler<TEvent, THandler>()` |

The parameterless `o.UseFluentValidation()` / `o.UseResourceAuthorization()` / `o.UseDomainEvents()` overloads are AOT-compatible — they only register the adapter / pipeline behaviors and rely on the consumer's explicit per-type registrations, which is the same pattern AOT consumers already use directly.

## Layered configuration via repeated calls

`UseAsp(configure)` and `UseMediator(configureTelemetry)` allow repeated calls; the configure delegates compose in call order rather than overwriting. This supports library-then-host configuration patterns:

```csharp
// Library defaults applied first…
builder.Services.AddTrellis(o => o
    .UseAsp(opts => opts.MapError<MyDomainError>(StatusCodes.Status409Conflict))
    .UseAsp(opts => opts.HonorPrefer())); // host adds prefer handling on top
```

## See also

- [API reference](../api_reference/trellis-api-servicedefaults.md) — type tables and method signatures.
- [Cookbook Recipe 12: DI wiring playbook](../api_reference/trellis-api-cookbook.md#recipe-12--di-wiring-playbook-addtrellis-composition-builder).
- Per-package articles: [`integration-aspnet.md`](integration-aspnet.md), [`integration-mediator.md`](integration-mediator.md), [`integration-fluentvalidation.md`](integration-fluentvalidation.md), [`integration-ef.md`](integration-ef.md), [`integration-asp-authorization.md`](integration-asp-authorization.md).
