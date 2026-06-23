---
package: Trellis.Authorization
namespaces: [Trellis.Authorization]
types: [Actor, ActorAttributes, ActorId, IActorProvider, IAuthorize, "IAuthorizeResource<TResource>", "IAuthorizeResourceVia<TOwner>", "IIdentifyResource<TResource,TId>", "IIdentifyRelatedResource<TRelated,TId>", "IIdentifyRelatedResources<TRelated,TId>", "IResourceLoader<TMessage,TResource>", "ResourceLoaderById<TMessage,TResource,TId>", "SharedResourceLoaderById<TResource,TId>"]
version: v3
last_verified: 2026-06-21
audience: [llm]
---
# Trellis.Authorization — API Reference

**Package:** `Trellis.Authorization`
**Namespace:** `Trellis.Authorization`
**Purpose:** Domain-layer authorization primitives — actor identity / permission / attribute model and the contracts used by the mediator's authorization behavior to perform static (permission) and resource-based authorization. This package contains no ASP.NET Core dependencies; the `IActorProvider` implementations and DI helpers ship in `Trellis.Asp` (see [`trellis-api-asp.md`](trellis-api-asp.md#namespace-trellisaspauthorization), namespace `Trellis.Asp.Authorization`).

> [!TIP]
> For the end-to-end mental model — JWT → JwtBearer → `IActorProvider` → mediator behaviors → 401/403 — plus decision trees and Mermaid diagrams, read [Mental model](../articles/integration-asp-authorization.md#mental-model) in the integration article first. This file is the type-by-type reference.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth) — recipes using this package.

## Use this file when

- You are modeling actors, permissions, forbidden permissions, or actor attributes without ASP.NET dependencies.
- You are implementing static permission authorization through `IAuthorize`.
- You are implementing resource-based authorization through `IAuthorizeResource<TResource>` and want the canonical guard shape.

## Owner check quick-start — copy this

The 90% case is "the command's resource id is loaded, and the actor must be the owner (or hold an admin permission)." For that shape, implement two interfaces — `IAuthorizeResource<TResource>` for the owner check and `IIdentifyResource<TResource, TId>` so the framework reuses the shared `SharedResourceLoaderById<TResource, TId>` instead of requiring a per-command loader.

```csharp
using Trellis;
using Trellis.Authorization;

public sealed record CancelOrderCommand(OrderId OrderId)
    : ICommand<Result<Unit>>,
      IAuthorizeResource<Order>,
      IIdentifyResource<Order, OrderId>
{
    public OrderId GetResourceId() => OrderId;

    public IResult Authorize(Actor actor, Order resource) =>
        resource.OwnerId == actor.Id || actor.HasPermission("orders:admin")
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden(
                PolicyId: "orders.owner",
                Resource: ResourceRef.For<Order>(OrderId)));
}

// DI (composition root):
services.AddResourceAuthorization<CancelOrderCommand, Order, Result<Unit>>();
// One per command — AOT-safe; or use the assembly-scanning overload.
```

That's it. The mediator pipeline:

1. resolves the actor via the registered `IActorProvider`,
2. loads the `Order` by `OrderId` via the shared `SharedResourceLoaderById<Order, OrderId>` (auto-registered when the command implements `IIdentifyResource<Order, OrderId>`),
3. calls `Authorize(actor, order)`.

For multi-hop authorization (the resource the actor must own is reached via one or more navigation hops, including the cricket "actor owns home OR away team" shape), use `IAuthorizeResourceVia<TOwner>` with `IIdentifyRelatedResource[s]<TRelated, TId>` along the path — see cookbook [Recipe 24](trellis-api-cookbook.md#recipe-24--indirect-multi-hop-resource-authorization).

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| **Owner check on the command's resource (the 90% case)** | Implement `IAuthorizeResource<TResource>` + `IIdentifyResource<TResource, TId>`; the framework loads via `SharedResourceLoaderById<TResource, TId>` and calls `Authorize(actor, resource)` | [Owner check quick-start](#owner-check-quick-start--copy-this) |
| Represent the current user/service | `Actor` | [`Actor`](#actor) |
| Check granted permissions with explicit deny override | `actor.HasPermission(...)`, `HasAllPermissions(...)`, `HasAnyPermission(...)` | [`Actor`](#actor) |
| Resolve actor for a request/message | `IActorProvider.GetCurrentActorAsync(...)` | [`IActorProvider`](#iactorprovider) |
| Require static permissions on a message | Implement `IAuthorize.RequiredPermissions` | [`IAuthorize`](#iauthorize) |
| Authorize against a loaded resource | Implement `IAuthorizeResource<TResource>.Authorize(actor, resource)` | [`IAuthorizeResource<TResource>`](#iauthorizeresourcetresource) |
| Write owner/admin resource guards | `Result.Ensure(condition, new Error.Forbidden(...))` | [`IAuthorizeResource<TResource>`](#iauthorizeresourcetresource), [Core `Result.Ensure`](trellis-api-core.md#public-static-partial-class-result) |
| Identify a resource by id for shared loading | `IIdentifyResource<TResource, TId>` | [`IIdentifyResource<TResource, TId>`](#iidentifyresourcetresource-tid) |
| Authorize against a related resource one or more navigation hops away (cricket-style fan-out, owner chains) | Implement `IAuthorizeResourceVia<TOwner>` on the command + `IIdentifyRelatedResource<TRelated, TId>` (singular) or `IIdentifyRelatedResources<TRelated, TId>` (terminal plural) on entities along the path | [`IAuthorizeResourceVia<TOwner>`](#iauthorizeresourceviatowner), [`IIdentifyRelatedResource<TRelated, TId>`](#iidentifyrelatedresourcetrelated-tid), [`IIdentifyRelatedResources<TRelated, TId>`](#iidentifyrelatedresourcestrelated-tid) |

## Common traps

- `Trellis.Authorization` is domain/application-layer only. ASP.NET actor providers are documented in [trellis-api-asp.md](trellis-api-asp.md#namespace-trellisaspauthorization).
- Prefer `Result.Ensure` for boolean authorization guards so generated code uses the same ROP primitive as the rest of Trellis.
- Do not mutate `RequiredPermissions`; expose the complete permission list as an immutable/read-only collection.
- The DI registration extension `AddResourceAuthorization(...)` lives in `Trellis.Mediator` (`namespace Trellis.Mediator`), not in `Trellis.Authorization`. Wiring an `IAuthorizeResource<TResource>` therefore typically requires both `using Trellis.Authorization;` (for the interfaces) and `using Trellis.Mediator;` (for the DI extension). The compile error if the second is missing is `CS1061: 'IServiceCollection' does not contain a definition for 'AddResourceAuthorization' and no accessible extension method 'AddResourceAuthorization' accepting a first argument of type 'IServiceCollection' could be found` — see [trellis-api-mediator.md](trellis-api-mediator.md#servicecollectionextensions).

## Common identity-provider claim shapes

The default `ClaimsActorProvider` (in `Trellis.Asp.Authorization`) maps a flat claim name to `Actor.Id` and a flat claim name to `Actor.Permissions`. Identity providers that ship claims under a nested JSON object require either the `NestedJsonPathClaimsActorProvider` derivation or a custom override of `ClaimsActorProvider.GetCurrentActorAsync`. The table below covers the configurations that most consumers reach for; inspect your token to confirm the exact shape.

| Provider | Token shape | Recommended configuration |
|---|---|---|
| **Auth0** (default JWT) | `{ "sub": "auth0\|abc", "permissions": ["orders:read"] }` | `AddClaimsActorProvider(opts => { opts.ActorIdClaim = "sub"; opts.PermissionsClaim = "permissions"; })` |
| **Auth0** (`app_metadata.roles`) | `{ "sub": "auth0\|abc", "app_metadata": { "roles": ["orders:read"] } }` | `AddNestedJsonPathClaimsActorProvider(opts => { opts.ActorIdClaim = "sub"; opts.ContainerClaim = "app_metadata"; opts.PermissionsPath = "roles"; })` |
| **Azure B2C** custom attribute | `{ "sub": "...", "extension_RolesJson": "{\"roles\":[\"orders:read\"]}" }` | `AddNestedJsonPathClaimsActorProvider(opts => { opts.ActorIdClaim = "sub"; opts.ContainerClaim = "extension_RolesJson"; opts.PermissionsPath = "roles"; })` |
| **Entra ID v2 (interactive user)** | uses `oid` + multiple `roles` claims | `AddEntraActorProvider(opts => { ... })` — purpose-built for Entra; see the type reference |
| **Keycloak** (`realm_access.roles`) | `{ "sub": "...", "realm_access": { "roles": ["orders:read"] } }` | `AddNestedJsonPathClaimsActorProvider(opts => { opts.ActorIdClaim = "sub"; opts.ContainerClaim = "realm_access"; opts.PermissionsPath = "roles"; })` |
| **Okta** (nested custom claim) | `{ "sub": "...", "custom_claims": { "permissions": ["orders:read"] } }` | `AddNestedJsonPathClaimsActorProvider(opts => { opts.ActorIdClaim = "sub"; opts.ContainerClaim = "custom_claims"; opts.PermissionsPath = "permissions"; })` |

**Silent-403 diagnostics.** When `ClaimsActorProvider` resolves zero permissions on an authenticated identity that does carry other claims, it emits one warning per application lifetime (`EventId 2`). When the configured `PermissionsClaim` resolves to a single value that parses as a JSON object or array, it emits one error per application lifetime (`EventId 3`) recommending `NestedJsonPathClaimsActorProvider`. Set `ClaimsActorOptions.ValidateClaimShapeOnFirstUse = false` to suppress both.

**Startup validation of `NestedJsonPathClaimsActorOptions`.** `AddNestedJsonPathClaimsActorProvider(...)` registers an `IValidateOptions<NestedJsonPathClaimsActorOptions>` with `ValidateOnStart()`. If `ActorIdPath` or `PermissionsPath` is configured without a non-blank `ContainerClaim` (empty or whitespace-only both count as missing), host startup fails with `OptionsValidationException` — much earlier than the first-request `InvalidOperationException` that the provider's constructor would otherwise produce. The constructor guard is kept as defense-in-depth for manual (non-DI) construction. Two caveats:

- The validator is permissive when the options are at defaults (paths empty) so a registered-but-unconfigured provider doesn't block startup.
- `ValidateOnStart()` fires regardless of whether a later `AddXxxActorProvider` call replaces `IActorProvider`. A consumer that calls `AddNestedJsonPathClaimsActorProvider(opts => { ... invalid ... })` and then `AddClaimsActorProvider()` will still see startup fail on the nested options. Pick one provider per host; if you must replace one already-registered, remove the earlier call rather than chaining.

## Types

### Namespace `Trellis.Authorization`

### `Actor`

**Declaration**

```csharp
public sealed class Actor : IEquatable<Actor>
```

`Actor` is an authorization-layer **entity**. Equality / `GetHashCode` / `==` / `!=` are based on the `Id` property only — two `Actor` instances with the same `Id` represent the same principal even when their `Permissions`, `ForbiddenPermissions`, or `Attributes` differ (those are point-in-time state about the principal, not part of identity). Mirrors `Trellis.Entity<TId>` semantics without inheriting the full `IAggregate` surface.

**Constructors**

| Signature | Description |
| --- | --- |
| `public Actor(ActorId id, IReadOnlySet<string> permissions, IReadOnlySet<string> forbiddenPermissions, IReadOnlyDictionary<string, string> attributes)` | Typed constructor. Snapshots the supplied state into frozen collections (ordinal comparison). Throws `ArgumentNullException` when any argument is null. |
| `public Actor(string id, IReadOnlySet<string> permissions, IReadOnlySet<string> forbiddenPermissions, IReadOnlyDictionary<string, string> attributes)` | Convenience overload that wraps the raw claim string in `ActorId` (via `ActorId.Create`) and delegates to the typed constructor. Throws `ArgumentException` when `id` is null / empty / whitespace. |

**Fields**

| Name | Type | Description |
| --- | --- | --- |
| `PermissionScopeSeparator` | `const char` | Separator used between permission name and scope in scoped permission strings. Value: `':'`. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `Id` | `ActorId` | Strongly-typed principal identifier (e.g. JWT `sub`). Wraps the raw claim string in a [`RequiredString<ActorId>`](trellis-api-core.md#requiredstringtself) so the identity flows through authorization-layer APIs and consumer aggregate fields as a domain type rather than an untyped `string`. |
| `Permissions` | `IReadOnlySet<string>` | Granted permissions. Ordinal comparison; setter snapshots into a `FrozenSet<string>`. |
| `ForbiddenPermissions` | `IReadOnlySet<string>` | Explicit deny-list. Deny always overrides allow. Snapshotted into a `FrozenSet<string>`. |
| `Attributes` | `IReadOnlyDictionary<string, string>` | ABAC attributes. Snapshotted into a `FrozenDictionary<string, string>` with ordinal comparer. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Actor Create(ActorId id, IReadOnlySet<string> permissions)` | `Actor` | Typed factory. Creates an actor with empty `ForbiddenPermissions` and empty `Attributes`. Throws `ArgumentNullException` for null arguments. |
| `public static Actor Create(string id, IReadOnlySet<string> permissions)` | `Actor` | Convenience factory that wraps the raw claim string in `ActorId`. |
| `public bool HasPermission(string permission)` | `bool` | Returns `true` only when `permission` is in `Permissions` and not in `ForbiddenPermissions`. |
| `public bool HasPermission(string permission, string scope)` | `bool` | Checks the scoped permission composed as `permission` + `':'` + `scope` (deny-aware). Throws `ArgumentNullException` when either argument is null. |
| `public bool HasAllPermissions(IEnumerable<string> permissions)` | `bool` | `true` when every entry passes `HasPermission`. |
| `public bool HasAnyPermission(IEnumerable<string> permissions)` | `bool` | `true` when at least one entry passes `HasPermission`. |
| `public bool IsOwner(ActorId resourceOwnerId)` | `bool` | Typed equality check between `Id` and `resourceOwnerId` using `ActorId`'s value-equality semantics. Prefer this overload when the owner id is itself a typed `ActorId` so the comparison is type-checked at the call site. |
| `public bool IsOwner(string resourceOwnerId)` | `bool` | Convenience overload that compares `Id.Value` and `resourceOwnerId` with `StringComparison.Ordinal`. |
| `public bool HasAttribute(string key)` | `bool` | `true` when `Attributes` contains `key`. |
| `public string? GetAttribute(string key)` | `string?` | Returns the attribute value or `null` when absent. |
| `public Result<TVo> GetRequiredAttribute<TVo>(string key) where TVo : class, IParsable<TVo>` | `Result<TVo>` | Parses the attribute through the value object's `IParsable` implementation (any source-generated `Required*` VO — `string`-, `Guid`-, `int`-backed, and so on), validating via its `TryParse` (which routes through `TryCreate`): a success `Result` with the typed value when present and valid, otherwise a failed `Result` with an `Error.InvalidInput` whose field is `key` (a missing attribute fails the same way). Throws `ArgumentNullException` when `key` is null. |
| `public bool TryGetAttribute<TVo>(string key, out TVo? value) where TVo : class, IParsable<TVo>` | `bool` | `true` with the parsed `value` when the attribute is present and valid; `false` (and `value` is null) when absent or invalid — deny-closes naturally in an authorization gate. Parses through the VO's `IParsable`, so any backing primitive works. Throws `ArgumentNullException` when `key` is null. |

### `ActorId`

**Declaration**

```csharp
public sealed partial class ActorId : RequiredString<ActorId>;
```

Strongly-typed wrapper around the raw principal id (typically the JWT `sub` or AAD `oid` claim) so the authorization layer exposes a domain type instead of an untyped `string`. It uses the strict `RequiredString<ActorId>` defaults: the value is trimmed on construction and an empty / whitespace-only id is rejected. Generated factories `ActorId.Create(string)` / `ActorId.TryCreate(string?)` come from the bundled source generator (see [`trellis-api-core.md`](trellis-api-core.md#primitive-value-object-base-classes)).

Consumers that store the principal id at aggregate boundaries — audit-style fields like `Order.CreatedByActorId` or `Document.LastModifiedByActorId` — should reuse `ActorId` for those fields so cross-aggregate comparisons (`actor.IsOwner(order.CreatedByActorId)`) are type-checked end-to-end. Domain identifiers that are conceptually different from the principal id (a customer aggregate id, a tenant member id, a domain user aggregate's primary key) remain whatever VO the domain models and are resolved to / from the principal at the application service boundary.

### `ActorAttributes`

**Declaration**

```csharp
public static class ActorAttributes
```

Well-known string keys for `Actor.Attributes`. Claim-derived keys align with Azure Entra ID v2.0 access tokens.

**Fields**

| Name | Type | Description |
| --- | --- | --- |
| `TenantId` | `const string` | Entra `tid` claim — issuing tenant GUID. Value: `"tid"`. |
| `PreferredUsername` | `const string` | Entra `preferred_username` claim. Value: `"preferred_username"`. |
| `AuthorizedParty` | `const string` | Entra `azp` claim — application ID of the calling client. Value: `"azp"`. |
| `AuthorizedPartyAcr` | `const string` | Entra `azpacr` claim — client authentication strength (`0` public, `1` secret, `2` certificate). Value: `"azpacr"`. |
| `AuthContextClassReference` | `const string` | Entra `acrs` claim — Conditional Access auth-context references. Value: `"acrs"`. |
| `IpAddress` | `const string` | Request IP address, populated from `HttpContext.Connection.RemoteIpAddress`. Value: `"ip_address"`. |
| `MfaAuthenticated` | `const string` | Whether the actor authenticated with MFA — derived from the `amr` claim. Value: `"mfa"`. |

### `IActorProvider`

**Declaration**

```csharp
public interface IActorProvider
```

| Signature | Returns | Description |
| --- | --- | --- |
| `Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Maybe<Actor>>` | Returns the current authenticated actor wrapped in `Maybe.From(...)`, or `Maybe<Actor>.None` when the request has no usable authenticated actor. The mediator authorization pipeline maps `Maybe.None` to `Error.AuthenticationRequired` (HTTP 401) per RFC 9110 §15.5.2. Implementations should throw `InvalidOperationException` only for genuine infrastructure or configuration failures (no `HttpContext`, mapping delegate threw, etc.) — those still surface as `Error.Unexpected` (HTTP 500). "No actor" is client-error state, not an exception. Register as scoped. |

**401 vs 500 contract.** `Maybe<Actor>.None` means the framework cannot identify an actor for the request — typically the request lacks credentials, the auth middleware did not produce an authenticated identity, or the configured actor-id claim is missing from an otherwise authenticated identity. All three are classes of client error and the mediator pipeline emits HTTP 401. A thrown `InvalidOperationException` means the provider itself cannot operate (no `HttpContext`, malformed configuration, mapping delegate failure) — that's a server bug and surfaces as HTTP 500.

> **`WWW-Authenticate` header on the 401.** RFC 9110 §11.6.1 requires `WWW-Authenticate` on 401 responses. `Error.AuthenticationRequired` stays transport-neutral, so `ResponseFailureWriter` emits the optional `Scheme` string verbatim when the error provides one; otherwise it synthesizes a scheme-level challenge from the registered default-challenge scheme via `IAuthenticationSchemeProvider` when no other middleware has already written the header. The synthesized header uses the scheme NAME registered with `AddAuthentication` (so `AddJwtBearer("ApiJwt", ...)` produces `WWW-Authenticate: ApiJwt`). If no authentication is registered at all, the writer emits no synthesized header — synthesizing "Bearer" for a service that does not use Bearer would mislead clients. Applications that need a parameterized challenge can return `new Error.AuthenticationRequired("Bearer realm=\"api\"")` or write `WWW-Authenticate` before Trellis renders the failure.

### `IAuthorize`

**Declaration**

```csharp
public interface IAuthorize
```

Marker for commands/queries enforcing static (permission-only) authorization. The mediator's `AuthorizationBehavior<TMessage, TResponse>` requires the current actor to hold **all** listed permissions (AND semantics).

| Name | Type | Description |
| --- | --- | --- |
| `RequiredPermissions` | `IReadOnlyList<string>` | Permissions every caller must hold. Duplicates and order are ignored — the check is set-like under AND-semantics. |

### `IAuthorizeResource<TResource>`

**Declaration**

```csharp
public interface IAuthorizeResource<in TResource>
```

Implemented by a command/query to perform resource-based authorization once the resource has been loaded.

| Signature | Returns | Description |
| --- | --- | --- |
| `IResult Authorize(Actor actor, TResource resource)` | `IResult` | Returns success to proceed or a failure (typically `Error.Forbidden`) to short-circuit the pipeline. |

### `IIdentifyResource<TResource, TId>`

**Declaration**

```csharp
public interface IIdentifyResource<TResource, out TId>
```

Companion to `IAuthorizeResource<TResource>` that exposes a typed resource identifier so the pipeline can use a `SharedResourceLoaderById<TResource, TId>` instead of a per-command loader.

| Signature | Returns | Description |
| --- | --- | --- |
| `TId GetResourceId()` | `TId` | Extracts the typed resource ID from the message. |

### `IAuthorizeResourceVia<TOwner>`

**Declaration**

```csharp
public interface IAuthorizeResourceVia<TOwner>
```

Declares resource-based authorization against a resource that is **not** the leaf the command identifies, but is reachable via one or more `IIdentifyRelatedResource[s]<,>` declarations on entities along the navigation chain. The originating motivation is the cricket-style "actor owns Team1 OR Team2" pattern: command identifies a `Match`, authorization is evaluated against the set of teams it points at.

The pipeline always passes `IReadOnlyList<TOwner>` to `Authorize` — size 1 for chains terminating in a singular hop, size N for chains terminating in a plural hop (cricket fan-out). The framework does not impose the operator over the list; the command picks `Any`, `All`, or any other shape inside `Authorize`.

| Signature | Returns | Description |
| --- | --- | --- |
| `IResult Authorize(Actor actor, IReadOnlyList<TOwner> owners)` | `IResult` | Returns success to proceed or a failure (typically `Error.Forbidden`) to short-circuit. The `owners` list is non-null and non-empty; an empty plural navigation short-circuits to `Error.Forbidden` before `Authorize` is called. |

**Failure semantics**:

- **Leaf load failure** — the loader's error bubbles verbatim (matches existing `IAuthorizeResource<T>` semantics for the resource the command identifies).
- **Intermediate or owner load failure** — collapsed to `Error.Forbidden` to avoid leaking existence of related resources whose presence/absence the actor may not be authorized to learn.
- **Empty result at any hop** (singular extract returning 0 IDs or plural extract returning 0 IDs) — short-circuits to `Error.Forbidden` without invoking `Authorize`.
- **Missing `SharedResourceLoaderById<TTo, TToId>`** at any hop — throws `InvalidOperationException` (deployment bug, not authorization denial).

A command may implement either `IAuthorizeResource<T>` **or** `IAuthorizeResourceVia<TOwner>`, never both. Registration throws at startup if both are present — security primitives are not silently composed.

### `IIdentifyRelatedResource<TRelated, TId>`

**Declaration**

```csharp
public interface IIdentifyRelatedResource<TRelated, out TId>
```

Entity-side declaration of a single outbound foreign-key navigation. Used by the resolver to walk the navigation chain at registration time. Implement on aggregate roots whose authorization is evaluated against a different aggregate one or more hops away.

| Signature | Returns | Description |
| --- | --- | --- |
| `TId GetRelatedResourceId()` | `TId` | Returns the identifier of the related resource of type `TRelated`. |

For two distinct outbound navigations to the same `TRelated` (cricket `Match.HomeTeamId` / `Match.AwayTeamId`), use [`IIdentifyRelatedResources<TRelated, TId>`](#iidentifyrelatedresourcestrelated-tid) instead — C# disallows declaring the same generic interface twice on a single type.

### `IIdentifyRelatedResources<TRelated, TId>`

**Declaration**

```csharp
public interface IIdentifyRelatedResources<TRelated, TId>
```

Entity-side declaration of a plural outbound navigation (fan-out). Used at the **terminal** hop of an authorization chain to express OR-style / candidate-set authorization (cricket `Match → {HomeTeam, AwayTeam}`).

| Signature | Returns | Description |
| --- | --- | --- |
| `IReadOnlyList<TId> GetRelatedResourceIds()` | `IReadOnlyList<TId>` | Returns the identifiers of all related resources of type `TRelated`. Non-null. Duplicates are de-duplicated by the pipeline before loading; an empty list short-circuits to `Error.Forbidden`. Order is not significant. |

Plural-in-middle (fan-out cartesian expansion) is intentionally out of scope for v1 and rejected at registration time. For chains needing a plural intermediate hop, drop to `IResourceLoader<TMessage, TProjection>` with a projection type.

### `IResourceLoader<TMessage, TResource>`

**Declaration**

```csharp
public interface IResourceLoader<in TMessage, TResource>
```

Loads the resource required for resource-based authorization. Resolved per request from DI as scoped.

| Signature | Returns | Description |
| --- | --- | --- |
| `Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Returns the loaded resource or a failure (typically `Error.NotFound`). The pipeline short-circuits on failure before invoking `IAuthorizeResource<TResource>.Authorize`. |

### `ResourceLoaderById<TMessage, TResource, TId>`

**Declaration**

```csharp
public abstract class ResourceLoaderById<TMessage, TResource, TId> : IResourceLoader<TMessage, TResource>
```

Convenience base for loaders that extract an ID from the message and call a repository.

| Signature | Returns | Description |
| --- | --- | --- |
| `protected abstract TId GetId(TMessage message)` | `TId` | Extract the resource ID from the message. |
| `protected abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Fetch the resource by ID; return `Result.Fail` with `Error.NotFound` when missing. |
| `public Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Sealed glue: calls `GetId(message)` then `GetByIdAsync(...)`. |

### `SharedResourceLoaderById<TResource, TId>`

**Declaration**

```csharp
public abstract class SharedResourceLoaderById<TResource, TId>
```

A single loader shared across every command that authorizes against the same `TResource`. When a command implements both `IAuthorizeResource<TResource>` and `IIdentifyResource<TResource, TId>` the pipeline bridges to this shared loader automatically. Explicit `IResourceLoader<TMessage, TResource>` registrations win over the shared loader.

`Trellis.Mediator.ServiceCollectionExtensions.AddResourceAuthorization(...)` registers all concrete `SharedResourceLoaderById<TResource, TId>` implementations as **scoped** — safe to depend on a `DbContext` or other scoped repository. Replace the registration after the scan completes if a different lifetime is required.

| Signature | Returns | Description |
| --- | --- | --- |
| `public abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Load the resource by ID; return `Result.Fail` with `Error.NotFound` when missing. |

### `IAuthorizedResource<TMessage, TResource>`

**Declaration**

```csharp
namespace Trellis.Authorization;

public interface IAuthorizedResource<TMessage, TResource>
    where TResource : class
{
    TResource GetRequiredResource();
    bool TryGetResource([MaybeNullWhen(false)] out TResource resource);
}
```

**Purpose.** v4 typed accessor that gives a handler the **same instance** of the resource that `ResourceAuthorizationBehavior` (or `ResourceAuthorizationViaBehavior`) loaded for the current dispatch — eliminating a duplicate load when the handler would otherwise re-fetch by id from its repository. For non-EF stores (CosmosDB / Dapper / HTTP-backed loaders) this is a measurable performance win; for EF it skips the second LINQ query while preserving change-tracker identity.

**Auto-registration.** Every public entry that registers resource authorization also registers the closed `IAuthorizedResource<TMessage, TResource>` as scoped. Handlers inject it via constructor injection; no additional composition-root call is required.

- For `IAuthorizeResource<TResource>` commands: `TResource` is the same `TResource`.
- For `IAuthorizeResourceVia<TOwner>` commands: `TResource` is the **leaf** (the resource identified via `IIdentifyResource<TLeaf, TLeafId>`), which is the typical mutation target. The owner accessor is intentionally not exposed in v4; handlers needing owner state read it from their repository.

| Signature | Returns | Description |
| --- | --- | --- |
| `TResource GetRequiredResource()` | `TResource` | Returns the resource loaded and authorized for the current pipeline dispatch. Throws `InvalidOperationException` outside a populated dispatch — typical causes: the handler was invoked directly (e.g. from a unit test) without going through the mediator pipeline; the message lacks resource-authorization registration; authentication failed, the loader failed, or `Authorize` was denied (none of which populate the accessor). The exception message names the closed pair so misconfiguration is easy to diagnose. |
| `bool TryGetResource(out TResource? resource)` | `bool` | Returns `true` with the resource when populated; `false` and `null` otherwise. Provided for genuinely optional reads (e.g., diagnostic logging that runs both inside and outside the pipeline). Production handlers should prefer `GetRequiredResource()` so a misconfigured pipeline fails loudly instead of silently skipping work. |

**Identity, not mutation-readiness.** The accessor returns the SAME instance the loader returned. The framework does not validate that the instance is a canonical mutation-ready aggregate. **Do not inject the accessor** for commands whose loader returns:

- A projection type (e.g. `OrderHeader` for cheap authorization, not the full `Order` aggregate).
- A no-tracking EF entity that the handler must mutate (the mutation will not persist).
- A stale read-replica POCO when the handler needs strong consistency.
- An HTTP DTO that cannot be persisted back through any local repository.

In those cases the handler reloads via the repository. The loader's job is to decide who owns the resource for the authorization check; the handler's job is to fetch the canonical mutation-ready aggregate. These are different shapes and the accessor would couple them incorrectly.

**Concurrency.** Safe across nested `mediator.Send` and concurrent `Task.WhenAll` dispatch of the same closed pair within one DI scope. Implementation uses a per-async-flow linked frame list with a volatile `IsActive` flag — each push allocates a new frame (no shared mutable state between sibling forks), and dispose flips the frame's `IsActive` flag (visible to orphan child tasks that captured the frame at fork time but outlived the parent dispatch). The framework guarantees an orphan task cannot read the resource after the parent's dispatch ends.

See [Recipe 31](trellis-api-cookbook.md#recipe-31--avoid-duplicate-load-with-iauthorizedresourcetcommand-tresource) in the cookbook for the WRONG/FIX shape and a full end-to-end example.

## Behavioral notes

- **Deny overrides allow.** A permission listed in both `Permissions` and `ForbiddenPermissions` is denied. `HasPermission`, `HasAllPermissions`, `HasAnyPermission`, and `HasPermission(permission, scope)` all observe this rule.
- **Ordinal comparison everywhere.** Permission lookups, attribute lookups, and `IsOwner` use `StringComparison.Ordinal`. Hydrate permissions and attributes with consistent casing.
- **Permissions snapshot to frozen collections.** Mutating a collection passed into `Actor` after construction has no effect; the actor exposes a `FrozenSet<string>` / `FrozenDictionary<string, string>` snapshot for O(1) lookups.
- **Scoped permissions** use the `"Permission:Scope"` convention (`Document.Edit:Tenant_A`). Add scoped entries directly to `Permissions` and check via `HasPermission(string, string)` — no separate scope collection.
- **Exact match only — no check-time wildcards.** `HasPermission` is a set lookup, so a granted `"seasons:*"` does **not** match a `HasPermission("seasons:read")` check, and a granted unscoped `"seasons"` does **not** satisfy `HasPermission("seasons", scope)`. Expand any `entity:*`-style authoring shorthand into a concrete permission catalog when hydrating the `Actor` (e.g. a static `Permissions` constants class plus a role→permissions map), consistent with the pre-flatten guidance above. `Trellis.Asp` ships [`RolePermissionProjection`](trellis-api-asp.md#rolepermissionprojection) to perform that role→permissions flattening from an app-supplied map. This keeps every check O(1) and avoids the privilege-escalation surface of pattern matching at authorization time.
- **Pipeline ordering.** When a command implements both `IAuthorize` (static) and `IAuthorizeResource<TResource>` (resource), the mediator behavior runs static checks first; resource loading and `Authorize(actor, resource)` only execute if the static check passes. A loader failure short-circuits before `Authorize` is called.

## Code examples

### Static permission authorization

```csharp
using Trellis;
using Trellis.Authorization;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed record DeleteOrderCommand(OrderId OrderId) : ICommand<Result<Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions { get; } = ["orders:delete"];
}
```

### Resource-based authorization with a shared loader

> **Preferred in generated services.** Use `IIdentifyResource<TResource, TId>` + `SharedResourceLoaderById<TResource, TId>` for resource authorization. A per-command `IResourceLoader<TMessage, TResource>` is an escape hatch for request-scoped state or command-specific load logic.

```csharp
using Trellis;
using Trellis.Authorization;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed record Order(OrderId Id, ActorId OwnerId);

public sealed record CancelOrderCommand(OrderId OrderId)
    : ICommand<Result<Unit>>, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>
{
    public OrderId GetResourceId() => OrderId;

    public IResult Authorize(Actor actor, Order order) =>
        Result.Ensure(
            order.OwnerId == actor.Id || actor.HasPermission("orders:cancel-any"),
            new Error.Forbidden("orders.cancel")
                { Detail = "Only the owner can cancel this order." });
}

public interface IOrderRepository
{
    Task<Maybe<Order>> FindByIdAsync(OrderId id, CancellationToken ct);
}

public sealed class OrderResourceLoader(IOrderRepository repo)
    : SharedResourceLoaderById<Order, OrderId>
{
    public override async Task<Result<Order>> GetByIdAsync(OrderId id, CancellationToken ct) =>
        (await repo.FindByIdAsync(id, ct)).ToResult(new Error.NotFound(ResourceRef.For<Order>(id)));
}
```

### Indirect (multi-hop) resource authorization — cricket fan-out

Authorize a `UploadScorecardCommand` against the actor owning **either** the home or away team of the match. The command identifies the leaf (`Match`); `Match` declares its plural outbound navigation to `Team`; the command's `Authorize` runs against the loaded list.

```csharp
using Trellis;
using Trellis.Authorization;

public sealed partial class MatchId : RequiredGuid<MatchId>;
public sealed partial class TeamId : RequiredGuid<TeamId>;

public sealed class Match : Aggregate<MatchId>, IIdentifyRelatedResources<Team, TeamId>
{
    public TeamId HomeTeamId { get; }
    public TeamId AwayTeamId { get; }
    public IReadOnlyList<TeamId> GetRelatedResourceIds() => [HomeTeamId, AwayTeamId];
}

public sealed class Team : Aggregate<TeamId>
{
    public ActorId CreatedByActorId { get; }
}

public sealed record UploadScorecardCommand(MatchId MatchId, /* fields */)
    : ICommand<Result<Unit>>,
      IAuthorizeResourceVia<Team>,
      IIdentifyResource<Match, MatchId>
{
    public MatchId GetResourceId() => MatchId;

    public IResult Authorize(Actor actor, IReadOnlyList<Team> owners) =>
        Result.Ensure(
            owners.Any(t => t.CreatedByActorId == actor.Id),
            new Error.Forbidden("match.upload-scorecard")
                { Detail = "Actor does not own either match team." });
}

// Composition root: assembly scan registers the via-behavior, the leaf-loader bridge,
// and the SharedResourceLoaderById<Match,MatchId> + SharedResourceLoaderById<Team,TeamId>.
services.AddTrellisBehaviors();
services.AddResourceAuthorization(typeof(UploadScorecardCommand).Assembly);
```

For chains (e.g. `Match → Team → Tournament`), declare `IIdentifyRelatedResource<Team, TeamId>` on `Match` and `IIdentifyRelatedResource<Tournament, TournamentId>` on `Team`, then set `IAuthorizeResourceVia<Tournament>` on the command.

For shapes the navigation-chain model can't express (composite keys, conditional/data-dependent paths, recursive hierarchies, projections, joins, plural-in-middle), drop to an explicit `IResourceLoader<TMessage, TProjection>` returning a custom projection, and put `IAuthorizeResource<TProjection>` on the command.

### Constructing an `Actor` directly (tests, custom providers)

```csharp
using System.Collections.Generic;
using Trellis.Authorization;

var actor = new Actor(
    id: "user-1",
    permissions: new HashSet<string>
    {
        "orders:cancel",
        $"orders:view{Actor.PermissionScopeSeparator}tenant-1",
    },
    forbiddenPermissions: new HashSet<string>(),
    attributes: new Dictionary<string, string>
    {
        [ActorAttributes.TenantId] = "tenant-1",
        [ActorAttributes.MfaAuthenticated] = "true",
    });

bool canCancel = actor.HasPermission("orders:cancel");
bool canViewTenant = actor.HasPermission("orders:view", "tenant-1");
string? tenant = actor.GetAttribute(ActorAttributes.TenantId);

// Typed accessors parse the claim through the value object's IParsable — any backing primitive,
// here a Guid-backed tenant id (where: public sealed partial class TenantId : RequiredGuid<TenantId>;):
Result<TenantId> tenantVo = actor.GetRequiredAttribute<TenantId>(ActorAttributes.TenantId);
bool hasTenant = actor.TryGetAttribute<TenantId>(ActorAttributes.TenantId, out var typedTenant);
```

## Cross-references

- [trellis-api-asp.md](trellis-api-asp.md#namespace-trellisaspauthorization) — `Trellis.Asp.Authorization` actor providers (`ClaimsActorProvider`, `EntraActorProvider`, `DevelopmentActorProvider`, `CachingActorProvider`) and the matching `AddClaimsActorProvider` / `AddEntraActorProvider` / `AddDevelopmentActorProvider` / `AddCachingActorProvider<T>` registration helpers.
- [trellis-api-mediator.md](trellis-api-mediator.md#authorizationbehaviortmessage-tresponse) — `AuthorizationBehavior<TMessage, TResponse>` pipeline behavior.
- [trellis-api-core.md](trellis-api-core.md#public-abstract-record-error) — `Result`, `Error.Forbidden`, `Error.NotFound`.
- [trellis-api-testing-aspnetcore.md](trellis-api-testing-aspnetcore.md#webapplicationfactoryextensions) — `WebApplicationFactoryExtensions.CreateClientWithActor` (writes the `X-Test-Actor` header consumed by `DevelopmentActorProvider`).
