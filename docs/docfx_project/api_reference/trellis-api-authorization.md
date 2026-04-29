# Trellis.Authorization — API Reference

**Package:** `Trellis.Authorization`
**Namespace:** `Trellis.Authorization`
**Purpose:** Domain-layer authorization primitives — actor identity / permission / attribute model and the contracts used by the mediator's authorization behavior to perform static (permission) and resource-based authorization. This package contains no ASP.NET Core dependencies; the `IActorProvider` implementations and DI helpers ship in `Trellis.Asp` (see [`trellis-api-asp.md`](trellis-api-asp.md), namespace `Trellis.Asp.Authorization`).

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

## Types

### Namespace `Trellis.Authorization`

### `Actor`

**Declaration**

```csharp
public sealed record Actor
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public Actor(string id, IReadOnlySet<string> permissions, IReadOnlySet<string> forbiddenPermissions, IReadOnlyDictionary<string, string> attributes)` | Snapshots the supplied state into frozen collections (ordinal comparison). Throws `ArgumentException` for null/whitespace `id`. |

**Fields**

| Name | Type | Description |
| --- | --- | --- |
| `PermissionScopeSeparator` | `const char` | Separator used between permission name and scope in scoped permission strings. Value: `':'`. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `Id` | `string` | Unique actor identifier (e.g. JWT `sub`). |
| `Permissions` | `IReadOnlySet<string>` | Granted permissions. Ordinal comparison; setter snapshots into a `FrozenSet<string>`. |
| `ForbiddenPermissions` | `IReadOnlySet<string>` | Explicit deny-list. Deny always overrides allow. Snapshotted into a `FrozenSet<string>`. |
| `Attributes` | `IReadOnlyDictionary<string, string>` | ABAC attributes. Snapshotted into a `FrozenDictionary<string, string>` with ordinal comparer. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Actor Create(string id, IReadOnlySet<string> permissions)` | `Actor` | Creates an actor with empty `ForbiddenPermissions` and empty `Attributes`. |
| `public bool HasPermission(string permission)` | `bool` | Returns `true` only when `permission` is in `Permissions` and not in `ForbiddenPermissions`. |
| `public bool HasPermission(string permission, string scope)` | `bool` | Checks the scoped permission `${permission}:${scope}` (deny-aware). |
| `public bool HasAllPermissions(IEnumerable<string> permissions)` | `bool` | `true` when every entry passes `HasPermission`. |
| `public bool HasAnyPermission(IEnumerable<string> permissions)` | `bool` | `true` when at least one entry passes `HasPermission`. |
| `public bool IsOwner(string resourceOwnerId)` | `bool` | Compares `Id` and `resourceOwnerId` with `StringComparison.Ordinal`. |
| `public bool HasAttribute(string key)` | `bool` | `true` when `Attributes` contains `key`. |
| `public string? GetAttribute(string key)` | `string?` | Returns the attribute value or `null` when absent. |

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
| `Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Returns the current authenticated actor. Implementations should throw `InvalidOperationException` (or equivalent) when the request is unauthenticated or the actor cannot be resolved. Register as scoped. |

### `IAuthorize`

**Declaration**

```csharp
public interface IAuthorize
```

Marker for commands/queries enforcing static (permission-only) authorization. The mediator's `AuthorizationBehavior<TMessage, TResponse>` requires the current actor to hold **all** listed permissions (AND semantics).

| Name | Type | Description |
| --- | --- | --- |
| `RequiredPermissions` | `IReadOnlyList<string>` | Permissions every caller must hold. |

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

| Signature | Returns | Description |
| --- | --- | --- |
| `public abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Load the resource by ID; return `Result.Fail` with `Error.NotFound` when missing. |

## Behavioral notes

- **Deny overrides allow.** A permission listed in both `Permissions` and `ForbiddenPermissions` is denied. `HasPermission`, `HasAllPermissions`, `HasAnyPermission`, and `HasPermission(permission, scope)` all observe this rule.
- **Ordinal comparison everywhere.** Permission lookups, attribute lookups, and `IsOwner` use `StringComparison.Ordinal`. Hydrate permissions and attributes with consistent casing.
- **Permissions snapshot to frozen collections.** Mutating a collection passed into `Actor` after construction has no effect; the actor exposes a `FrozenSet<string>` / `FrozenDictionary<string, string>` snapshot for O(1) lookups.
- **Scoped permissions** use the `"Permission:Scope"` convention (`Document.Edit:Tenant_A`). Add scoped entries directly to `Permissions` and check via `HasPermission(string, string)` — no separate scope collection.
- **Pipeline ordering.** When a command implements both `IAuthorize` (static) and `IAuthorizeResource<TResource>` (resource), the mediator behavior runs static checks first; resource loading and `Authorize(actor, resource)` only execute if the static check passes. A loader failure short-circuits before `Authorize` is called.

## Code examples

### Static permission authorization

```csharp
using Trellis;
using Trellis.Authorization;

public sealed record DeleteOrderCommand(string OrderId) : ICommand<Result>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions { get; } = ["orders:delete"];
}
```

### Resource-based authorization with a shared loader

```csharp
using Trellis;
using Trellis.Authorization;

public sealed record Order(string Id, string OwnerId);

public sealed record CancelOrderCommand(string OrderId)
    : ICommand<Result>, IAuthorizeResource<Order>, IIdentifyResource<Order, string>
{
    public string GetResourceId() => OrderId;

    public IResult Authorize(Actor actor, Order order) =>
        Result.Ensure(
            actor.IsOwner(order.OwnerId) || actor.HasPermission("orders:cancel-any"),
            new Error.Forbidden("orders.cancel")
                { Detail = "Only the owner can cancel this order." });
}

public sealed class OrderResourceLoader(IOrderRepository repo)
    : SharedResourceLoaderById<Order, string>
{
    public override Task<Result<Order>> GetByIdAsync(string id, CancellationToken ct) =>
        repo.GetByIdAsync(id, ct);
}
```

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
```

## Cross-references

- [trellis-api-asp.md](trellis-api-asp.md) — `Trellis.Asp.Authorization` actor providers (`ClaimsActorProvider`, `EntraActorProvider`, `DevelopmentActorProvider`, `CachingActorProvider`) and the matching `AddClaimsActorProvider` / `AddEntraActorProvider` / `AddDevelopmentActorProvider` / `AddCachingActorProvider<T>` registration helpers.
- [trellis-api-mediator.md](trellis-api-mediator.md) — `AuthorizationBehavior<TMessage, TResponse>` pipeline behavior.
- [trellis-api-core.md](trellis-api-core.md) — `Result`, `Error.Forbidden`, `Error.NotFound`.
- [trellis-api-testing-aspnetcore.md](trellis-api-testing-aspnetcore.md) — `WebApplicationFactoryExtensions.CreateClientWithActor` (writes the `X-Test-Actor` header consumed by `DevelopmentActorProvider`).
