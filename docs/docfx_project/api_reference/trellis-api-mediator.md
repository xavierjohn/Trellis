---
package: Trellis.Mediator
namespaces: [Trellis.Mediator]
types: [ICommand<T>, IQuery<T>, "IRequestHandler<,>", "IPipelineBehavior<,>", "AuthorizationBehavior<TMessage,TResponse>", "ExceptionBehavior<TMessage,TResponse>", IValidate, "LoggingBehavior<TMessage,TResponse>", "ResourceAuthorizationViaBehavior<TMessage,TLeaf,TOwner,TResponse>", ResolvedAuthorizationPath, ResolvedAuthorizationHop, HopLoadResult, "ResolvedAuthorizationPathHolder<TMessage,TLeaf,TOwner,TResponse>", ResourceAuthorizationPathResolver, "ResourceAuthorizationBehavior<TMessage,TResource,TResponse>", ServiceCollectionExtensions, "TracingBehavior<TMessage,TResponse>", TrellisMediatorTelemetryOptions, IMessageValidator<TMessage>, IDomainEventHandler<TEvent>, IDomainEventPublisher, DomainEventHandlerCascadedException, CascadeOffender, "DomainEventDispatchBehavior<,>", DomainEventDispatchServiceCollectionExtensions, DomainEventPublisherExtensions, "TrackedAggregateDomainEventDispatchBehavior<,>", TrackedAggregateDomainEventDispatchServiceCollectionExtensions]
version: v3
last_verified: 2026-06-03
audience: [llm]
---
# Trellis.Mediator — API Reference

