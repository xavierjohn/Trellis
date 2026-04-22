# Trellis.Mediator — API Reference

**Package:** `Trellis.Mediator`
**Namespace:** `Trellis.Mediator`
**Purpose:** Provides Trellis result-aware Mediator pipeline behaviors plus DI helpers for validation, authorization, tracing, logging, and optional resource authorization.

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
| `PipelineBehaviors` | `IReadOnlyList<Type>` | Ordered pipeline behavior types: `ExceptionBehavior<,>`, `TracingBehavior<,>`, `LoggingBehavior<,>`, `AuthorizationBehavior<,>`, `ValidationBehavior<,>`. Resource authorization is not part of this list. |

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

### ValidationBehavior<TMessage, TResponse>
**Declaration**

```csharp
public sealed class ValidationBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse> where TMessage : IValidate, global::Mediator.IMessage where TResponse : IResult, IFailureFactory<TResponse>
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public ValidationBehavior<TMessage, TResponse>()` | Implicit parameterless constructor. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Calls `message.Validate()`. When validation fails, returns `TResponse.CreateFailure(validationResult.Error)` using whatever error object `Validate()` returned; it is not limited to `Error.UnprocessableContent`. |

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
```

## Code examples

### Registering behaviors and explicit resource authorization

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
services.AddScoped<IResourceLoader<GetOrderQuery, Order>, GetOrderResourceLoader>();
services.AddTrellisBehaviors();
services.AddResourceAuthorization<GetOrderQuery, Order, Result<Order>>();

var behaviorOrder = Trellis.Mediator.ServiceCollectionExtensions.PipelineBehaviors;
Console.WriteLine(string.Join(", ", behaviorOrder.Select(type => type.Name)));

public sealed record Order(string Id, string OwnerId);

public sealed record GetOrderQuery(string Id)
    : IQuery<Result<Order>>, IAuthorize, IAuthorizeResource<Order>, IValidate
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:read"];

    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Id)
            ? Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(Id)), "required") { Detail = "Order ID is required." })))
            : Result.Ok();

    public IResult Authorize(Actor actor, Order resource) =>
        actor.IsOwner(resource.OwnerId)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("orders.read") { Detail = "Only the owner can view the order." });
}

public sealed class GetOrderResourceLoader : IResourceLoader<GetOrderQuery, Order>
{
    public Task<Result<Order>> LoadAsync(GetOrderQuery message, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Ok(new Order(message.Id, "user-1")));
}

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
