---
package: Trellis.ServiceDefaults
namespaces: [Trellis.ServiceDefaults]
types: [TrellisServiceCollectionExtensions, TrellisServiceBuilder]
version: v3
last_verified: 2026-06-03
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
| Enable ASP Result-to-HTTP mapping | `services.AddTrellis(o => o.UseAsp())` | [`TrellisServiceBuilder`](#trellisservicebuilder), [ASP](trellis-api-asp.md#domain--http-boundary-mapping) |
| Enable scalar-value JSON / model-binding validation | `services.AddTrellis(o => o.UseScalarValueValidation())` (compose with `.UseAsp()` for the controller-host default) | [`TrellisServiceBuilder`](#trellisservicebuilder), [ASP](trellis-api-asp.md#namespace-trellisaspvalidation) |
| Enable Trellis ProblemDetails customization | `.UseProblemDetails()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [ASP `AddTrellisProblemDetails`](trellis-api-asp.md#servicecollectionextensions) |
| Enable the IETF `Idempotency-Key` middleware for opted-in `POST` / `PATCH` endpoints | `.UseIdempotency(opt => ...)` plus `services.AddInMemoryIdempotencyStore()` (or an EF-backed store) and `app.UseTrellisIdempotency()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [ASP `Trellis.Asp.Idempotency`](trellis-api-asp.md#namespace-trellisaspidempotency), Cookbook [Recipe 29](trellis-api-cookbook.md#recipe-29--ietf-idempotency-key-middleware-on-post--patch-with-usetrellisidempotency) |
| Add standard mediator behaviors | `.UseMediator()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator](trellis-api-mediator.md#trellismediatorservicecollectionextensions) |
| Add FluentValidation adapter/scanning | `.UseFluentValidation(typeof(Program).Assembly)` or `.UseFluentValidation()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [FluentValidation](trellis-api-fluentvalidation.md#fluentvalidationservicecollectionextensions) |
| Add resource authorization | `.UseResourceAuthorization(...)` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator resource authorization](trellis-api-mediator.md#resourceauthorizationbehaviortmessage-tresource-tresponse) |
| Register an actor provider | `.UseClaimsActorProvider()`, `.UseNestedJsonPathClaimsActorProvider()`, `.UseEntraActorProvider()`, or `.UseDevelopmentActorProvider()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [ASP actor providers](trellis-api-asp.md#namespace-trellisaspauthorization) |
| Add EF unit-of-work behavior | `.UseEntityFrameworkUnitOfWork<TContext>()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator UoW](trellis-api-mediator.md#cross-package-preflight-for-pipeline-changes) |
| Dispatch domain events from successful commands | `.UseDomainEvents(typeof(MyHandler).Assembly)` or `.UseDomainEvents()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator domain events](trellis-api-mediator.md#domaineventdispatchbehavior) |
| Auto-dispatch domain events from every tracked aggregate (outcome-DTO commands) | `.UseTrackedAggregateDomainEvents(typeof(MyHandler).Assembly)` or `.UseTrackedAggregateDomainEvents()` | [`TrellisServiceBuilder`](#trellisservicebuilder), [Mediator tracked dispatch](trellis-api-mediator.md#trackedaggregatedomaineventdispatchbehavior) |

## Common traps

- `AddTrellis(...)` does not register `DbContext`, Mediator handlers, or route constraints — those are always application-owned. Validators (`IValidator<T>`), resource loaders (`IResourceLoader<TMessage, TResource>`), and domain event handlers (`IDomainEventHandler<TEvent>`) are registered ONLY when you opt into assembly scanning via the params-`Assembly[]` overloads (`UseFluentValidation(asm)`, `UseResourceAuthorization(asm)`, `UseDomainEvents(asm)`); the parameterless overloads register only the adapter / pipeline behavior and leave per-type registrations to you.
- `UseEntityFrameworkUnitOfWork<TContext>()` is applied last so transaction commit remains innermost in the mediator pipeline.
- Calling `UseEntityFrameworkUnitOfWork<TContext>()` more than once (with the same or a different `TContext`) throws `InvalidOperationException`. The Trellis pipeline supports exactly one transactional `IUnitOfWork` per composition; chaining two calls (e.g. for a read/write context split) is always misconfiguration. Use a separate composition root or a single multi-tenant `DbContext` instead.
- If you only need one module, direct package-specific registration remains valid; the builder is for composition-root clarity.

## AOT compatibility

`Trellis.ServiceDefaults` is now **AOT- and trim-compatible** (`<IsAotCompatible>true</IsAotCompatible>`, `<IsTrimmable>true</IsTrimmable>`, `<EnableAotAnalyzer>true</EnableAotAnalyzer>`, `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`). The compatibility surface is split across two overload shapes per assembly-scanning slot:

| Slot | AOT-safe overload | Scanning overload (`[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`) |
| --- | --- | --- |
| FluentValidation | `o.UseFluentValidation()` (adapter only) plus `o.UseFluentValidation<TValidator, TMessage>()` per validator | `o.UseFluentValidation(asm)` |
| Resource authorization | `o.UseResourceAuthorization()` (pipeline only) plus `o.UseResourceAuthorization<TMessage, TResource, TResponse>()` per command | `o.UseResourceAuthorization(asm)` |
| Domain events (response-shape) | `o.UseDomainEvents()` (publisher + behavior only) plus `o.UseDomainEvents<TEvent, THandler>()` per handler | `o.UseDomainEvents(asm)` |
| Domain events (tracked-aggregate) | `o.UseTrackedAggregateDomainEvents()` (publisher + behavior only) plus `o.UseTrackedAggregateDomainEvents<TEvent, THandler>()` per handler | `o.UseTrackedAggregateDomainEvents(asm)` |

The AOT-safe overloads use only open-generic DI registrations and explicit closed-type method calls — no reflection over assemblies. The scanning overloads remain available for fast iteration in non-AOT consumers and surface the IL2026 / IL3050 warnings at the consumer's call site so the choice between AOT and convenience is explicit, never silent.

Direct per-package registrations (`services.AddTrellisFluentValidation()`, `services.AddResourceAuthorization<TMessage, TResource, TResponse>()`, `services.AddDomainEventHandler<TEvent, THandler>()`) remain valid as an escape hatch — call them outside the builder when you need to register a type the builder does not yet model.

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
| `public TrellisServiceBuilder UseAsp(Action<TrellisAspOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `Trellis.Asp` integration via `AddTrellisAsp(...)` (error-to-status mapping + `ResourceCollectionNameRegistry`). **Does NOT register scalar-value validation** — compose with `UseScalarValueValidation()` when the host binds value-object DTOs from JSON / route / query. Repeated calls compose the configure delegates rather than overwriting. |
| `public TrellisServiceBuilder UseScalarValueValidation()` | `TrellisServiceBuilder` | Registers scalar-value validation via `AddScalarValueValidation()`: configures both MVC and Minimal API JSON pipelines (model binders, JSON converters, `SuppressModelStateInvalidFilter` toggle) so `IScalarValue<TSelf, TPrimitive>` / `Maybe<T>` validation surfaces as RFC 9457 ProblemDetails. Mutates global `MvcOptions` / `JsonOptions`. Independent of `UseAsp()`. **Does NOT register the Minimal API endpoint filter or middleware** — Minimal API hosts must additionally call `app.UseScalarValueValidation()` (middleware) and chain `.WithScalarValueValidation()` per endpoint. Idempotent. |
| `public TrellisServiceBuilder UseProblemDetails()` | `TrellisServiceBuilder` | Registers Trellis ProblemDetails customization (`traceId` on every error, `405` `Allow` header projected as `extensions.allow`, `500` detail rewrite) via `AddTrellisProblemDetails()`. Independent of `UseAsp()` — does not pull in Trellis MVC/result-mapping infrastructure. Idempotent across direct + builder composition: a consumer that calls both `services.AddTrellisProblemDetails()` directly and `options.UseProblemDetails()` ends up with exactly one Trellis post-configure layer. |
| `public TrellisServiceBuilder UseIdempotency(Action<IdempotencyOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `AddTrellisIdempotency(configure)`: startup-validated `IdempotencyOptions`, the default `IIdempotencyScopeResolver` (per-actor, falling back to anonymous), and an internal marker used by `app.UseTrellisIdempotency()` for startup validation. **Does not register a store** — composition is explicit; pair with `services.AddInMemoryIdempotencyStore()` (dev / tests) or an EF-backed store (production). Mount the middleware with `app.UseTrellisIdempotency()` in the request pipeline. Independent of `UseAsp()`. Repeated calls compose the configure delegates rather than overwriting, mirroring `UseAsp` / `UseMediator`. |
| `public TrellisServiceBuilder UseMediator(Action<TrellisMediatorTelemetryOptions>? configureTelemetry = null)` | `TrellisServiceBuilder` | Registers Trellis Mediator behaviors via `AddTrellisBehaviors(...)`. Repeated calls compose the configure delegates rather than overwriting. |
| `public TrellisServiceBuilder UseFluentValidation()` | `TrellisServiceBuilder` | **AOT-safe.** Registers the FluentValidation adapter only; pair with `UseFluentValidation<TValidator, TMessage>()` or explicit per-validator DI registrations. Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseFluentValidation<TValidator, TMessage>() where TValidator : class, IValidator<TMessage>` | `TrellisServiceBuilder` | **AOT-safe.** Registers `TValidator` as the `IValidator<TMessage>` and wires the FluentValidation adapter. Implies `UseMediator()`. |
| `[RequiresUnreferencedCode] [RequiresDynamicCode] public TrellisServiceBuilder UseFluentValidation(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Registers the FluentValidation adapter and scans the supplied assemblies for `IValidator<T>` implementations (non-AOT). Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseResourceAuthorization()` | `TrellisServiceBuilder` | **AOT-safe.** Enables the resource-authorization pipeline without scanning. Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseResourceAuthorization<TMessage, TResource, TResponse>()` | `TrellisServiceBuilder` | **AOT-safe.** Registers the closed-generic `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` for the named command. Implies `UseMediator()`. |
| `[RequiresUnreferencedCode] [RequiresDynamicCode] public TrellisServiceBuilder UseResourceAuthorization(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Scans assemblies for `IAuthorizeResource<TResource>` commands and registers `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` for each (non-AOT). Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseClaimsActorProvider(Action<ClaimsActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `ClaimsActorProvider` as `IActorProvider`. Mutually exclusive with the other actor-provider selectors; a second actor-provider selector throws `InvalidOperationException`. |
| `public TrellisServiceBuilder UseNestedJsonPathClaimsActorProvider(Action<NestedJsonPathClaimsActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `NestedJsonPathClaimsActorProvider` as `IActorProvider` with startup validation for the container-claim invariant. Mutually exclusive with the other actor-provider selectors; a second actor-provider selector throws `InvalidOperationException`. When `configure` is omitted, the default empty JSON paths make the provider behave like `ClaimsActorProvider`. |
| `public TrellisServiceBuilder UseEntraActorProvider(Action<EntraActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `EntraActorProvider` as `IActorProvider`. Mutually exclusive with the other actor-provider selectors; a second actor-provider selector throws `InvalidOperationException`. |
| `public TrellisServiceBuilder UseDevelopmentActorProvider(Action<DevelopmentActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `DevelopmentActorProvider` as `IActorProvider`. Mutually exclusive with the other actor-provider selectors; a second actor-provider selector throws `InvalidOperationException`. Use only in development/testing hosts. |
| `public TrellisServiceBuilder UseCachingActorProvider<T>() where T : class, IActorProvider` | `TrellisServiceBuilder` | Wraps the inner `IActorProvider` registration with a per-request caching decorator via `AddCachingActorProvider<T>()`. Chain after the matching `UseXxxActorProvider(...)` call so the inner provider's `IOptions<TOptions>` is configured first. Throws `InvalidOperationException` on repeated call. |
| `public TrellisServiceBuilder UseWorkerActor(Actor systemActor)` | `TrellisServiceBuilder` | Composes the unkeyed `IActorProvider` registration produced by the selected actor-provider slot with a worker-actor wrapper via `AddTrellisWorkerActor(systemActor)`. The wrapper returns `systemActor` when `IHttpContextAccessor.HttpContext` is `null` (background-worker ticks, hosted services) and delegates to the inner provider otherwise. `Apply()` always runs this after the actor-provider selection and any `UseCachingActorProvider<T>()` wrap, so worker-tick lookups bypass the caching layer regardless of the chain order in which `UseWorkerActor(...)` was called. Requires that some actor provider slot is selected somewhere in the composition (`UseClaimsActorProvider`, `UseNestedJsonPathClaimsActorProvider`, `UseEntraActorProvider`, `UseDevelopmentActorProvider`, or `UseCachingActorProvider<T>()` with a custom provider). Throws `InvalidOperationException` on repeated call, when no actor provider slot is selected, or when the inner descriptor is singleton-lifetime via implementation type or factory (use `services.AddSingleton<IActorProvider>(instance)` or re-register as scoped instead) or transient-lifetime (re-register as scoped). Keyed registrations are ignored. |
| `[RequiresUnreferencedCode] [RequiresDynamicCode] public TrellisServiceBuilder UseEntityFrameworkUnitOfWork<TContext>() where TContext : DbContext` | `TrellisServiceBuilder` | Registers `EfUnitOfWork<TContext>` and `TransactionalCommandBehavior<,>` via `AddTrellisUnitOfWork<TContext>()`. Implies `UseMediator()` and is applied last. Annotated non-AOT because the underlying `Trellis.EntityFrameworkCore` package opts out of AOT/trim. Throws `InvalidOperationException` on repeated call — see "Common traps". |
| `public TrellisServiceBuilder UseDomainEvents()` | `TrellisServiceBuilder` | **AOT-safe.** Registers `DomainEventDispatchBehavior<,>` and the default `IDomainEventPublisher` without scanning. Implies `UseMediator()`. Mutually exclusive with `UseTrackedAggregateDomainEvents(...)`. |
| `public TrellisServiceBuilder UseDomainEvents<TEvent, THandler>() where TEvent : IDomainEvent where THandler : class, IDomainEventHandler<TEvent>` | `TrellisServiceBuilder` | **AOT-safe.** Registers `THandler` for `TEvent` and wires the dispatch behavior. Implies `UseMediator()`. Mutually exclusive with `UseTrackedAggregateDomainEvents(...)`. |
| `[RequiresUnreferencedCode] [RequiresDynamicCode] public TrellisServiceBuilder UseDomainEvents(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Scans assemblies for `IDomainEventHandler<TEvent>` implementations (non-AOT). Implies `UseMediator()`. Mutually exclusive with `UseTrackedAggregateDomainEvents(...)`. |
| `public TrellisServiceBuilder UseTrackedAggregateDomainEvents()` | `TrellisServiceBuilder` | **AOT-safe.** Registers `TrackedAggregateDomainEventDispatchBehavior<,>` and the default `IDomainEventPublisher` without scanning. Implies `UseMediator()`. Mutually exclusive with `UseDomainEvents(...)`. |
| `public TrellisServiceBuilder UseTrackedAggregateDomainEvents<TEvent, THandler>() where TEvent : IDomainEvent where THandler : class, IDomainEventHandler<TEvent>` | `TrellisServiceBuilder` | **AOT-safe.** Registers `THandler` for `TEvent` and wires the tracked dispatch behavior. Implies `UseMediator()`. Mutually exclusive with `UseDomainEvents(...)`. |
| `[RequiresUnreferencedCode] [RequiresDynamicCode] public TrellisServiceBuilder UseTrackedAggregateDomainEvents(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Scans assemblies for `IDomainEventHandler<TEvent>` implementations (non-AOT). Implies `UseMediator()`. Mutually exclusive with `UseDomainEvents(...)`. |

## Behavior

`AddTrellis(...)` records selected modules first and then applies them in this order:

1. ASP integration.
2. ProblemDetails customization (when `UseProblemDetails()` is selected).
3. Idempotency-Key middleware DI (when `UseIdempotency(...)` is selected).
4. Actor provider (the optional caching wrap that chains after it, then the optional worker-actor wrap that chains after caching).
5. Mediator behaviors.
6. Resource authorization (assembly scanning, when assemblies are supplied).
7. FluentValidation adapter/scanning when selected.
8. Domain event dispatch (registers `DomainEventDispatchBehavior<,>`, the default `IDomainEventPublisher`, and any scanned handlers).
9. EF Core Unit of Work.

That order preserves the important pipeline invariant: `TransactionalCommandBehavior<,>` is the innermost behavior, closest to the handler, so commit failures remain visible to outer logging/tracing/exception behaviors.

### Order-independence for explicit resource-authorization registrations

Explicit `services.AddResourceAuthorization<TMessage, TResource, TResponse>()` calls made BEFORE `AddTrellis(...)` are now order-independent. `AddTrellisBehaviors()` (called by `UseMediator()`) detects any pre-existing closed-generic `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` descriptors and re-positions them to sit immediately before `ValidationBehavior<,>`, so they end up in the canonical pipeline envelope regardless of registration order. This mirrors the symmetry between `AddTrellisUnitOfWork<TContext>` and `AddDomainEventDispatch`.

`AddTrellis(...)` deliberately does **not** register:

- `AddDbContext<TContext>(...)` — provider, connection string, pooling, migrations, and interceptors are application-owned.
- `AddMediator(...)` — handler discovery/source-generator configuration is application-owned.
- route constraints — route parameter names are application-owned.

## Examples

```csharp
// Error-to-status mapping only (no scalar-value JSON / model-binding wiring).
services.AddTrellis(options => options.UseAsp());
```

```csharp
// Controller-host default: error mapping + scalar-value validation.
services.AddTrellis(options => options
    .UseAsp()
    .UseScalarValueValidation()
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
    .UseScalarValueValidation()
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