**Package:** `Trellis.Mediator`
**Namespace:** `Trellis.Mediator`
**Purpose:** Provides Trellis result-aware Mediator pipeline behaviors plus DI helpers for validation, authorization, tracing, logging, and optional resource authorization.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#patterns-index) — recipes using this package.

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
| Hide existence of sensitive resources from unauthorized callers | `services.AddResourceAuthorization(o => o.HideExistence<TResource>())` | [`ResourceAuthorizationOptions`](#resourceauthorizationoptions) |
| Bridge `IIdentifyResource<TResource,TId>` to a shared loader | `services.AddSharedResourceLoader<TMessage,TResource,TId>()` | [`ServiceCollectionExtensions`](#servicecollectionextensions) |
| Register EF unit-of-work behavior | `services.AddTrellisUnitOfWork<TContext>()` | [`Canonical pipeline order`](#canonical-pipeline-order) |
| Keep commits inside the pipeline | Repositories stage changes; `TransactionalCommandBehavior` commits on success | [`Behavioral notes`](#behavioral-notes) |
| Dispatch domain events on a successful command (assembly scan) | `services.AddDomainEventDispatch(typeof(MyHandler).Assembly)` | [`DomainEventDispatchServiceCollectionExtensions`](#domaineventdispatchservicecollectionextensions) |
| Dispatch domain events with explicit (AOT-friendly) handler registration | `services.AddDomainEventHandler<TEvent, THandler>()` | [`DomainEventDispatchServiceCollectionExtensions`](#domaineventdispatchservicecollectionextensions) |
| Auto-dispatch domain events from every tracked aggregate (regardless of response shape) | `services.AddTrackedAggregateDomainEventDispatch()` | [`TrackedAggregateDomainEventDispatchServiceCollectionExtensions`](#trackedaggregatedomaineventdispatchservicecollectionextensions) |
| Implement a domain-event handler | Implement `IDomainEventHandler<TEvent>` | [`IDomainEventHandler`](#idomaineventhandler) |

## Common traps

- Explicit `AddResourceAuthorization<TMessage,TResource,TResponse>()` inserts the behavior only; it does not automatically register the shared-loader bridge.
- `AddTrellisUnitOfWork<TContext>()` should be registered after other behavior registrations so the transaction behavior is innermost.
- Handlers should return Trellis `Result` / `Result<T>` failures, not throw for expected business outcomes.

### Cross-package preflight for pipeline changes

Mediator pipeline work is rarely isolated. Load these companion references before changing registrations, behavior ordering, or handler patterns:

| If the change touches... | Also read | Why |
|---|---|---|
| EF-backed command commits or `AddTrellisUnitOfWork<TContext>()` | [`trellis-api-efcore.md`](trellis-api-efcore.md#unitofworkservicecollectionextensions), [`trellis-api-servicedefaults.md`](trellis-api-servicedefaults.md#trellisservicebuilder) | The transaction behavior is registered by EF Core and applied last by the service-defaults builder. |
| Resource authorization | [`trellis-api-authorization.md`](trellis-api-authorization.md#resource-based-authorization-with-a-shared-loader), [`trellis-api-efcore.md`](trellis-api-efcore.md#repositorybasetaggregate-tid) when resources load from repositories | The interfaces live in Authorization; resource loading often composes with EF repository/result semantics. |
| FluentValidation in the validation stage | [`trellis-api-mediator-fluentvalidation.md`](trellis-api-mediator-fluentvalidation.md#fluentvalidationservicecollectionextensions) | FluentValidation contributes `IMessageValidator<TMessage>` instances to `ValidationBehavior`; it does not add another pipeline slot. The adapter lives in the dedicated `Trellis.Mediator.FluentValidation` package. |
| ASP endpoints that send commands/queries | [`trellis-api-asp.md`](trellis-api-asp.md#endpoint-checklist-for-generated-apis) | Endpoint response mapping and scalar validation happen at the ASP boundary; handlers should stay transport-free. |

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
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Resolves the current actor via `IActorProvider`. When the provider returns `Maybe<Actor>.None`, short-circuits with `TResponse.CreateFailure(new Error.AuthenticationRequired { Detail = "Authentication required." })` (HTTP 401, RFC 9110 §15.5.2). When the actor is present but lacks one of `RequiredPermissions`, short-circuits with `TResponse.CreateFailure(new Error.Forbidden("authorization.insufficient.permissions") { Detail = "Insufficient permissions." })` (HTTP 403). The 401 vs 403 distinction is shared with `ResourceAuthorizationBehavior` and `ResourceAuthorizationViaBehavior` via the internal `ActorResolution.TryResolveAsync` / `ActorResolution.AuthenticationRequired()` helpers; provider-side `InvalidOperationException` (genuine bugs — no `HttpContext`, mapping delegate threw, etc.) propagates uncaught and surfaces as `Error.Unexpected` (HTTP 500) via `ExceptionBehavior`. |

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
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Catches unhandled exceptions except `OperationCanceledException`, logs them with a per-incident fault id, and returns `TResponse.CreateFailure(new Error.Unexpected("unhandled_exception", faultId) { Detail = "An unexpected error occurred while processing the request." })`. |

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
| `public LoggingBehavior(ILogger<LoggingBehavior<TMessage, TResponse>> logger, TrellisMediatorTelemetryOptions? options = null)` | Builds the logging behavior. `options` is resolved from DI; under `AddTrellisBehaviors()` the `TrellisMediatorTelemetryOptions` singleton is always registered, so this argument is non-null in production. The optional-null fallback exists only for consumers that instantiate the behavior outside of DI (custom test fixtures); when null, the safe-by-default options are used and `Error.Detail` is redacted. Throws `ArgumentNullException` when `logger` is null. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Logs start (Debug), end with elapsed milliseconds (Debug on success, Information for expected caller/domain failures, Warning for unexpected/dependency/opaque transport failures). Per-call timing is at Debug so production at the default `Information` minimum stays quiet; raise via `"Trellis.Mediator": "Debug"` in logging configuration to opt back in. On failure emits `error.Code` only by default; the free-text `Error.Detail` is included only when `TrellisMediatorTelemetryOptions.IncludeErrorDetail` is `true`. |

### ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>
**Declaration**

```csharp
public sealed partial class ResourceAuthorizationViaBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TMessage, TLeaf, TOwner, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorizeResourceVia<TOwner>, global::Mediator.IMessage
    where TLeaf : class
    where TResponse : IResult, IFailureFactory<TResponse>
```

Pipeline behavior implementing indirect (multi-hop) resource authorization. Loads the leaf via the existing `IResourceLoader<TMessage, TLeaf>` infrastructure (typically the `SharedResourceLoaderAdapter` bridge for messages also implementing `IIdentifyResource<TLeaf, TLeafId>`), then walks the pre-resolved `ResolvedAuthorizationPath` from leaf to owner, and finally invokes the command's `IAuthorizeResourceVia<TOwner>.Authorize(actor, IReadOnlyList<TOwner>)`.

**Constructors**

| Signature | Description |
| --- | --- |
| `public ResourceAuthorizationViaBehavior(IActorProvider actorProvider, IServiceProvider serviceProvider, ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse> pathHolder, IOptions<ResourceAuthorizationOptions>? options = null, ILogger<ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>>? logger = null)` | DI-friendly constructor; the closed-generic holder is registered per via-command so DI naturally disambiguates. `options` defaults to a fresh `ResourceAuthorizationOptions` (`DefaultExposurePolicy = Propagate`); `logger` defaults to `NullLogger.Instance`. |
| `public ResourceAuthorizationViaBehavior(IActorProvider actorProvider, IServiceProvider serviceProvider, ResolvedAuthorizationPath path, IOptions<ResourceAuthorizationOptions>? options = null, ILogger<ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>>? logger = null)` | Test/manual constructor accepting a hand-built path. Validates `path.MessageType`/`LeafType`/`OwnerType` match the behavior's generic arguments. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Resolves the actor before doing any I/O (including resolving the leaf loader from DI — loader construction is treated as I/O because the DI factory may open a `DbContext` or pre-fetch state). When the actor provider returns `Maybe<Actor>.None`, short-circuits with `TResponse.CreateFailure(new Error.AuthenticationRequired { Detail = "Authentication required." })`; provider-side `InvalidOperationException` still propagates as a deployment bug. Loads the leaf via `IResourceLoader<TMessage, TLeaf>` — leaf load failure bubbles verbatim. Walks the resolved path: per hop extracts IDs (de-duplicated, nulls filtered), loads each via the registered `SharedResourceLoaderById<TTo, TToId>` — intermediate/owner load failures collapse to `Error.Forbidden` (no existence leak); empty ID list at any hop short-circuits to `Error.Forbidden`. Finally calls `message.Authorize(actor, IReadOnlyList<TOwner>)` and returns its result, or invokes the handler when the authorization passes. **Failure-exposure policy.** Lookup key is `typeof(TLeaf)` (the resource the command identifies, not the owner). When `ResourceAuthorizationOptions` opts `TLeaf` into `HideAsNotFound`, all `Error.Forbidden` and `Error.AuthenticationRequired` outcomes — actor-required, leaf-load Forbidden, intermediate/owner load failures, empty-hop, null-payload, and `message.Authorize` denial — translate to `new Error.NotFound(ResourceRef)` referencing `TLeaf` (never `TOwner`). The pass-through guarantee for operational errors (`Error.Unexpected`, `Error.Unavailable`, transport faults) applies only to the LEAF loader's direct return value; intermediate / owner hop failures are already collapsed to the synthetic `Forbidden("resource.authorization-via.load-failed")` by the v1 multi-hop security model BEFORE the exposure-policy translation runs, so under `HideAsNotFound` an underlying `Unavailable` from a downstream owner service surfaces as `404` to the consumer. Translation emits the same `[LoggerMessage]` `ExistenceHidden` event as the direct behavior. See [Recipe 32](trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound). **Null-payload defense.** A loader that violates its `Result<T>` contract by returning `Result.Ok(null)` is treated as fail-closed rather than crashing the pipeline: a leaf null-payload short-circuits to `Error.Forbidden` with code `resource.authorization-via.null-payload` (caller-visible). A hop null-success is treated internally as a hop failure and — like every other intermediate/owner load failure — collapses to `Error.Forbidden` with code `resource.authorization-via.load-failed` (the underlying null-payload code is intentionally not surfaced, mirroring the existence-leak protection on hop failures generally). |

### ResolvedAuthorizationPath
**Declaration**

```csharp
public sealed class ResolvedAuthorizationPath
```

Pre-built navigation path from leaf to owner used by `ResourceAuthorizationViaBehavior<,,,>`. Topology is validated at construction: `Hops` non-empty, `hops[0].FromType == LeafType`, terminal `hops[N].ToType == OwnerType`, adjacent hops chain (`hops[i].ToType == hops[i+1].FromType`), at most one plural hop and only at the terminal position. The `Hops` collection is defensively copied.

**Constructors**

| Signature | Description |
| --- | --- |
| `public ResolvedAuthorizationPath(Type messageType, Type leafType, Type ownerType, IReadOnlyList<ResolvedAuthorizationHop> hops)` | Builds the path; throws `ArgumentException` on invariant violations. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `MessageType` | `Type` | Command/query type this path serves. |
| `LeafType` | `Type` | Leaf resource type the command identifies. |
| `OwnerType` | `Type` | Owner resource type authorization is evaluated against. |
| `Hops` | `IReadOnlyList<ResolvedAuthorizationHop>` | Ordered hops from leaf to owner. |

### ResolvedAuthorizationHop
**Declaration**

```csharp
public sealed class ResolvedAuthorizationHop
```

Single hop in an indirect authorization chain.

**Constructors**

| Signature | Description |
| --- | --- |
| `public ResolvedAuthorizationHop(Type fromType, Type toType, Type toIdType, Func<object, IReadOnlyList<object>> extractIds, Func<IServiceProvider, object, CancellationToken, Task<HopLoadResult>> loadAsync, bool isPlural)` | Builds the hop with typed extractor and loader delegates. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `FromType` | `Type` | Source resource type. |
| `ToType` | `Type` | Destination resource type. |
| `ToIdType` | `Type` | Identifier type for the destination resource. |
| `ExtractIds` | `Func<object, IReadOnlyList<object>>` | Extracts related-resource IDs from a single source instance. |
| `LoadAsync` | `Func<IServiceProvider, object, CancellationToken, Task<HopLoadResult>>` | Loads one resource by ID from the request-scoped service provider. |
| `IsPlural` | `bool` | True when the hop is plural (only the terminal hop may be plural). |

### HopLoadResult
**Declaration**

```csharp
public readonly struct HopLoadResult
```

Result of loading a single related resource at one ID during a hop walk. Uses an explicit success flag — `default(HopLoadResult)` is a failure with a sentinel error, so a misconfigured hop loader cannot accidentally produce a "successful" result carrying `null`.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static HopLoadResult Success(object value)` | `HopLoadResult` | Throws `ArgumentNullException` on null. |
| `public static HopLoadResult Failure(Error error)` | `HopLoadResult` | Throws `ArgumentNullException` on null. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `object?` | The loaded value when successful; `null` when failed. |
| `Error` | `Error?` | The loader's error when failed; `null` when successful. |
| `IsSuccess` | `bool` | False for `default(HopLoadResult)` so misconfigured loaders cannot silently bypass short-circuits. |

### ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse>
**Declaration**

```csharp
public sealed class ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse>
```

Closed-generic carrier that lets DI naturally disambiguate the `ResolvedAuthorizationPath` per via-authorized command. Each via-command's path is registered as `Singleton<ResolvedAuthorizationPathHolder<TM, TL, TO, TR>>(holder)`. The matching `ResourceAuthorizationViaBehavior<TM, TL, TO, TR>` constructor takes the holder, so registration is a typed (not factory) descriptor — letting the relocator recognize Trellis-owned descriptors by `ImplementationType` alone without a factory-shape heuristic.

### ResourceAuthorizationPathResolver
**Declaration**

```csharp
public static class ResourceAuthorizationPathResolver
```

Resolves a `ResolvedAuthorizationPath` from a leaf type to an owner type by walking the entity graph defined by `IIdentifyRelatedResource<TRelated, TId>` and `IIdentifyRelatedResources<TRelated, TId>` declarations on candidate entity types.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `[RequiresUnreferencedCode] [RequiresDynamicCode] public static ResolvedAuthorizationPath Resolve(Type messageType, Type leafType, Type ownerType, IReadOnlyCollection<Type> candidateEntityTypes)` | `ResolvedAuthorizationPath` | DFS-enumerates distinct simple paths from `leafType` to `ownerType`. Throws `InvalidOperationException` when no path exists, when multiple distinct simple paths exist (lists all paths in the message), when a plural hop is non-terminal, or when `leafType == ownerType`. Cycles in the graph are tolerated (per-path visited-set); duplicate candidate types are deduplicated. Builds typed extractor + loader delegates so the runtime hot path has no `dynamic` and no per-call reflection. |

### ResourceAuthorizationBehavior<TMessage, TResource, TResponse>
**Declaration**

```csharp
public sealed partial class ResourceAuthorizationBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TMessage, TResource, TResponse>(IActorProvider actorProvider, IServiceProvider serviceProvider, IOptions<ResourceAuthorizationOptions>? options = null, ILogger<ResourceAuthorizationBehavior<TMessage, TResource, TResponse>>? logger = null) : IPipelineBehavior<TMessage, TResponse> where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage where TResource : class where TResponse : IResult, IFailureFactory<TResponse>
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public ResourceAuthorizationBehavior(IActorProvider actorProvider, IServiceProvider serviceProvider, IOptions<ResourceAuthorizationOptions>? options = null, ILogger<ResourceAuthorizationBehavior<TMessage, TResource, TResponse>>? logger = null)` | Builds the resource-loading authorization behavior. `options` defaults to a fresh `ResourceAuthorizationOptions` with `DefaultExposurePolicy = Propagate` (back-compat for consumers that have not opted any resource into hide-as-NotFound). `logger` defaults to `NullLogger.Instance`. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Resolves the actor from `IActorProvider` first (returns `Error.AuthenticationRequired` when no actor is available — fail fast before doing any I/O; provider-side `InvalidOperationException` still propagates as a deployment bug). Then resolves `IResourceLoader<TMessage, TResource>` from the current scope, returns loader failures directly, and finally calls `message.Authorize(actor, resource)` before invoking the handler. After `Authorize` succeeds the loaded resource is published via the per-async-flow accessor backing `IAuthorizedResource<TMessage, TResource>` — a linked-frame design with a volatile `IsActive` flag whose dispose (after `next` returns) flips `IsActive` and restores the parent frame, so handlers can read the same instance and avoid a duplicate load while orphan tasks that outlive the dispatch cannot observe the resource. See [Recipe 31](trellis-api-cookbook.md#recipe-31--avoid-duplicate-load-with-iauthorizedresourcetcommand-tresource). **Failure-exposure policy.** When `ResourceAuthorizationOptions` opts `TResource` into `AuthFailureExposurePolicy.HideAsNotFound`, both load-failure and authorize-failure `Error.Forbidden` / `Error.AuthenticationRequired` are translated to `new Error.NotFound(ResourceRef)` where the resource type comes from the configured public type (defaults to `TResource`) and the id is extracted via reflection on `IIdentifyResource<TResource, TId>` (or the public type for the projection overload). Other error kinds pass through unchanged. Translation emits a `[LoggerMessage]` event `ExistenceHidden` (`EventId = 1`, `Level = Information`) carrying the original `Kind` and `Code`. See [Recipe 32](trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound). **Null-payload defense.** A loader that violates its `Result<T>` contract by returning `Result.Ok(null)` is treated as fail-closed: the behavior short-circuits to `Error.Forbidden` with code `resource.authorization.null-payload` rather than letting a downstream `NullReferenceException` from `message.Authorize` bubble as a 500. Under `HideAsNotFound` that synthetic Forbidden is also translated. This behavior is only active when registered explicitly or via `AddResourceAuthorization(...)`; it is not included in `AddTrellisBehaviors()` or `PipelineBehaviors`. |

### AuthFailureExposurePolicy
**Declaration**

```csharp
public enum AuthFailureExposurePolicy { Propagate = 0, HideAsNotFound = 1 }
```

| Member | Description |
| --- | --- |
| `Propagate` | Default. `Error.Forbidden` and `Error.AuthenticationRequired` flow through verbatim. |
| `HideAsNotFound` | `Error.Forbidden` and `Error.AuthenticationRequired` are translated to `new Error.NotFound(ResourceRef)` so unauthorized actors cannot distinguish "resource does not exist" from "resource exists but you may not access it." Only those two error kinds translate — other errors pass through. |

### ResourceAuthorizationOptions
**Declaration**

```csharp
public sealed class ResourceAuthorizationOptions
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public ResourceAuthorizationOptions()` | Constructs the options bag with `DefaultExposurePolicy = AuthFailureExposurePolicy.Propagate` and no per-resource overrides. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `DefaultExposurePolicy` | `AuthFailureExposurePolicy` | Policy applied to resources that have no per-resource override. Defaults to `Propagate`. Set to `HideAsNotFound` to flip the default for an entire service; use `Propagate<TResource>()` to mark individual safe-to-disclose resources. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public ResourceAuthorizationOptions HideExistence<TResource>() where TResource : class` | `ResourceAuthorizationOptions` | Opt `TResource` into `HideAsNotFound`. Synthetic `NotFound.ResourceRef.Type` uses the simple name of `TResource` with backtick mangling stripped via `ResourceRef.FormatTypeName`. ID extraction uses `IIdentifyResource<TResource, TId>` on the message (returns `ResourceRef` without an id when the message does not implement it). Returns `this` for chaining. |
| `public ResourceAuthorizationOptions HideExistence<TAuthorizationResource, TPublicResource>() where TAuthorizationResource : class` | `ResourceAuthorizationOptions` | Projection-loader overload. Use when the loader returns an internal authorization-only projection (`TAuthorizationResource`) and the wire-public type is different (`TPublicResource`). The synthetic `NotFound.ResourceRef.Type` is the public type name. ID extraction tries `IIdentifyResource<TPublicResource, TId>` first, then falls back to `IIdentifyResource<TAuthorizationResource, TId>`. |
| `public ResourceAuthorizationOptions Propagate<TResource>() where TResource : class` | `ResourceAuthorizationOptions` | Explicitly opt `TResource` into `Propagate`. Useful for overriding a non-default `DefaultExposurePolicy`. |

**Behavioral notes**

- **Translation scope is narrow by design.** Only `Error.Forbidden` and `Error.AuthenticationRequired` translate. `Error.Unexpected`, `Error.Unavailable`, `Error.NotFound` from the loader, and transport faults pass through verbatim — hiding transient infrastructure failures behind 404 would destroy operational signal. The pass-through guarantee applies to the leaf loader's direct return value only; for the via path, intermediate / owner hop failures are already collapsed to a synthetic `Forbidden("resource.authorization-via.load-failed")` by the v1 multi-hop security model (existence-leak protection on related resources) BEFORE exposure translation runs, so under `HideAsNotFound` an underlying `Unavailable` from a downstream owner service surfaces as `404` to the consumer. Consumers needing finer-grained downstream-failure visibility on the related-resource graph should use the direct `IAuthorizeResource<TResource>` model instead of the via fan-out shape.
- **Via commands key on `TLeaf`.** `HideExistence<Match>()` covers commands implementing `IAuthorizeResourceVia<Team>` + `IIdentifyResource<Match, MatchId>`; the synthetic `NotFound` references `Match`, never `Team`. Opting `Team` (the authorization implementation detail) is a no-op.
- **`AuthorizationBehavior` short-circuits earlier.** Commands implementing both `IAuthorize` and `IAuthorizeResource<T>` have their static-permission failures emitted by `AuthorizationBehavior` before resource authorization runs. Those failures are NOT translated. Commands needing full existence-hiding must omit `IAuthorize`.
- **Cache safety.** Hidden 404s look identical to real 404s on the wire — a shared cache will misdirect responses across actors. Mark protected endpoints with `Cache-Control: no-store` or `private`.

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
| `public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services)` | `IServiceCollection` | Registers the five open generic behaviors listed in `PipelineBehaviors` and a default `TrellisMediatorTelemetryOptions` singleton (Detail redacted). **Idempotent** — uses `TryAddEnumerable`/`TryAddSingleton` so repeat calls (e.g. from plug-in extensions like `AddTrellisFluentValidation` from `Trellis.Mediator.FluentValidation`, `AddTrellisAsp`) do not duplicate registrations. |
| `public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services, Action<TrellisMediatorTelemetryOptions> configure)` | `IServiceCollection` | Same as the parameterless overload, but applies `configure` to the registered `TrellisMediatorTelemetryOptions` singleton. Replaces any prior options registration so this call wins regardless of ordering. |
| `public static IServiceCollection AddResourceAuthorization<TMessage, TResource, TResponse>(this IServiceCollection services) where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage where TResource : class where TResponse : IResult, IFailureFactory<TResponse>` | `IServiceCollection` | Registers `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` and inserts it immediately before `ValidationBehavior<,>` when validation is already registered. Also registers `IAuthorizedResource<TMessage, TResource>` as scoped (backed by `AuthorizedResourceHolder<,>`) so handlers can inject the v4 typed accessor; see [Recipe 31](trellis-api-cookbook.md#recipe-31--avoid-duplicate-load-with-iauthorizedresourcetcommand-tresource). **Idempotent** for the same closed service type + implementation type; different response types or via behaviors remain distinct. **Throws `InvalidOperationException`** when `TMessage` also implements `IAuthorizeResourceVia<TOwner>` (dual-mode commands are rejected at every entry point — security primitives are never silently composed). |
| `[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")] [RequiresDynamicCode("Constructs closed generic types at runtime. Use explicit registration for AOT scenarios.")] public static IServiceCollection AddResourceAuthorization(this IServiceCollection services, params Assembly[] assemblies)` | `IServiceCollection` | Scans assemblies for `IAuthorizeResource<TResource>` AND `IAuthorizeResourceVia<TOwner>` implementations, resolves `TResponse` from `ICommand<T>`, `IQuery<T>`, or `IRequest<T>`, registers closed `ResourceAuthorizationBehavior<,,>` / `ResourceAuthorizationViaBehavior<,,,>` instances, registers discovered `IResourceLoader<,>` and `SharedResourceLoaderById<,>` implementations, and bridges `IIdentifyResource<TResource, TId>` messages to shared loaders. Also auto-registers `IAuthorizedResource<TMessage, TResource>` (for direct commands) and `IAuthorizedResource<TMessage, TLeaf>` (for via commands — leaf only, owner accessor not in v4) so handlers can inject the v4 typed accessor. Closed behavior registration is idempotent across repeated scans and explicit-plus-scanned overlap when service type + implementation type match. For `IAuthorizeResourceVia<TOwner>` commands the scanner runs `ResourceAuthorizationPathResolver.Resolve(...)` over every scanned entity type and registers the closed `ResolvedAuthorizationPathHolder<,,,>` so the behavior receives its path via DI. **Throws `InvalidOperationException` at startup** when (a) any message's `TResponse` does not implement both `IResult` and `IFailureFactory<TResponse>` (security-marker fail-fast), (b) any message implements both `IAuthorizeResource<T>` and `IAuthorizeResourceVia<TOwner>` (security primitives are never silently composed), (c) any `IAuthorizeResourceVia<TOwner>` command does not also implement `IIdentifyResource<TLeaf, TLeafId>` (silent skip would leave the via-marker unprotected at runtime), (d) the path resolver finds zero or multiple distinct simple paths from leaf to owner, or (e) any discovered resource type or via-leaf type is a value type (the v4 accessor closed generics require `where TResource : class` / `where TLeaf : class`; the friendly diagnostic names the offending command and resource type). |
| `[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")] public static IServiceCollection AddResourceLoaders(this IServiceCollection services, Assembly assembly)` | `IServiceCollection` | Registers discovered `IResourceLoader<,>` implementations with `TryAddScoped`. Throws `ArgumentNullException` when `services` or `assembly` is null. |
| `public static IServiceCollection AddSharedResourceLoader<TMessage, TResource, TId>(this IServiceCollection services) where TMessage : IIdentifyResource<TResource, TId>` | `IServiceCollection` | Registers `SharedResourceLoaderAdapter<TMessage, TResource, TId>` as `IResourceLoader<TMessage, TResource>`. Constraint loosened from also requiring `IAuthorizeResource<TResource>` so via-commands (which use `IAuthorizeResourceVia<TOwner>` instead) can reuse the same bridging. There is no `AddSharedResourceLoaderById` helper; register `SharedResourceLoaderById<TResource,TId>` in DI separately. |
| `public static IServiceCollection AddRelatedResourceAuthorization<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TMessage, TLeaf, TLeafId, TOwner, TOwnerId, TResponse>(this IServiceCollection services, Func<TLeaf, TOwnerId?> extractOwnerId) where TMessage : IAuthorizeResourceVia<TOwner>, IIdentifyResource<TLeaf, TLeafId>, global::Mediator.IMessage where TLeaf : class where TOwner : class where TOwnerId : notnull where TResponse : IResult, IFailureFactory<TResponse>` | `IServiceCollection` | Explicit single-hop registration for AOT / non-scanning consumers. Builds a `ResolvedAuthorizationPath` with one hop using `extractOwnerId` to extract the owner id from the loaded leaf, then registers `ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>` as a typed descriptor and `ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse>` as a singleton. Throws `ArgumentNullException` when `services` or `extractOwnerId` is null. Throws `InvalidOperationException` if `TMessage` also implements `IAuthorizeResource<T>` (dual-mode security primitives are never silently composed). The hop loader throws `InvalidOperationException` at request time if `SharedResourceLoaderById<TOwner, TOwnerId>` is not registered (deployment bug, not authorization denial). |
| `public static IServiceCollection AddRelatedResourceAuthorization<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TMessage, TLeaf, TOwner, TResponse>(this IServiceCollection services, ResolvedAuthorizationPath path) where TMessage : IAuthorizeResourceVia<TOwner>, global::Mediator.IMessage where TLeaf : class where TResponse : IResult, IFailureFactory<TResponse>` | `IServiceCollection` | Explicit registration accepting a hand-built `ResolvedAuthorizationPath` for shapes the single-hop overload cannot express (chains, plural-terminal fan-out, custom extractors). Also registers `IAuthorizedResource<TMessage, TLeaf>` as scoped so handlers can inject the v4 typed accessor for the leaf (the typical mutation target); the owner accessor is intentionally not in v4. Throws `ArgumentNullException` when `services` or `path` is null. Same dual-mode rejection as the single-hop overload. |
| `public static IServiceCollection AddResourceAuthorization(this IServiceCollection services, Action<ResourceAuthorizationOptions> configure)` | `IServiceCollection` | Configures the per-resource failure-exposure policy via `ResourceAuthorizationOptions`. Repeated calls compose configure delegates rather than overwriting. Always-on side-effect: registers `IOptions<ResourceAuthorizationOptions>` (also added by every other `AddResourceAuthorization` / `AddRelatedResourceAuthorization` overload — behaviors can therefore always resolve options regardless of registration order). Throws `ArgumentNullException` when `services` or `configure` is null. See [Recipe 32](trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound). |

### TracingBehavior<TMessage, TResponse>
**Declaration**

```csharp
public sealed class TracingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse> where TMessage : global::Mediator.IMessage where TResponse : IResult
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public TracingBehavior(TrellisMediatorTelemetryOptions? options = null)` | Builds the tracing behavior. `options` is resolved from DI; under `AddTrellisBehaviors()` the `TrellisMediatorTelemetryOptions` singleton is always registered, so this argument is non-null in production. The optional-null fallback exists only for consumers that instantiate the behavior outside of DI (custom test fixtures); when null, the safe-by-default options are used and `Error.Detail` is redacted from `Activity.StatusDescription`. |

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
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Starts an activity named after `TMessage`. On failed results, tags the activity with `error.code` (the stable `Error.Code`) and `error.type` (the stable error class name); sets `ActivityStatusCode.Error`. The `StatusDescription` is left empty by default — the free-text `Error.Detail` is included only when `TrellisMediatorTelemetryOptions.IncludeErrorDetail` is `true`. On success sets `ActivityStatusCode.Ok`. Rethrows consumer-initiated `OperationCanceledException` when the request `cancellationToken` is canceled and the exception carries that token, after recording the OpenTelemetry exception event and tagging `otel.status_description` = `canceled`; the activity status remains `Unset`. Other thrown exceptions are marked `ActivityStatusCode.Error`, tagged with `error.type`, recorded as an OpenTelemetry exception event (`exception.type`, `exception.message`, `exception.stacktrace`), and rethrown; the exception message is **not** copied into `Activity.StatusDescription`. |

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

Extensibility hook for the unified validation stage. Implementations are resolved from DI as `IEnumerable<IMessageValidator<TMessage>>` by `ValidationBehavior<TMessage, TResponse>`; every registered validator runs before the handler. External packages (e.g., `Trellis.Mediator.FluentValidation`) plug additional validation sources into the pipeline through this interface without taking a dependency on a specific validation library or message-side interface from `Trellis.Mediator`.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `ValueTask<IResult> ValidateAsync(TMessage message, CancellationToken cancellationToken)` | `ValueTask<IResult>` | Returns `Result.Ok()` on success, or `Result.Fail(new Error.InvalidInput(...))` with field/rule violations on failure. `Error.InvalidInput` failures from every validator (and `IValidate.Validate()` if implemented) are aggregated into a single response failure by `ValidationBehavior`. Returning a non-`Error.InvalidInput` failure (e.g., `Error.Conflict`, `Error.Forbidden`) is allowed but short-circuits the stage immediately and is propagated as-is. |

### ValidationBehavior<TMessage, TResponse>
**Declaration**

```csharp
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IMessageValidator<TMessage>> validators) : IPipelineBehavior<TMessage, TResponse> where TMessage : global::Mediator.IMessage where TResponse : IResult, IFailureFactory<TResponse>
```

Unified validation stage. Runs `IValidate.Validate()` (when the message implements `IValidate`) and every `IMessageValidator<TMessage>` registered in DI for the message, then aggregates `Error.InvalidInput` failures into a single response. The behavior is registered for **all** messages — when the message does not implement `IValidate` and no validators are registered it is a no-op pass-through.

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
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Aggregation rules: (1) Multiple `Error.InvalidInput` failures from `IValidate` and validators are merged into a single `Error.InvalidInput` whose `Fields` and `Rules` collect every reported violation. (2) An `Error.InvalidInput` with empty `Fields` AND empty `Rules` still short-circuits the handler — original failure semantics are preserved. (3) A non-`Error.InvalidInput` failure returned by any source short-circuits the stage immediately and is propagated as-is. |

### IDomainEventHandler
**Declaration**

```csharp
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
```

Handles a domain event raised by an `IAggregate`. Implementations are resolved via DI by [`DomainEventDispatchBehavior`](#domaineventdispatchbehavior) after a successful command and invoked once per matching event in the dispatch snapshot.

Dispatch matches the runtime type of the event **exactly**; base-type and interface-type handlers are not resolved automatically. Handlers must be **idempotent** and **side-effect-only**: send email, publish to a bus, update a projection, or enqueue external work, but do not mutate aggregates or raise more domain events from inside the dispatch loop. If a handler raises a new event on the same aggregate, or mutates another aggregate participating in tracked dispatch, post-dispatch validation throws [`DomainEventHandlerCascadedException`](#domaineventhandlercascadedexception). To perform more domain mutation, issue a follow-up Mediator command from the application layer after the originating command completes, or queue post-commit work that runs as a separate top-level command.

Non-cancellation exceptions thrown by a handler are logged at error level and swallowed by the default publisher so that other handlers, other events, and the originating command still complete. `OperationCanceledException` propagates when the publisher's supplied cancellation token is canceled, so the request can abort. Cascade detection does not make handler-side failures durable; durable at-least-once side effects require an outbox pattern (planned for a future release, not shipped today).

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
| `ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)` | `ValueTask` | Publishes to all matching handlers. Resolution uses `domainEvent.GetType()`. Non-cancellation handler exceptions are logged and swallowed; `OperationCanceledException` propagates when the supplied token is canceled so the caller can abort. Default implementation (`MediatorDomainEventPublisher`) is `internal` and registered by `AddDomainEventDispatch()`. |

### DomainEventHandlerCascadedException
**Declaration**

```csharp
public sealed class DomainEventHandlerCascadedException : InvalidOperationException
```

Thrown when domain-event dispatch detects that a handler cascaded new domain events during a strict single-wave snapshot dispatch. The exception is raised after the original snapshot has been published but before `AcceptChanges()` clears the aggregate event queues, so operators can inspect the still-uncommitted events.

**Constructors**

| Signature | Description |
| --- | --- |
| `public DomainEventHandlerCascadedException()` | Standard exception constructor. `Offenders` is empty. |
| `public DomainEventHandlerCascadedException(string message)` | Standard exception constructor with a custom message. `Offenders` is empty. |
| `public DomainEventHandlerCascadedException(string message, Exception innerException)` | Standard exception constructor with an inner exception. `Offenders` is empty. |
| `public DomainEventHandlerCascadedException(IReadOnlyList<CascadeOffender> offenders)` | Builds the exception from all offending aggregates. The supplied list is copied. |
| `public DomainEventHandlerCascadedException(Type aggregateType, IReadOnlyList<string> cascadedEventTypeNames)` | Convenience overload for a single offending aggregate. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `Offenders` | `IReadOnlyList<CascadeOffender>` | Aggregates whose post-dispatch event queue changed from the entry snapshot. The tracked-aggregate behavior can report multiple offenders from one dispatch pass. |

### CascadeOffender
**Declaration**

```csharp
public readonly record struct CascadeOffender(Type AggregateType, IReadOnlyList<string> CascadedEventTypeNames);
```

Identifies one aggregate type whose pending-event list changed during dispatch.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `AggregateType` | `Type` | The offending aggregate type. |
| `CascadedEventTypeNames` | `IReadOnlyList<string>` | The event type names still present from the first changed position after the entry snapshot was published. |

### DomainEventDispatchBehavior
**Declaration**

```csharp
public sealed partial class DomainEventDispatchBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult
```

Pipeline behavior that dispatches domain events accumulated on the success-value aggregate after the command handler returns. Constrained to `ICommand<TResponse>` so queries returning aggregate types do not trigger dispatch. Dispatch only runs when the response is a successful `IResult<TAggregate>` (typically `Result<TAggregate>`) where `TAggregate` implements `IAggregate`; other response shapes (`Result<Unit>`, `Result<TDto>`, `Result<(A,B)>`) pass through untouched.

> **Persist-on-failure outcomes.** A `Result.FailAfterCommit<TAggregate>(error)` outcome is **still a failure** — `IsFailure` is `true` — so dispatch is skipped exactly as for any other failure. Any events the handler raised on aggregates remain on those in-memory instances and are discarded with the request scope; they are not a durable retry buffer. If the persist-on-failure scenario needs to drive downstream side effects, model them explicitly via an outbox row or a dedicated follow-up command instead of relying on event re-dispatch. The commit (when `TransactionalCommandBehavior` is also registered) happens upstream and is independent of dispatch.

When `TransactionalCommandBehavior` is also registered, dispatch fires after the transaction commits — handlers see committed state. On a successful `IResult<TAggregate>` response, the behavior snapshots `aggregate.UncommittedEvents()` once at dispatch entry and publishes **only that snapshot** sequentially. Handler-raised events are never picked up by a later loop. After the snapshot has been published, the behavior validates the aggregate's event queue: the post-dispatch list must still equal the entry snapshot by both length and per-position reference equality. Any handler that raised new events, cleared the list via `AcceptChanges`, replaced events, or reordered them trips the validation and throws [`DomainEventHandlerCascadedException`](#domaineventhandlercascadedexception). `IChangeTracking.AcceptChanges()` is called only after validation proves the dispatch was clean.

On cascade or cancellation, `AcceptChanges()` is not called. The aggregate retains the original events and any cascaded events so operators can inspect the in-memory state. Mid-dispatch cancellation still propagates immediately; handlers that already ran are not rolled back, so handlers must remain idempotent for at-least-once retry. Non-cancellation handler exceptions are still swallowed by the default `MediatorDomainEventPublisher`; cascade detection is about handler-caused mutations of the aggregate's pending-event list, not handler-side failures.

> [!WARNING]
> **Post-commit throw caveat.** With `TransactionalCommandBehavior` registered, the database commit is already durable before domain-event dispatch starts. If cascade detection throws, the caller receives a failure-shaped response (via the outer exception behavior in the standard pipeline) even though the write committed; a retry may therefore hit normal "already committed" semantics. Durable at-least-once delivery requires an outbox pattern — planned for a future release, not shipped today.

**Constructors**

| Signature | Description |
| --- | --- |
| `public DomainEventDispatchBehavior(IDomainEventPublisher publisher, ILogger<DomainEventDispatchBehavior<TMessage, TResponse>> logger)` | Resolves the publisher used to fan out events to registered handlers. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Awaits `next`, returns immediately on failure or when the response is not an `IResult<TAggregate>` (typically `Result<TAggregate>`). On success, snapshots `aggregate.UncommittedEvents()`, publishes only that snapshot, validates the post-dispatch event list still matches the snapshot by length AND per-position reference equality, and calls `AcceptChanges()` only on clean dispatch. Throws `DomainEventHandlerCascadedException` on any mismatch (raises, clears, replaces, reorders); cancellation propagates before `AcceptChanges()` so undispatched events remain on the aggregate. |

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
| `[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use AddDomainEventHandler<TEvent, THandler> for AOT/trim scenarios.")] [RequiresDynamicCode("Constructs closed generic IDomainEventHandler<TEvent> at runtime.")] public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services, params Assembly[] assemblies)` | `IServiceCollection` | Scans the assemblies for concrete `IDomainEventHandler<TEvent>` implementations and registers each as scoped. A type implementing handlers for multiple event types is registered once per interface. Also wires the dispatch behavior + publisher (idempotent). Throws `ArgumentNullException` when `services` or `assemblies` is null and `ArgumentException` when the array is empty or contains null. |

### TrackedAggregateDomainEventDispatchBehavior
**Declaration**

```csharp
public sealed partial class TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>
    : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::Mediator.ICommand<TResponse>
    where TResponse : IResult
```

Opt-in alternative to [`DomainEventDispatchBehavior<,>`](#domaineventdispatchbehavior). Instead of extracting an aggregate from the response, it reads the unit-of-work's [`ITrackedAggregateSource`](trellis-api-core.md#itrackedaggregatesource) and dispatches events from every aggregate that participated in the most recent successful commit. Use this when handlers return outcome DTOs (`Result<DoneDto>`, `Result<Unit>`, `Result<(A, B)>`) but mutate aggregates via the EF change tracker.

The tracked behavior snapshots the committed aggregate set and each aggregate's `UncommittedEvents()` at dispatch entry, publishes only those snapshots, then validates every snapshot aggregate before clearing anything. If dispatching aggregate A's events causes a handler to append events to aggregate B that was also in the snapshot, aggregate B is reported as a cascade offender too; the thrown [`DomainEventHandlerCascadedException`](#domaineventhandlercascadedexception) lists every offending aggregate. `AcceptChanges()` is called on the snapshot aggregates only after the entire pass validates cleanly. The `TrackedAggregateDispatchReentrancyGuard` skips nested tracked dispatch; Mediator commands sent from inside a domain-event handler can therefore leave their own aggregate events stranded. Queue follow-up commands outside the handler instead.

**Constructors**

| Signature | Description |
| --- | --- |
| `public TrackedAggregateDomainEventDispatchBehavior(ITrackedAggregateSource trackedAggregateSource, IDomainEventPublisher publisher, ILogger<TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>> logger)` | Resolves the unit-of-work sidecar, publisher, and logger. Throws `ArgumentNullException` when any argument is null. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Awaits `next`. Skips dispatch on `IsFailure` (including `Result.FailAfterCommit<T>(...)`), when re-entered (`AsyncLocal` guard shared across all closed-generic instantiations), and when `ITrackedAggregateSource.CommittedAggregates` is empty. Otherwise snapshots every committed aggregate's events, publishes only those snapshots, validates that no snapshot aggregate accumulated additional events, and calls `IChangeTracking.AcceptChanges()` on every snapshot aggregate only after clean validation. Throws `DomainEventHandlerCascadedException` with all offending aggregates on same-aggregate or cross-aggregate cascade. Cancellation propagates above `AcceptChanges()` so undispatched events remain on every snapshot aggregate. |

### TrackedAggregateDomainEventDispatchServiceCollectionExtensions
**Declaration**

```csharp
public static class TrackedAggregateDomainEventDispatchServiceCollectionExtensions
```

DI registration helper for the tracked dispatch behavior.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrackedAggregateDomainEventDispatch(this IServiceCollection services)` | `IServiceCollection` | Registers `TrackedAggregateDomainEventDispatchBehavior<,>` (open generic, scoped) and the default `IDomainEventPublisher`. Removes any prior `DomainEventDispatchBehavior<,>` registration (mutually exclusive). Yanks any prior `TransactionalCommandBehavior<,>` registration and re-appends it last so tracked dispatch sits just outside the transaction behavior and runs after commit. **Idempotent**. Subsequent `AddDomainEventDispatch()` / `AddDomainEventHandler<,>()` calls register handlers only — they do not reintroduce the response-shape behavior. |


## Extension methods

### Trellis.Mediator.ServiceCollectionExtensions

```csharp
public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services)
public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services, Action<TrellisMediatorTelemetryOptions> configure)
public static IServiceCollection AddResourceAuthorization<TMessage, TResource, TResponse>(this IServiceCollection services) where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage where TResource : class where TResponse : IResult, IFailureFactory<TResponse>
[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")]
[RequiresDynamicCode("Constructs closed generic types at runtime. Use explicit registration for AOT scenarios.")]
public static IServiceCollection AddResourceAuthorization(this IServiceCollection services, params Assembly[] assemblies)
[RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")]
public static IServiceCollection AddResourceLoaders(this IServiceCollection services, Assembly assembly)
public static IServiceCollection AddSharedResourceLoader<TMessage, TResource, TId>(this IServiceCollection services) where TMessage : IIdentifyResource<TResource, TId>
public static IServiceCollection AddRelatedResourceAuthorization<TMessage, TLeaf, TLeafId, TOwner, TOwnerId, TResponse>(this IServiceCollection services, Func<TLeaf, TOwnerId?> extractOwnerId)
    where TMessage : IAuthorizeResourceVia<TOwner>, IIdentifyResource<TLeaf, TLeafId>, global::Mediator.IMessage
    where TLeaf : class
    where TOwner : class
    where TOwnerId : notnull
    where TResponse : IResult, IFailureFactory<TResponse>
public static IServiceCollection AddRelatedResourceAuthorization<TMessage, TLeaf, TOwner, TResponse>(this IServiceCollection services, ResolvedAuthorizationPath path)
    where TMessage : IAuthorizeResourceVia<TOwner>, global::Mediator.IMessage
    where TLeaf : class
    where TResponse : IResult, IFailureFactory<TResponse>
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

### Trellis.Mediator.DomainEventPublisherExtensions

```csharp
public static Task DispatchAggregateEventsAsync(
    this IDomainEventPublisher publisher,
    IAggregate aggregate,
    CancellationToken cancellationToken = default)
```

**POST-COMMIT ONLY.** Uses the same strict snapshot contract as [`DomainEventDispatchBehavior<,>`](#domaineventdispatchbehavior) for handlers whose `TResponse` is not an `IResult<TAggregate>` shape (`Result<Unit>`, `Result<TDto>`, `Result<(A,B)>`) and for non-Mediator call sites such as `BackgroundService` workers, and returns after the aggregate's current event snapshot has been published and accepted. See *Behavioral notes: DispatchAggregateEventsAsync* below.

### Behavioral notes: DispatchAggregateEventsAsync

- **Snapshot + cascade semantics:** Snapshots `aggregate.UncommittedEvents()` once, publishes only that snapshot sequentially, validates that no handler appended new events, and calls `IChangeTracking.AcceptChanges()` only after clean validation. Throws [`DomainEventHandlerCascadedException`](#domaineventhandlercascadedexception) on cascade (`AcceptChanges()` is not called — original and cascaded events remain on the aggregate).
- **Cancellation:** Cancellation propagates above `AcceptChanges()` so undispatched events remain on the aggregate; dispatched events stay dispatched and handlers must be idempotent for retry.
- **Handler exceptions (publisher contract):** Handler exceptions follow the publisher's contract: the default `MediatorDomainEventPublisher` logs and swallows non-cancellation handler exceptions so the helper continues; a custom publisher that propagates handler exceptions causes the helper to rethrow without calling `AcceptChanges()`.
- **When to call:** **Must only be called after the underlying unit of work has committed** — calling it inside a handler that relies on `TransactionalCommandBehavior` for its commit publishes events before the database transaction is durable. Durable at-least-once dispatch requires an outbox pattern, planned for a future release.
- **Cross-doc link:** See [Dispatching events from non-aggregate response shapes](../articles/integration-mediator.md#dispatching-events-from-non-aggregate-response-shapes-post-commit-safe) for the integration article.

### Trellis.Mediator.TrackedAggregateDomainEventDispatchServiceCollectionExtensions

```csharp
public static IServiceCollection AddTrackedAggregateDomainEventDispatch(this IServiceCollection services)
```

Registers the tracked-aggregate pipeline behavior + default publisher. Mutually exclusive with `AddDomainEventDispatch()`: removes any prior response-shape `DomainEventDispatchBehavior<,>` registration so calling both no longer double-dispatches. Re-orders any prior `Trellis.EntityFrameworkCore.TransactionalCommandBehavior<,>` registration so tracked dispatch sits just outside the transaction behavior and runs after commit (events are read from the unit-of-work's snapshot, taken at commit time). Idempotent. See [`TrackedAggregateDomainEventDispatchBehavior`](#trackedaggregatedomaineventdispatchbehavior) for the behavior contract and [Auto-dispatching from outcome-DTO commands](../articles/integration-mediator.md#auto-dispatching-from-outcome-dto-commands-opt-in-tracked-behavior) for the integration article.

## Interfaces

```csharp
public interface IValidate
public interface IMessageValidator<in TMessage> where TMessage : global::Mediator.IMessage
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
public interface IDomainEventPublisher
```

## Behavioral notes

### Canonical pipeline order

The Trellis pipeline executes outermost → innermost in this order. `AddTrellisBehaviors()` registers slots 1-4 and 6; resource authorization, domain-event dispatch, tracked dispatch, and EF transactions are opt-in registrations that are inserted into the canonical slots shown below.

1. **`ExceptionBehavior<,>`** — catches unhandled exceptions (except `OperationCanceledException`), logs them with a per-incident fault id, and converts them to a typed `TResponse.CreateFailure(new Error.Unexpected("unhandled_exception", faultId) { Detail = "An unexpected error occurred while processing the request." })`. Sits outermost so every other layer is wrapped.
2. **`TracingBehavior<,>`** — opens an OpenTelemetry `Activity` per message under the `"Trellis.Mediator"` activity source. On failed results, tags `error.code` / `error.type` and sets `ActivityStatusCode.Error`; non-cancellation thrown exceptions do the same and also add the standard exception event (`exception.type`, `exception.message`, `exception.stacktrace`). Consumer-initiated cancellations (the thrown exception carries the canceled request token) record the exception event, tag `otel.status_description` = `canceled`, and leave the status `Unset`. `Error.Detail` is redacted from `StatusDescription` unless `TrellisMediatorTelemetryOptions.IncludeErrorDetail` is `true`.
3. **`LoggingBehavior<,>`** — structured logging with start/end and elapsed-ms entries; emits the stable `Error.Code` on failure. Inherits the same correlation context propagated by the surrounding `Activity`. `Error.Detail` is redacted unless `IncludeErrorDetail` is `true`.
4. **`AuthorizationBehavior<,>`** — runs for `IAuthorize` messages; resolves the actor, returns `Error.AuthenticationRequired` when no actor is available, and rejects with `new Error.Forbidden("authorization.insufficient.permissions")` when `RequiredPermissions` are not satisfied.
5. **`ResourceAuthorizationBehavior<,,>`** *(opt-in via `AddResourceAuthorization(...)`)* — runs for `IAuthorizeResource<TResource>` messages. Inserted **immediately before `ValidationBehavior<,>`** so a 403 short-circuits before a 422 is computed; duplicate closed behavior registrations are ignored so the same behavior does not run twice per request. Resolves the actor before loader construction or resource I/O, returns `Error.AuthenticationRequired` when no actor is available, then loads the resource via `IResourceLoader<TMessage, TResource>` and calls `message.Authorize(actor, resource)`.
6. **`ValidationBehavior<,>`** — unified validation stage. Runs `IValidate.Validate()` if implemented, then every `IMessageValidator<TMessage>` resolved from DI; aggregates all `Error.InvalidInput` failures into a single response. External validation sources (e.g., the `Trellis.Mediator.FluentValidation` adapter) participate here without occupying their own pipeline slot.
7. **`DomainEventDispatchBehavior<,>`** *(opt-in via `AddDomainEventDispatch(...)`)* — runs for `ICommand<TResponse>` messages where `TResponse` is `IResult<TAggregate>` (typically `Result<TAggregate>`) and `TAggregate : IAggregate`. After the inner pipeline returns success, snapshots `aggregate.UncommittedEvents()`, dispatches only that snapshot to registered `IDomainEventHandler<TEvent>` instances, validates that handlers did not append events, and calls `AcceptChanges()` only on clean dispatch. When `TransactionalCommandBehavior` is registered innermost, dispatch fires after the transaction commits — handlers see committed state, and a later cascade exception cannot roll back the database write. Non-cancellation handler exceptions are logged and swallowed; `OperationCanceledException` propagates when the request token is canceled and skips `AcceptChanges()`. **Mutually exclusive** with `TrackedAggregateDomainEventDispatchBehavior<,>`: `AddTrackedAggregateDomainEventDispatch()` removes any prior response-shape registration, and later `AddDomainEventDispatch()` / `AddDomainEventHandler<,>()` calls do not reintroduce it while tracked dispatch is present.
   - **Or** `TrackedAggregateDomainEventDispatchBehavior<,>` *(opt-in via `AddTrackedAggregateDomainEventDispatch(...)`)* — sits at the same slot but reads the aggregates from `ITrackedAggregateSource.CommittedAggregates` (populated by the unit-of-work at commit time), snapshots each aggregate's events, and throws `DomainEventHandlerCascadedException` when same-aggregate or cross-aggregate cascade is detected. Fires for any response shape, including outcome DTOs. See [`TrackedAggregateDomainEventDispatchBehavior`](#trackedaggregatedomaineventdispatchbehavior).
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

public sealed record Order(OrderId Id, ActorId OwnerId);

public sealed record GetOrderQuery(OrderId Id)
    : IQuery<Result<Order>>, IAuthorize, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>, IValidate
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:read"];

    public OrderId GetResourceId() => Id;

    public IResult Validate() => Result.Ok();

    public IResult Authorize(Actor actor, Order resource) =>
        resource.OwnerId == actor.Id
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
    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Maybe.From(Actor.Create("user-1", new HashSet<string> { "orders:read" })));
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

- [trellis-api-authorization.md](trellis-api-authorization.md#patterns-index)
- [trellis-api-core.md](trellis-api-core.md#patterns-index)
- [trellis-api-asp.md](trellis-api-asp.md#patterns-index)
