# Trellis.ServiceDefaults API Reference

**Package:** `Trellis.ServiceDefaults`  
**Namespace:** `Trellis.ServiceDefaults`  
**Purpose:** Opinionated composition builder for API/composition-root projects that want Trellis integration modules applied in the canonical order.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-12--di-wiring-playbook-addtrellis-composition-builder) — composition-root recipe.

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
| `public TrellisServiceBuilder UseResourceAuthorization(params Assembly[] assemblies)` | `TrellisServiceBuilder` | Registers resource authorization behaviors/loaders discovered in assemblies. Implies `UseMediator()`. |
| `public TrellisServiceBuilder UseClaimsActorProvider(Action<ClaimsActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `ClaimsActorProvider` as `IActorProvider`. |
| `public TrellisServiceBuilder UseEntraActorProvider(Action<EntraActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `EntraActorProvider` as `IActorProvider`. |
| `public TrellisServiceBuilder UseDevelopmentActorProvider(Action<DevelopmentActorOptions>? configure = null)` | `TrellisServiceBuilder` | Registers `DevelopmentActorProvider` as `IActorProvider`. Use only in development/testing hosts. |
| `public TrellisServiceBuilder UseEntityFrameworkUnitOfWork<TContext>() where TContext : DbContext` | `TrellisServiceBuilder` | Registers `EfUnitOfWork<TContext>` and `TransactionalCommandBehavior<,>` via `AddTrellisUnitOfWork<TContext>()`. Implies `UseMediator()` and is applied last. |

## Behavior

`AddTrellis(...)` records selected modules first and then applies them in this order:

1. ASP integration.
2. Actor provider.
3. Mediator behaviors.
4. Resource authorization.
5. FluentValidation adapter/scanning when selected.
6. EF Core Unit of Work.

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
services.AddTrellis(options => options
    .UseAsp()
    .UseMediator()
    .UseClaimsActorProvider()
    .UseResourceAuthorization(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```
