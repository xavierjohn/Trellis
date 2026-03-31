# Trellis.Mediator — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-authorization.md, trellis-api-asp.md.

**Package:** `Trellis.Mediator` | **Namespace:** `Trellis.Mediator`

> **Import:** Add `using Trellis.Mediator;` for registration extensions (`AddTrellisBehaviors`, `AddResourceAuthorization`). Commands and queries use `Mediator` namespace (`ICommand<T>`, `IQuery<T>`) from the Mediator library. Authorization interfaces use `using Trellis.Authorization;`.

CQRS pattern: define a command/query record implementing `ICommand<T>`/`IQuery<T>`, implement a handler, register with `AddTrellisBehaviors()`. The mediator dispatches through the pipeline behavior chain.

### Pipeline Order

Exception → Tracing → Logging → Authorization → ResourceAuthorization (actor-only) → Validation

Resource-based authorization with a loaded resource (`IAuthorizeResource<TResource>`) is auto-discovered via `AddResourceAuthorization(Assembly)`, or registered explicitly per-command via `AddResourceAuthorization<TMessage, TResource, TResponse>()` for AOT scenarios.

### Behaviors

Pipeline behaviors execute in order: `ExceptionBehavior` (catch unhandled) → `TracingBehavior` (OpenTelemetry) → `LoggingBehavior` (structured logging) → `AuthorizationBehavior` (permission check) → `ResourceAuthorizationBehavior` (ownership check) → `ValidationBehavior` (IValidate) → Handler.

| Behavior | Constraint on TMessage | Purpose |
|----------|----------------------|---------|
| `ExceptionBehavior` | `IMessage` | Catches unhandled exceptions → `Error.Unexpected` |
| `TracingBehavior` | `IMessage` | OpenTelemetry Activity span (`ActivitySourceName = "Trellis.Mediator"`) |
| `LoggingBehavior` | `IMessage` | Structured logging with duration |
| `AuthorizationBehavior` | `IAuthorize, IMessage` | Checks `HasAllPermissions` → `Error.Forbidden` |
| `ResourceAuthorizationBehavior<,,>` | `IAuthorizeResource<TResource>, IMessage` | Loads resource via `IResourceLoader`, delegates to `message.Authorize(actor, resource)`. Auto-discovered via `AddResourceAuthorization(Assembly)`. |
| `ValidationBehavior` | `IValidate, IMessage` | Calls `message.Validate()`, short-circuits |

### IValidate Interface

Implement on a command/query to add validation before the handler runs. The `ValidationBehavior` calls `Validate()` and short-circuits with `ValidationError` on failure.

```csharp
interface IValidate { IResult Validate(); }
```

### TracingBehavior Constants

OpenTelemetry activity source names used by the mediator tracing behavior for distributed tracing.

```csharp
public const string ActivitySourceName = "Trellis.Mediator";
```

Use `ActivitySourceName` to register the activity source with OpenTelemetry: `builder.AddSource(TracingBehavior<IMessage, IResult>.ActivitySourceName)`.

### Registration

`AddTrellisBehaviors()` registers all pipeline behaviors. `AddResourceAuthorization(params Assembly[])` scans assemblies for `IResourceLoader` implementations.

```csharp
services.AddTrellisBehaviors();

// Recommended: scan-register both IAuthorizeResource<T> behaviors and IResourceLoader<,> implementations
services.AddResourceAuthorization(typeof(CancelOrderCommand).Assembly);

// OR: explicit per-command registration (AOT-compatible)
services.AddResourceAuthorization<CancelOrderCommand, Order, Result<Order>>();
services.AddResourceLoaders(typeof(CancelOrderResourceLoader).Assembly);
```

---
