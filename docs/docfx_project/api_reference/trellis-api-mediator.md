# Trellis.Mediator — API Reference

**Package:** `Trellis.Mediator`
**Namespace:** `Trellis.Mediator`
**Purpose:** Provides Trellis result-aware Mediator pipeline behaviors plus DI helpers for validation, authorization, tracing, logging, and optional resource authorization.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

## Types

### AuthorizationBehavior<TMessage, TResponse>
**Declaration**

```csharp
public sealed class AuthorizationBehavior<TMessage, TResponse>(IActorProvider actorProvider) : IPipelineBehavior<TMessage, TResponse> where TMessage : IAuthorize, global::Mediator.IMessage where TResponse : IResult, IFailureFactory<TResponse>
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public AuthorizationBehavior(IActorProvider actorProvider)` | Builds the static-permission behavior. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Resolves the current actor, checks `RequiredPermissions` with `HasAllPermissions`, and returns `TResponse.CreateFailure(new Error.Forbidden("authorization.insufficient.permissions") { Detail = "Insufficient permissions." })` when authorization fails. Throws `InvalidOperationException` when no actor can be resolved. |

### ExceptionBehavior<TMessage, TResponse>
**Declaration**

```csharp
public sealed partial class ExceptionBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse> where TMessage : global::Mediator.IMessage where TResponse : IResult, IFailureFactory<TResponse>
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public ExceptionBehavior(ILogger<ExceptionBehavior<TMessage, TResponse>> logger)` | Builds the exception-to-failure behavior. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Catches unhandled exceptions except `OperationCanceledException`, logs them, and returns `TResponse.CreateFailure(new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "An unexpected error occurred while processing the request." })`. |

### IValidate
**Declaration**

```csharp
public interface IValidate
```

**Constructors**

No public constructors.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `IResult Validate()` | `IResult` | Returns success to continue or any failure result to short-circuit the pipeline. |

### LoggingBehavior<TMessage, TResponse>
**Declaration**

```csharp
public sealed partial class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse> where TMessage : global::Mediator.IMessage where TResponse : IResult
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public LoggingBehavior(ILogger<LoggingBehavior<TMessage, TResponse>> logger, TrellisMediatorTelemetryOptions? options = null)` | Builds the logging behavior. `options` is resolved from DI; when `null` (i.e. not registered) the safe-by-default options are used and `Error.Detail` is redacted. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Logs start (Information), end with elapsed milliseconds (Information on success, Warning on failure). On failure emits `error.Code` only by default; the free-text `Error.Detail` is included only when `TrellisMediatorTelemetryOptions.IncludeErrorDetail` is `true`. |

### ResourceAuthorizationBehavior<TMessage, TResource, TResponse>
**Declaration**

```csharp
public sealed class ResourceAuthorizationBehavior<TMessage, TResource, TResponse>(IActorProvider actorProvider, IServiceProvider serviceProvider) : IPipelineBehavior<TMessage, TResponse> where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage where TResponse : IResult, IFailureFactory<TResponse>
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public ResourceAuthorizationBehavior(IActorProvider actorProvider, IServiceProvider serviceProvider)` | Builds the resource-loading authorization behavior. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Resolves the actor from `IActorProvider` first (throws `InvalidOperationException` when null — fail fast before doing any I/O). Then resolves `IResourceLoader<TMessage, TResource>` from the current scope, returns loader failures directly, and finally calls `message.Authorize(actor, loadResult.Unwrap())` before invoking the handler. This behavior is only active when registered explicitly or via `AddResourceAuthorization(...)`; it is not included in `AddTrellisBehaviors()` or `PipelineBehaviors`. |

### ServiceCollectionExtensions
**Declaration**

```csharp
public static class ServiceCollectionExtensions
```

**Constructors**

