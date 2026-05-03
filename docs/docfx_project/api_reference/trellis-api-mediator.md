---
package: Trellis.Mediator
namespaces: [Trellis.Mediator]
types: [ICommand<T>, IQuery<T>, "IRequestHandler<,>", "IPipelineBehavior<,>", ServiceCollectionExtensions, IDomainEventHandler<TEvent>, IDomainEventPublisher, "DomainEventDispatchBehavior<,>", DomainEventDispatchServiceCollectionExtensions]
version: v3
last_verified: 2026-05-02
audience: [llm]
---
# Trellis.Mediator — API Reference

**Package:** `Trellis.Mediator`
**Namespace:** `Trellis.Mediator`
**Purpose:** Provides Trellis result-aware Mediator pipeline behaviors plus DI helpers for validation, authorization, tracing, logging, and optional resource authorization.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

## Use this file when

- You are wiring Trellis result-aware behaviors into the `Mediator` pipeline.
- You need exact command/query interfaces, validation behavior, static authorization, resource authorization, tracing/logging, or EF unit-of-work behavior.
- You need to know which DI helper registers a behavior versus which helper registers resource loaders.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Add the standard Trellis mediator behaviors | `services.AddTrellisBehaviors()` | [`ServiceCollectionExtensions`](#servicecollectionextensions) |
| Add validation to a message | Implement `IValidate` and register `IMessageValidator<TMessage>` or FluentValidation adapter | [`ValidationBehavior<TMessage,TResponse>`](#validationbehaviortmessage-tresponse) |
| Add static permission authorization | Message implements `IAuthorize`; register `AddTrellisBehaviors()` | [`AuthorizationBehavior<TMessage,TResponse>`](#authorizationbehaviortmessage-tresponse) |
| Add resource authorization with assembly scanning | `services.AddResourceAuthorization(typeof(SomeType).Assembly)` | [`ServiceCollectionExtensions`](#servicecollectionextensions) |
| Add resource authorization explicitly | `services.AddResourceAuthorization<TMessage,TResource,TResponse>()` plus loader registration | [`ResourceAuthorizationBehavior<TMessage,TResource,TResponse>`](#resourceauthorizationbehaviortmessage-tresource-tresponse) |
| Bridge `IIdentifyResource<TResource,TId>` to a shared loader | `services.AddSharedResourceLoader<TMessage,TResource,TId>()` | [`ServiceCollectionExtensions`](#servicecollectionextensions) |
| Register EF unit-of-work behavior | `services.AddTrellisUnitOfWork<TContext>()` | [`Canonical pipeline order`](#canonical-pipeline-order) |
| Keep commits inside the pipeline | Repositories stage changes; `TransactionalCommandBehavior` commits on success | [`Behavioral notes`](#behavioral-notes) |
| Dispatch domain events on a successful command (assembly scan) | `services.AddDomainEventDispatch(typeof(MyHandler).Assembly)` | [`DomainEventDispatchServiceCollectionExtensions`](#domaineventdispatchservicecollectionextensions) |
| Dispatch domain events with explicit (AOT-friendly) handler registration | `services.AddDomainEventHandler<TEvent, THandler>()` | [`DomainEventDispatchServiceCollectionExtensions`](#domaineventdispatchservicecollectionextensions) |
| Implement a domain-event handler | Implement `IDomainEventHandler<TEvent>` | [`IDomainEventHandler`](#idomaineventhandler) |

## Common traps

- Explicit `AddResourceAuthorization<TMessage,TResource,TResponse>()` inserts the behavior only; it does not automatically register the shared-loader bridge.
- `AddTrellisUnitOfWork<TContext>()` should be registered after other behavior registrations so the transaction behavior is innermost.
- Handlers should return Trellis `Result` / `Result<T>` failures, not throw for expected business outcomes.

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

### IDomainEventHandler
**Declaration**

```csharp
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
```

Handles a domain event raised by an `IAggregate`. Implementations are resolved via DI by [`DomainEventDispatchBehavior`](#domaineventdispatchbehavior) after a successful command and invoked once per matching event.

Dispatch matches the runtime type of the event **exactly**; base-type and interface-type handlers are not resolved automatically. Handlers must be **idempotent** and treat their work as a best-effort side effect — exceptions thrown by a handler are logged at error level and swallowed so that other handlers, other events, and the originating command still complete. Handlers should treat themselves as side-effect-only: although the dispatcher drains handler-raised events on the same aggregate across subsequent waves (capped at 8), those re-entered events are dispatched *without being persisted* — the originating command's transaction has already committed. The drain-in-waves loop exists to avoid silently dropping events from accidental re-entry, not as a supported pattern for cascading domain mutations; persist-and-emit chains belong inside command handlers, not domain-event handlers.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `ValueTask HandleAsync(TEvent domainEvent, CancellationToken cancellationToken)` | `ValueTask` | Handles the specified domain event. The cancellation token is propagated from the originating command pipeline. |

### IDomainEventPublisher
**Declaration**

```csharp
public interface IDomainEventPublisher
```

Publishes a single `IDomainEvent` by resolving and invoking all `IDomainEventHandler<TEvent>` registrations for the event's runtime type. Application code rarely needs to inject this directly; it is useful for non-pipeline contexts such as background jobs or scheduled tasks that want to fan out an event the same way the pipeline would.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)` | `ValueTask` | Publishes to all matching handlers. Resolution uses `domainEvent.GetType()`. Handler exceptions are logged and swallowed. Default implementation (`MediatorDomainEventPublisher`) is `internal` and registered by `AddDomainEventDispatch()`. |

### DomainEventDispatchBehavior
**Declaration**

```csharp
public sealed class DomainEventDispatchBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult
```

Pipeline behavior that dispatches domain events accumulated on the success-value aggregate after the command handler returns. Constrained to `ICommand<TResponse>` so queries returning aggregate types do not trigger dispatch. Dispatch only runs when the response is a successful `Result<TAggregate>` where `TAggregate` implements `IAggregate`; other response shapes (`Result<Unit>`, `Result<TDto>`, `Result<(A,B)>`) pass through untouched.

When `TransactionalCommandBehavior` is also registered, dispatch fires after the transaction commits — handlers see committed state. Events are dispatched in waves with index tracking: each wave snapshots `aggregate.UncommittedEvents()` and dispatches events at indices the previous wave didn't reach, so events raised by a handler on the same aggregate accumulate at the next index and are picked up on the next wave. `IChangeTracking.AcceptChanges()` is called **once at the end of the loop**, only on full success — cancellation propagates above this call so undispatched (and dispatched) events stay on the aggregate, and handlers must be idempotent because a retry will re-publish events that already fired. The wave count is capped (`MaxDispatchWaves = 8`); if the cap is exceeded the remaining events are abandoned, an error is logged, and `AcceptChanges()` is called defensively to keep the aggregate from staying dirty across requests.

**Constants**

| Name | Type | Description |
| --- | --- | --- |
| `MaxDispatchWaves` | `int` (8) | Maximum number of dispatch waves before the loop bails. v1 expects single-wave dispatch; this cap exists to surface accidental re-entry. |

**Constructors**

| Signature | Description |
| --- | --- |
| `public DomainEventDispatchBehavior(IDomainEventPublisher publisher, ILogger<DomainEventDispatchBehavior<TMessage, TResponse>> logger)` | Resolves the publisher used to fan out events to registered handlers. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Awaits `next`, returns immediately on failure or when the response is not an aggregate `Result<TAggregate>`. On success, drains `aggregate.UncommittedEvents()` in waves with index tracking; calls `AcceptChanges()` **once at the end** of a fully successful loop. Cancellation propagates above the `AcceptChanges()` call so undispatched events remain on the aggregate. |

### DomainEventDispatchServiceCollectionExtensions
**Declaration**

```csharp
public static class DomainEventDispatchServiceCollectionExtensions
```

DI registration helpers for the dispatch behavior, default publisher, and per-event handler bindings.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services)` | `IServiceCollection` | Registers `DomainEventDispatchBehavior<,>` (open generic, scoped) and the default `IDomainEventPublisher` (`MediatorDomainEventPublisher`, scoped). Calls `AddTrellisBehaviors()` first so the always-on behaviors are present. **Idempotent**. AOT-friendly (no scanning). |
| `public static IServiceCollection AddDomainEventHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services) where TEvent : IDomainEvent where THandler : class, IDomainEventHandler<TEvent>` | `IServiceCollection` | Registers a single `IDomainEventHandler<TEvent>` implementation as scoped, and ensures the dispatch behavior + publisher are wired. Use this for AOT/trim scenarios. **Idempotent**. |
| `[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use AddDomainEventHandler<TEvent, THandler> for AOT/trim scenarios.")] [RequiresDynamicCode("Constructs closed generic IDomainEventHandler<TEvent> at runtime.")] public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services, params Assembly[] assemblies)` | `IServiceCollection` | Scans the assemblies for concrete `IDomainEventHandler<TEvent>` implementations and registers each as scoped. A type implementing handlers for multiple event types is registered once per interface. Also wires the dispatch behavior + publisher (idempotent). |


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

### Trellis.Mediator.DomainEventDispatchServiceCollectionExtensions

```csharp
public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services)
public static IServiceCollection AddDomainEventHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
    where TEvent : IDomainEvent
    where THandler : class, IDomainEventHandler<TEvent>
[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use AddDomainEventHandler<TEvent, THandler> for AOT/trim scenarios.")]
[RequiresDynamicCode("Constructs closed generic IDomainEventHandler<TEvent> at runtime.")]
public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services, params Assembly[] assemblies)
```

## Interfaces

```csharp
public interface IValidate
public interface IMessageValidator<in TMessage> where TMessage : global::Mediator.IMessage
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
public interface IDomainEventPublisher
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
7. **`DomainEventDispatchBehavior<,>`** *(opt-in via `AddDomainEventDispatch(...)`)* — runs for `ICommand<TResponse>` messages where `TResponse` is `Result<TAggregate>` and `TAggregate : IAggregate`. After the inner pipeline returns success, dispatches each event from `aggregate.UncommittedEvents()` to its registered `IDomainEventHandler<TEvent>` instances and calls `AcceptChanges()`. When `TransactionalCommandBehavior` is registered innermost, dispatch fires after the transaction commits — handlers see committed state. Handler exceptions are logged and swallowed; the command still succeeds.
8. **`TransactionalCommandBehavior<,>`** *(opt-in, lives in `Trellis.EntityFrameworkCore`, not registered by `AddTrellisBehaviors()`)* — wraps the handler for `ICommand<TResponse>` messages and calls `IUnitOfWork.CommitAsync` on success. Register via `AddTrellisUnitOfWork<TContext>()` from the EFCore package **after** `AddTrellisBehaviors()` and `AddDomainEventDispatch(...)` so it lands innermost (closest to the handler) and commit failures remain visible to outer logging/tracing/dispatch. Queries are skipped.

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
services.AddSharedResourceLoader<GetOrderQuery, Order, OrderId>();

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

### Domain event dispatch — order confirmation email handler

```csharp
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis;
using Trellis.Mediator;

// 1. Domain side: an aggregate raises an event during a state-changing method.
public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed record OrderSubmitted(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id) : base(id) { }

    public Result<Order> Submit(TimeProvider clock)
    {
        DomainEvents.Add(new OrderSubmitted(Id, clock.GetUtcNow()));
        return this;
    }
}

// 2. Command and handler return Result<Order>.
public sealed record SubmitOrderCommand(OrderId OrderId) : ICommand<Result<Order>>;

public sealed class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand, Result<Order>>
{
    private readonly TimeProvider _clock;
    public SubmitOrderCommandHandler(TimeProvider clock) => _clock = clock;

    public ValueTask<Result<Order>> Handle(SubmitOrderCommand command, CancellationToken cancellationToken)
        => new(new Order(command.OrderId).Submit(_clock));
}

// 3. Email handler runs after the command succeeds. Side effects are best-effort:
//    catch + log + return; never let an email failure roll back the user's order submission.
public sealed class OrderConfirmationEmailHandler : IDomainEventHandler<OrderSubmitted>
{
    private readonly ILogger<OrderConfirmationEmailHandler> _logger;
    public OrderConfirmationEmailHandler(ILogger<OrderConfirmationEmailHandler> logger) => _logger = logger;

    public ValueTask HandleAsync(OrderSubmitted domainEvent, CancellationToken cancellationToken)
    {
        // await _email.SendAsync(...) etc.
        _logger.LogInformation("Order {OrderId} submitted at {OccurredAt}", domainEvent.OrderId, domainEvent.OccurredAt);
        return ValueTask.CompletedTask;
    }
}

// 4. Composition root: register the dispatch behavior + the handler.
//    Use AddDomainEventDispatch(assemblies) for scanning, OR AddDomainEventHandler<,>() per handler for AOT.
var services = new ServiceCollection();
services.AddSingleton(TimeProvider.System);
services.AddLogging();
services.AddTrellisBehaviors();
services.AddDomainEventDispatch(typeof(OrderConfirmationEmailHandler).Assembly);
// AOT alternative:
//   services.AddDomainEventDispatch();
//   services.AddDomainEventHandler<OrderSubmitted, OrderConfirmationEmailHandler>();
```

## Cross-references

- [trellis-api-authorization.md](trellis-api-authorization.md)
- [trellis-api-core.md](trellis-api-core.md)
- [trellis-api-asp.md](trellis-api-asp.md)
