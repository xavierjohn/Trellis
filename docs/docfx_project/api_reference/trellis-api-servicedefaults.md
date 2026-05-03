---
package: Trellis.ServiceDefaults
namespaces: [Trellis.ServiceDefaults]
types: [AddTrellisServiceDefaults, WebApplicationBuilderExtensions]
version: v3
last_verified: 2026-05-01
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

- `AddTrellis(...)` does not register `DbContext`, Mediator handlers, route constraints, or app-specific validators.
- `UseEntityFrameworkUnitOfWork<TContext>()` is applied last so transaction commit remains innermost in the mediator pipeline.
- If you only need one module, direct package-specific registration remains valid; the builder is for composition-root clarity.

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
| `public TrellisServiceBuilder UseAsp(Action<TrellisAspOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `Trellis.Asp` integration via `AddTrellisAsp(...)`. |
| `public TrellisServiceBuilder UseMediator(Action<TrellisMediatorTelemetryOptions>? configureTelemetry = null)` | `TrellisServiceBuilder` | Registers Trellis Mediator behaviors via `AddTrellisBehaviors(...)`. |
| `public TrellisServiceBuilder UseFluentValidation(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Registers the FluentValidation adapter. When assemblies are supplied, also scans them for validators. Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseResourceAuthorization(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Registers resource authorization behaviors/loaders discovered in assemblies. With no assemblies, enables the mediator pipeline for explicit resource-authorization registrations without scanning. Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseClaimsActorProvider(Action<ClaimsActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `ClaimsActorProvider` as `IActorProvider`. |
| `public TrellisServiceBuilder UseEntraActorProvider(Action<EntraActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `EntraActorProvider` as `IActorProvider`. |
| `public TrellisServiceBuilder UseDevelopmentActorProvider(Action<DevelopmentActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `DevelopmentActorProvider` as `IActorProvider`. Use only in development/testing hosts. |
| `public TrellisServiceBuilder UseEntityFrameworkUnitOfWork<TContext>() where TContext : DbContext` | `TrellisServiceBuilder` | Registers `EfUnitOfWork<TContext>` and `TransactionalCommandBehavior<,>` via `AddTrellisUnitOfWork<TContext>()`. Implies `UseMediator()` and is applied last. |
| `public TrellisServiceBuilder UseDomainEvents(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Registers `DomainEventDispatchBehavior<,>` and the default `IDomainEventPublisher`. With assemblies, scans for `IDomainEventHandler<TEvent>` implementations and registers each as scoped. Implies `UseMediator()`. Applied between FluentValidation and `UseEntityFrameworkUnitOfWork<TContext>()` so events fire after the transaction commits. |

## Behavior

`AddTrellis(...)` records selected modules first and then applies them in this order:

1. ASP integration.
2. Actor provider.
3. Mediator behaviors.
4. Resource authorization.
5. FluentValidation adapter/scanning when selected.
6. Domain event dispatch (registers `DomainEventDispatchBehavior<,>` and any scanned handlers).
7. EF Core Unit of Work.

That order preserves the important pipeline invariant: `TransactionalCommandBehavior<,>` is the innermost behavior, closest to the handler, so commit failures remain visible to outer logging/tracing/exception behaviors.

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