No public constructors.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `PipelineBehaviors` | `IReadOnlyList<Type>` | Ordered pipeline behavior types (outermost → innermost): `ExceptionBehavior<,>`, `TracingBehavior<,>`, `LoggingBehavior<,>`, `AuthorizationBehavior<,>`, `ValidationBehavior<,>`. Resource authorization and the EFCore `TransactionalCommandBehavior` are opt-in and not part of this list. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services)` | `IServiceCollection` | Registers the five open generic behaviors listed in `PipelineBehaviors` and a default `TrellisMediatorTelemetryOptions` singleton (Detail redacted). **Idempotent** — uses `TryAddEnumerable`/`TryAddSingleton` so repeat calls (e.g. from plug-in extensions like `AddTrellisFluentValidation`, `AddTrellisAsp`) do not duplicate registrations. |
| `public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services, Action<TrellisMediatorTelemetryOptions> configure)` | `IServiceCollection` | Same as the parameterless overload, but applies `configure` to the registered `TrellisMediatorTelemetryOptions` singleton. Replaces any prior options registration so this call wins regardless of ordering. |
| `public static IServiceCollection AddResourceAuthorization<TMessage, TResource, TResponse>(this IServiceCollection services) where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage where TResponse : IResult, IFailureFactory<TResponse>` | `IServiceCollection` | Registers `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` and inserts it immediately before `ValidationBehavior<,>` when validation is already registered. |
| `[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")] [RequiresDynamicCode("Constructs closed generic types at runtime. Use explicit registration for AOT scenarios.")] public static IServiceCollection AddResourceAuthorization(this IServiceCollection services, params Assembly[] assemblies)` | `IServiceCollection` | Scans assemblies for `IAuthorizeResource<TResource>` implementations, resolves `TResponse` from `ICommand<T>`, `IQuery<T>`, or `IRequest<T>`, registers closed `ResourceAuthorizationBehavior<,,>` instances, registers discovered `IResourceLoader<,>` implementations, registers discovered `SharedResourceLoaderById<,>` implementations, and bridges `IIdentifyResource<TResource, TId>` messages to shared loaders when no explicit loader is registered. |
| `[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")] public static IServiceCollection AddResourceLoaders(this IServiceCollection services, Assembly assembly)` | `IServiceCollection` | Registers discovered `IResourceLoader<,>` implementations with `TryAddScoped`. |
| `public static IServiceCollection AddSharedResourceLoader<TMessage, TResource, TId>(this IServiceCollection services) where TMessage : IAuthorizeResource<TResource>, IIdentifyResource<TResource, TId>` | `IServiceCollection` | Registers `SharedResourceLoaderAdapter<TMessage, TResource, TId>` as `IResourceLoader<TMessage, TResource>`. |

### TracingBehavior<TMessage, TResponse>
**Declaration**

```csharp
public sealed class TracingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse> where TMessage : global::Mediator.IMessage where TResponse : IResult
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public TracingBehavior(TrellisMediatorTelemetryOptions? options = null)` | Builds the tracing behavior. `options` is resolved from DI; when `null` (i.e. not registered) the safe-by-default options are used and `Error.Detail` is redacted from `Activity.StatusDescription`. |

**Fields**

| Name | Type | Description |
| --- | --- | --- |
| `ActivitySourceName` | `string` | Public constant activity source name. Value: `"Trellis.Mediator"`. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Starts an activity named after `TMessage`. On failure tags the activity with `error.code` (the stable `Error.Code`) and `error.type` (the stable error class name); sets `ActivityStatusCode.Error`. The `StatusDescription` is left empty by default — the free-text `Error.Detail` is included only when `TrellisMediatorTelemetryOptions.IncludeErrorDetail` is `true`. On success sets `ActivityStatusCode.Ok`. Rethrows thrown exceptions after marking the activity as error and tagging `error.type`; the exception message is **not** copied into telemetry. |

### TrellisMediatorTelemetryOptions
**Declaration**

```csharp
public sealed class TrellisMediatorTelemetryOptions
```

Operator-tunable redaction settings consumed by `LoggingBehavior` and `TracingBehavior`. Resolved from DI; when not registered the behaviors fall back to a default-constructed instance (Detail redacted).

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `IncludeErrorDetail` | `bool` | When `true`, the logging and tracing behaviors include `Error.Detail` in their emitted message and activity status description. Defaults to `false` (Detail is redacted; only the stable `Error.Code` and type name are emitted). |

### IMessageValidator<TMessage>
**Declaration**

```csharp
public interface IMessageValidator<in TMessage>
    where TMessage : global::Mediator.IMessage
```

Extensibility hook for the unified validation stage. Implementations are resolved from DI as `IEnumerable<IMessageValidator<TMessage>>` by `ValidationBehavior<TMessage, TResponse>`; every registered validator runs before the handler. External packages (e.g., `Trellis.FluentValidation`) plug additional validation sources into the pipeline through this interface without taking a dependency on a specific validation library or message-side interface from `Trellis.Mediator`.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `ValueTask<IResult> ValidateAsync(TMessage message, CancellationToken cancellationToken)` | `ValueTask<IResult>` | Returns `Result.Ok()` on success, or `Result.Fail(new Error.UnprocessableContent(...))` with field/rule violations on failure. `Error.UnprocessableContent` failures from every validator (and `IValidate.Validate()` if implemented) are aggregated into a single response failure by `ValidationBehavior`. Returning a non-`Error.UnprocessableContent` failure (e.g., `Error.Conflict`, `Error.Forbidden`) is allowed but short-circuits the stage immediately and is propagated as-is. |

### ValidationBehavior<TMessage, TResponse>
**Declaration**

```csharp
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IMessageValidator<TMessage>> validators) : IPipelineBehavior<TMessage, TResponse> where TMessage : global::Mediator.IMessage where TResponse : IResult, IFailureFactory<TResponse>
```

Unified validation stage. Runs `IValidate.Validate()` (when the message implements `IValidate`) and every `IMessageValidator<TMessage>` registered in DI for the message, then aggregates `Error.UnprocessableContent` failures into a single response. The behavior is registered for **all** messages — when the message does not implement `IValidate` and no validators are registered it is a no-op pass-through.

**Constructors**

| Signature | Description |
| --- | --- |
| `public ValidationBehavior(IEnumerable<IMessageValidator<TMessage>> validators)` | Receives every `IMessageValidator<TMessage>` registered in DI. The collection is iterated once per request. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Aggregation rules: (1) Multiple `Error.UnprocessableContent` failures from `IValidate` and validators are merged into a single `Error.UnprocessableContent` whose `Fields` and `Rules` collect every reported violation. (2) An `Error.UnprocessableContent` with empty `Fields` AND empty `Rules` still short-circuits the handler — original failure semantics are preserved. (3) A non-`Error.UnprocessableContent` failure returned by any source short-circuits the stage immediately and is propagated as-is. |

## Extension methods

### Trellis.Mediator.ServiceCollectionExtensions

```csharp
public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services)
public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services, Action<TrellisMediatorTelemetryOptions> configure)
public static IServiceCollection AddResourceAuthorization<TMessage, TResource, TResponse>(this IServiceCollection services) where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage where TResponse : IResult, IFailureFactory<TResponse>
[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")]
[RequiresDynamicCode("Constructs closed generic types at runtime. Use explicit registration for AOT scenarios.")]
public static IServiceCollection AddResourceAuthorization(this IServiceCollection services, params Assembly[] assemblies)
[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")]
public static IServiceCollection AddResourceLoaders(this IServiceCollection services, Assembly assembly)
public static IServiceCollection AddSharedResourceLoader<TMessage, TResource, TId>(this IServiceCollection services) where TMessage : IAuthorizeResource<TResource>, IIdentifyResource<TResource, TId>
```

## Interfaces

```csharp
public interface IValidate
public interface IMessageValidator<in TMessage> where TMessage : global::Mediator.IMessage
```

## Behavioral notes

### Canonical pipeline order

The Trellis pipeline executes outermost → innermost in this order. The first five are registered by `AddTrellisBehaviors()`; the last two are opt-in.

1. **`ExceptionBehavior<,>`** — catches unhandled exceptions (except `OperationCanceledException`), logs them, and converts them to a typed `TResponse.CreateFailure(new Error.InternalServerError(...))`. Sits outermost so every other layer is wrapped.
2. **`TracingBehavior<,>`** — opens an OpenTelemetry `Activity` per message under the `"Trellis.Mediator"` activity source. On failure tags `error.code` / `error.type` and sets `ActivityStatusCode.Error`. `Error.Detail` is redacted from `StatusDescription` unless `TrellisMediatorTelemetryOptions.IncludeErrorDetail` is `true`.
3. **`LoggingBehavior<,>`** — structured logging with start/end and elapsed-ms entries; emits the stable `Error.Code` on failure. Inherits the same correlation context propagated by the surrounding `Activity`. `Error.Detail` is redacted unless `IncludeErrorDetail` is `true`.
4. **`AuthorizationBehavior<,>`** — runs for `IAuthorize` messages; resolves the actor and rejects with `new Error.Forbidden("authorization.insufficient.permissions")` when `RequiredPermissions` are not satisfied. No I/O.
5. **`ResourceAuthorizationBehavior<,,>`** *(opt-in via `AddResourceAuthorization(...)`)* — runs for `IAuthorizeResource<TResource>` messages. Inserted **immediately before `ValidationBehavior<,>`** so a 403 short-circuits before a 422 is computed. Resolves the actor first (fail-fast, no I/O when null), then loads the resource via `IResourceLoader<TMessage, TResource>` and calls `message.Authorize(actor, resource)`.
6. **`ValidationBehavior<,>`** — unified validation stage. Runs `IValidate.Validate()` if implemented, then every `IMessageValidator<TMessage>` resolved from DI; aggregates all `Error.UnprocessableContent` failures into a single response. External validation sources (e.g., the `Trellis.FluentValidation` adapter) participate here without occupying their own pipeline slot.
7. **`TransactionalCommandBehavior<,>`** *(opt-in, lives in `Trellis.EntityFrameworkCore`, not registered by `AddTrellisBehaviors()`)* — wraps the handler for `ICommand<TResponse>` messages and calls `IUnitOfWork.CommitAsync` on success. Register via `AddTrellisUnitOfWork<TContext>()` from the EFCore package **after** `AddTrellisBehaviors()` so it lands innermost (closest to the handler) and commit failures remain visible to outer logging/tracing. Queries are skipped.

## Code examples

### Registering behaviors and shared resource authorization

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

var services = new ServiceCollection();

services.AddScoped<IActorProvider, StaticActorProvider>();
services.AddScoped<SharedResourceLoaderById<Order, OrderId>, OrderResourceLoader>();
services.AddTrellisBehaviors();
services.AddResourceAuthorization<GetOrderQuery, Order, Result<Order>>();

var behaviorOrder = Trellis.Mediator.ServiceCollectionExtensions.PipelineBehaviors;
Console.WriteLine(string.Join(", ", behaviorOrder.Select(type => type.Name)));

public sealed partial class OrderId : RequiredGuid<OrderId>;
public sealed partial class ActorId : RequiredString<ActorId>;

public sealed record Order(OrderId Id, ActorId OwnerId);

public sealed record GetOrderQuery(OrderId Id)
    : IQuery<Result<Order>>, IAuthorize, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>, IValidate
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:read"];

    public OrderId GetResourceId() => Id;

    public IResult Validate() => Result.Ok();

    public IResult Authorize(Actor actor, Order resource) =>
        resource.OwnerId.Value == actor.Id
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("orders.read") { Detail = "Only the owner can view the order." });
}

public sealed class OrderResourceLoader : SharedResourceLoaderById<Order, OrderId>
{
    public override Task<Result<Order>> GetByIdAsync(OrderId id, CancellationToken cancellationToken) =>
        Task.FromResult(ActorId.TryCreate("user-1").Map(ownerId => new Order(id, ownerId)));
}

// Escape hatch: prefer IIdentifyResource<TResource, TId> + SharedResourceLoaderById<TResource, TId> in generated services.
// services.AddScoped<IResourceLoader<GetOrderQuery, Order>, GetOrderResourceLoader>();

public sealed class StaticActorProvider : IActorProvider
{
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Actor.Create("user-1", new HashSet<string> { "orders:read" }));
}
```

### Assembly scanning registration

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;

var services = new ServiceCollection();
Assembly[] assemblies = [typeof(SomeMessageInApplicationAssembly).Assembly];

services.AddTrellisBehaviors();
services.AddResourceAuthorization(assemblies);

public sealed class SomeMessageInApplicationAssembly { }
```

## Cross-references

- [trellis-api-authorization.md](trellis-api-authorization.md)
- [trellis-api-core.md](trellis-api-core.md)
- [trellis-api-asp.md](trellis-api-asp.md)
