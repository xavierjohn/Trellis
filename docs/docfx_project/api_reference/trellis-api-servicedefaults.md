---
package: Trellis.ServiceDefaults
namespaces: [Trellis.ServiceDefaults]
types: [TrellisServiceCollectionExtensions, TrellisServiceBuilder]
version: v3
last_verified: 2026-05-05
audience: [llm]
---
# Trellis.ServiceDefaults API Reference

**Package:** `Trellis.ServiceDefaults`  
**Namespace:** `Trellis.ServiceDefaults`  
**Purpose:** Opinionated composition builder for API/composition-root projects that want Trellis integration modules applied in the canonical order.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-12--di-wiring-playbook-addtrellis-composition-builder) — composition-root recipe.

## Use this file when

- You are wiring a composition root and want Trellis modules applied in the canonical order.
- You want one fluent builder for ASP, Mediator, FluentValidation, resource authorization, actor provider, and EF unit-of-work registration.
- You need to know what `AddTrellis(...)` deliberately does not register.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Enable ASP Result-to-HTTP mapping | `services.AddTrellis(o => o.UseAsp())` | [`TrellisServiceBuilder`](#trellisservicebuilder), [ASP](trellis-api-asp.md) |
| Add standard mediator behaviors | `.UseMediator()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator](trellis-api-mediator.md) |
| Add FluentValidation adapter/scanning | `.UseFluentValidation(typeof(Program).Assembly)` or `.UseFluentValidation()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [FluentValidation](trellis-api-fluentvalidation.md) |
| Add resource authorization | `.UseResourceAuthorization(...)` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator resource authorization](trellis-api-mediator.md) |
| Register an actor provider | `.UseClaimsActorProvider()`, `.UseEntraActorProvider()`, or `.UseDevelopmentActorProvider()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [ASP actor providers](trellis-api-asp.md#namespace-trellisaspauthorization) |
| Add EF unit-of-work behavior | `.UseEntityFrameworkUnitOfWork<TContext>()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator UoW](trellis-api-mediator.md) |
| Dispatch domain events from successful commands | `.UseDomainEvents(typeof(MyHandler).Assembly)` or `.UseDomainEvents()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator domain events](trellis-api-mediator.md) |

## Common traps

- `AddTrellis(...)` does not register `DbContext`, Mediator handlers, or route constraints — those are always application-owned. Validators (`IValidator<T>`), resource loaders (`IResourceLoader<TMessage, TResource>`), and domain event handlers (`IDomainEventHandler<TEvent>`) are registered ONLY when you opt into assembly scanning via the params-`Assembly[]` overloads (`UseFluentValidation(asm)`, `UseResourceAuthorization(asm)`, `UseDomainEvents(asm)`); the parameterless overloads register only the adapter / pipeline behavior and leave per-type registrations to you.
- `UseEntityFrameworkUnitOfWork<TContext>()` is applied last so transaction commit remains innermost in the mediator pipeline.
- Calling `UseEntityFrameworkUnitOfWork<TContext>()` more than once (with the same or a different `TContext`) throws `InvalidOperationException`. The Trellis pipeline supports exactly one transactional `IUnitOfWork` per composition; chaining two calls (e.g. for a read/write context split) is always misconfiguration. Use a separate composition root or a single multi-tenant `DbContext` instead.
- If you only need one module, direct package-specific registration remains valid; the builder is for composition-root clarity.

## AOT compatibility

`Trellis.ServiceDefaults` is **not** AOT- or trim-compatible. The package opts out of AOT/trim analyzers (`<IsAotCompatible>false</IsAotCompatible>`, `<IsTrimmable>false</IsTrimmable>`) because the assembly-scanning fluent methods (`UseFluentValidation(params Assembly[])`, `UseResourceAuthorization(params Assembly[])`, `UseDomainEvents(params Assembly[])`) wrap underlying `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` APIs whose attributes are not propagated through the wrapper.

For AOT/trim consumers, use the per-package direct APIs that do propagate the attributes:

| Builder method | AOT-friendly direct call |
| --- | --- |
| `o.UseFluentValidation(asm)` | `services.AddTrellisFluentValidation()` plus per-validator `services.AddScoped<IValidator<T>, TValidator>()` |
| `o.UseResourceAuthorization(asm)` | `services.AddResourceAuthorization<TMessage, TResource, TResponse>()` plus `services.AddScoped<IResourceLoader<TMessage, TResource>, TLoader>()` |
| `o.UseDomainEvents(asm)` | `services.AddDomainEventDispatch()` plus `services.AddDomainEventHandler<TEvent, THandler>()` |

The parameterless `o.UseFluentValidation()` / `o.UseResourceAuthorization()` / `o.UseDomainEvents()` overloads are AOT-compatible — they only register the adapter / pipeline behaviors and rely on the consumer's explicit per-type registrations.

## Types

### `TrellisServiceCollectionExtensions`

```csharp
public static class TrellisServiceCollectionExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellis(this IServiceCollection services, Action<TrellisServiceBuilder> configure)` | `IServiceCollection` | Creates a `TrellisServiceBuilder`, lets the caller select modules, then applies the selected modules in canonical order. Does not register `DbContext` or Mediator handlers. |

### `TrellisServiceBuilder`

```csharp
public sealed class TrellisServiceBuilder
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisServiceBuilder UseAsp(Action<TrellisAspOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `Trellis.Asp` integration via `AddTrellisAsp(...)`. Repeated calls compose the configure delegates rather than overwriting. |
| `public TrellisServiceBuilder UseMediator(Action<TrellisMediatorTelemetryOptions>? configureTelemetry = null)` | `TrellisServiceBuilder` | Registers Trellis Mediator behaviors via `AddTrellisBehaviors(...)`. Repeated calls compose the configure delegates rather than overwriting. |
| `public TrellisServiceBuilder UseFluentValidation(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Registers the FluentValidation adapter. When assemblies are supplied, also scans them for validators (non-AOT). Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseResourceAuthorization(params Assembly[] assemblies)` | `TrellisServiceBuilder` | With assemblies: scans for `IAuthorizeResource<TResource>` commands and registers `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` for each (non-AOT). With no assemblies: relies on the static-permission `AuthorizationBehavior<,>` registered unconditionally by `UseMediator()`/`AddTrellisBehaviors()`, and on the consumer's explicit per-message `services.AddResourceAuthorization<TMessage, TResource, TResponse>()` calls. Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseClaimsActorProvider(Action<ClaimsActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `ClaimsActorProvider` as `IActorProvider`. Mutually exclusive with the other actor-provider selectors. |
| `public TrellisServiceBuilder UseEntraActorProvider(Action<EntraActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `EntraActorProvider` as `IActorProvider`. Mutually exclusive with the other actor-provider selectors. |
| `public TrellisServiceBuilder UseDevelopmentActorProvider(Action<DevelopmentActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `DevelopmentActorProvider` as `IActorProvider`. Mutually exclusive with the other actor-provider selectors. Use only in development/testing hosts. |
| `public TrellisServiceBuilder UseCachingActorProvider<T>() where T : class, IActorProvider` | `TrellisServiceBuilder` | Wraps the inner `IActorProvider` registration with a per-request caching decorator via `AddCachingActorProvider<T>()`. Chain after the matching `UseXxxActorProvider(...)` call so the inner provider's `IOptions<TOptions>` is configured first. |
| `public TrellisServiceBuilder UseEntityFrameworkUnitOfWork<TContext>() where TContext : DbContext` | `TrellisServiceBuilder` | Registers `EfUnitOfWork<TContext>` and `TransactionalCommandBehavior<,>` via `AddTrellisUnitOfWork<TContext>()`. Implies `UseMediator()` and is applied last. Throws `InvalidOperationException` on repeated call — see "Common traps". |
| `public TrellisServiceBuilder UseDomainEvents(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Registers `DomainEventDispatchBehavior<,>` and the default `IDomainEventPublisher`. With assemblies, scans for `IDomainEventHandler<TEvent>` implementations and registers each as scoped (non-AOT). Implies `UseMediator()`. Applied between FluentValidation and `UseEntityFrameworkUnitOfWork<TContext>()` so events fire after the transaction commits. |

## Behavior

`AddTrellis(...)` records selected modules first and then applies them in this order:

1. ASP integration.
2. Actor provider (and the optional caching wrap that chains after it).
3. Mediator behaviors.
4. Resource authorization (assembly scanning, when assemblies are supplied).
5. FluentValidation adapter/scanning when selected.
6. Domain event dispatch (registers `DomainEventDispatchBehavior<,>`, the default `IDomainEventPublisher`, and any scanned handlers).
7. EF Core Unit of Work.

That order preserves the important pipeline invariant: `TransactionalCommandBehavior<,>` is the innermost behavior, closest to the handler, so commit failures remain visible to outer logging/tracing/exception behaviors.

### Order-independence for explicit resource-authorization registrations

Explicit `services.AddResourceAuthorization<TMessage, TResource, TResponse>()` calls made BEFORE `AddTrellis(...)` are now order-independent. `AddTrellisBehaviors()` (called by `UseMediator()`) detects any pre-existing closed-generic `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` descriptors and re-positions them to sit immediately before `ValidationBehavior<,>`, so they end up in the canonical pipeline envelope regardless of registration order. This mirrors the symmetry between `AddTrellisUnitOfWork<TContext>` and `AddDomainEventDispatch`.

`AddTrellis(...)` deliberately does **not** register:

- `AddDbContext<TContext>(...)` — provider, connection string, pooling, migrations, and interceptors are application-owned.
- `AddMediator(...)` — handler discovery/source-generator configuration is application-owned.
- route constraints — route parameter names are application-owned.

## Examples

```csharp
services.AddTrellis(options => options.UseAsp());
```

```csharp
services.AddTrellis(options => options
    .UseAsp()
    .UseMediator()
    .UseFluentValidation(typeof(Program).Assembly));
```

```csharp
// Adapter only; validators are registered explicitly elsewhere.
services.AddTrellis(options => options
    .UseMediator()
    .UseFluentValidation());
```

```csharp
// No assembly scanning; resource authorization registrations are explicit elsewhere.
services.AddTrellis(options => options
    .UseMediator()
    .UseResourceAuthorization());
```

```csharp
services.AddTrellis(options => options
    .UseAsp()
    .UseMediator()
    .UseClaimsActorProvider()
    .UseResourceAuthorization(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```

```csharp
// Domain event dispatch with assembly scanning. Handlers fire after the transaction
// commits because UseDomainEvents is applied before UseEntityFrameworkUnitOfWork.
services.AddTrellis(options => options
    .UseMediator()
    .UseDomainEvents(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```
