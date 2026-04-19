# Trellis.Authorization / Trellis.Asp.Authorization — API Reference

**Packages:** `Trellis.Authorization`, `Trellis.Asp.Authorization`
**Namespaces:** `Trellis.Authorization`, `Trellis.Asp.Authorization`
**Purpose:** Defines actor/resource authorization primitives plus ASP.NET Core actor providers and DI registration helpers.

## Types

### Actor
**Declaration**

```csharp
public sealed record Actor
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public Actor(string id, IReadOnlySet<string> permissions, IReadOnlySet<string> forbiddenPermissions, IReadOnlyDictionary<string, string> attributes)` | Creates an actor and snapshots permissions, forbidden permissions, and attributes using frozen collections. |

**Fields**

| Name | Type | Description |
| --- | --- | --- |
| `PermissionScopeSeparator` | `char` | Public constant separator used when building scoped permissions. Value: `':'`. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `Id` | `string` | Actor identifier. |
| `Permissions` | `IReadOnlySet<string>` | Granted permissions; lookups are case-sensitive. |
| `ForbiddenPermissions` | `IReadOnlySet<string>` | Explicit deny-list; deny overrides allow. |
| `Attributes` | `IReadOnlyDictionary<string, string>` | Attribute-based access control data. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Actor Create(string id, IReadOnlySet<string> permissions)` | `Actor` | Creates an actor with empty forbidden permissions and empty attributes. |
| `public bool HasPermission(string permission)` | `bool` | Returns `true` only when the permission is present and not forbidden. |
| `public bool HasPermission(string permission, string scope)` | `bool` | Checks the scoped permission string `${permission}:${scope}` using `PermissionScopeSeparator`. |
| `public bool HasAllPermissions(IEnumerable<string> permissions)` | `bool` | Returns `true` only when every permission passes `HasPermission`. |
| `public bool HasAnyPermission(IEnumerable<string> permissions)` | `bool` | Returns `true` when at least one permission passes `HasPermission`. |
| `public bool IsOwner(string resourceOwnerId)` | `bool` | Compares `Id` and `resourceOwnerId` with ordinal equality. |
| `public bool HasAttribute(string key)` | `bool` | Returns `true` when `Attributes` contains `key`. |
| `public string? GetAttribute(string key)` | `string?` | Returns the attribute value or `null`. |

### ActorAttributes
**Declaration**

```csharp
public static class ActorAttributes
```

**Constructors**

No public constructors.

**Fields**

| Name | Type | Description |
| --- | --- | --- |
| `TenantId` | `string` | `tid` claim / tenant ID. |
| `PreferredUsername` | `string` | `preferred_username` claim. |
| `AuthorizedParty` | `string` | `azp` claim. |
| `AuthorizedPartyAcr` | `string` | `azpacr` claim. |
| `AuthContextClassReference` | `string` | `acrs` claim. |
| `IpAddress` | `string` | Request IP address attribute key. |
| `MfaAuthenticated` | `string` | MFA-derived attribute key. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `—` | `—` | None. |

### IActorProvider
**Declaration**

```csharp
public interface IActorProvider
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
| `Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Returns the current actor. Implementations may throw when the request is unauthenticated or the actor cannot be resolved. |

### IAuthorize
**Declaration**

```csharp
public interface IAuthorize
```

**Constructors**

No public constructors.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `RequiredPermissions` | `IReadOnlyList<string>` | Static permission list enforced by `AuthorizationBehavior<TMessage, TResponse>`. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `—` | `—` | None. |

### IAuthorizeResource<TResource>
**Declaration**

```csharp
public interface IAuthorizeResource<in TResource>
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
| `IResult Authorize(Actor actor, TResource resource)` | `IResult` | Performs resource-based authorization after the loader succeeds. |

### IIdentifyResource<TResource, TId>
**Declaration**

```csharp
public interface IIdentifyResource<TResource, out TId>
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
| `TId GetResourceId()` | `TId` | Extracts the typed resource ID from the message. |

### IResourceLoader<TMessage, TResource>
**Declaration**

```csharp
public interface IResourceLoader<in TMessage, TResource>
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
| `Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Loads the resource used by resource authorization. |

### ResourceLoaderById<TMessage, TResource, TId>
**Declaration**

```csharp
public abstract class ResourceLoaderById<TMessage, TResource, TId> : IResourceLoader<TMessage, TResource>
```

**Constructors**

No public constructors declared in source.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `protected abstract TId GetId(TMessage message)` | `TId` | Extracts the resource ID from the message. |
| `protected abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Loads the resource by ID. |
| `public Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Calls `GetId(message)` and then `GetByIdAsync(...)`. |

### SharedResourceLoaderById<TResource, TId>
**Declaration**

```csharp
public abstract class SharedResourceLoaderById<TResource, TId>
```

**Constructors**

No public constructors declared in source.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken)` | `Task<Result<TResource>>` | Shared resource lookup used by `SharedResourceLoaderAdapter<TMessage, TResource, TId>`. |

### ClaimsActorOptions
**Declaration**

```csharp
public class ClaimsActorOptions
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public ClaimsActorOptions()` | Implicit parameterless constructor. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `ActorIdClaim` | `string` | Claim type used for `Actor.Id`. Default: `"sub"`. |
| `PermissionsClaim` | `string` | Claim type used for permissions. Default: `"permissions"`. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `—` | `—` | None. |

### ClaimsActorProvider
**Declaration**

```csharp
public class ClaimsActorProvider(IHttpContextAccessor httpContextAccessor, IOptions<ClaimsActorOptions> options) : IActorProvider
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public ClaimsActorProvider(IHttpContextAccessor httpContextAccessor, IOptions<ClaimsActorOptions> options)` | Builds a provider that reads the current authenticated `ClaimsIdentity`. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `HttpContextAccessor` | `IHttpContextAccessor` | Protected accessor exposed to derived providers. |
| `Options` | `ClaimsActorOptions` | Protected mapped options value. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public virtual Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Throws `InvalidOperationException` when `HttpContext` is missing, no authenticated identity exists, or the configured actor ID claim is missing. Maps permissions from `Options.PermissionsClaim`. |

### DevelopmentActorOptions
**Declaration**

```csharp
public sealed class DevelopmentActorOptions
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public DevelopmentActorOptions()` | Implicit parameterless constructor. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `DefaultActorId` | `string` | Default fallback actor ID. Default: `"development"`. |
| `DefaultPermissions` | `IReadOnlySet<string>` | Default fallback permissions when no header is supplied. Default: empty `HashSet<string>`. |
| `ThrowOnMalformedHeader` | `bool` | When `true`, malformed `X-Test-Actor` JSON throws instead of falling back. Default: `false`. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `—` | `—` | None. |

### DevelopmentActorProvider
**Declaration**

```csharp
public sealed partial class DevelopmentActorProvider(IHttpContextAccessor httpContextAccessor, IHostEnvironment hostEnvironment, IOptions<DevelopmentActorOptions> options, ILogger<DevelopmentActorProvider> logger) : IActorProvider
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public DevelopmentActorProvider(IHttpContextAccessor httpContextAccessor, IHostEnvironment hostEnvironment, IOptions<DevelopmentActorOptions> options, ILogger<DevelopmentActorProvider> logger)` | Builds the development/test header-based actor provider. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Throws `InvalidOperationException` whenever `!hostEnvironment.IsDevelopment()`, even if the header is absent. In Development, returns the default actor when `HttpContext` is null or the `X-Test-Actor` header is missing; malformed JSON throws only when `ThrowOnMalformedHeader` is `true`. |

### EntraActorOptions
**Declaration**

```csharp
public sealed class EntraActorOptions
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public EntraActorOptions()` | Implicit parameterless constructor. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `IdClaimType` | `string` | Claim type used for actor ID. Default: `"http://schemas.microsoft.com/identity/claims/objectidentifier"`. |
| `MapPermissions` | `Func<IEnumerable<System.Security.Claims.Claim>, IReadOnlySet<string>>` | Default mapping returns values from both `"roles"` and `ClaimTypes.Role` claims. |
| `MapForbiddenPermissions` | `Func<IEnumerable<System.Security.Claims.Claim>, IReadOnlySet<string>>` | Default mapping returns an empty `HashSet<string>`. |
| `MapAttributes` | `Func<IEnumerable<System.Security.Claims.Claim>, Microsoft.AspNetCore.Http.HttpContext, IReadOnlyDictionary<string, string>>` | Default mapping includes `tid`, `preferred_username`, `azp`, `azpacr`, `acrs`, request IP address, and MFA state from `amr == "mfa"`. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `—` | `—` | None. |

### EntraActorProvider
**Declaration**

```csharp
public sealed class EntraActorProvider : ClaimsActorProvider
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public EntraActorProvider(IHttpContextAccessor httpContextAccessor, IOptions<EntraActorOptions> options)` | Builds the Entra-specific provider and passes `ActorIdClaim = options.Value.IdClaimType` plus `PermissionsClaim = "roles"` to the base provider. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public override Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Throws the same missing-context / missing-authentication failures as `ClaimsActorProvider`. When `IdClaimType` is the long objectidentifier claim, it falls back to the short `"oid"` claim before failing. Wraps exceptions thrown by `MapPermissions`, `MapForbiddenPermissions`, and `MapAttributes` in `InvalidOperationException`. |

### CachingActorProvider
**Declaration**

```csharp
public sealed class CachingActorProvider : IActorProvider
```

**Constructors**

| Signature | Description |
| --- | --- |
| `public CachingActorProvider(IActorProvider inner, Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)` | Wraps another provider and caches the first request-scoped resolution task. |

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Reuses one cached task per scope; applies caller cancellation only to awaiting, not to the shared inner task unless it matches `RequestAborted`. |

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
| `—` | `—` | None. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddClaimsActorProvider(this IServiceCollection services, Action<ClaimsActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, configures `ClaimsActorOptions`, and registers `IActorProvider` as `ClaimsActorProvider`. |
| `public static IServiceCollection AddEntraActorProvider(this IServiceCollection services, Action<EntraActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, configures `EntraActorOptions`, and registers `IActorProvider` as `EntraActorProvider`. |
| `public static IServiceCollection AddDevelopmentActorProvider(this IServiceCollection services, Action<DevelopmentActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, logging, configures `DevelopmentActorOptions`, and registers `IActorProvider` as `DevelopmentActorProvider`. The provider itself throws outside Development regardless of header presence. |
| `public static IServiceCollection AddCachingActorProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services) where T : class, IActorProvider` | `IServiceCollection` | Registers concrete provider `T`, then wraps it with `CachingActorProvider` as the scoped `IActorProvider`. |

## Extension methods

### Trellis.Asp.Authorization.ServiceCollectionExtensions

```csharp
public static IServiceCollection AddClaimsActorProvider(this IServiceCollection services, Action<ClaimsActorOptions>? configure = null)
public static IServiceCollection AddEntraActorProvider(this IServiceCollection services, Action<EntraActorOptions>? configure = null)
public static IServiceCollection AddDevelopmentActorProvider(this IServiceCollection services, Action<DevelopmentActorOptions>? configure = null)
public static IServiceCollection AddCachingActorProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services) where T : class, IActorProvider
```

## Interfaces

```csharp
public interface IActorProvider
public interface IAuthorize
public interface IAuthorizeResource<in TResource>
public interface IIdentifyResource<TResource, out TId>
public interface IResourceLoader<in TMessage, TResource>
```

## Code examples

### Actor checks and ID-based resource loading

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Authorization;

var actor = new Actor(
    id: "user-1",
    permissions: new HashSet<string> { "orders:cancel", $"orders:view{Actor.PermissionScopeSeparator}tenant-1" },
    forbiddenPermissions: new HashSet<string>(),
    attributes: new Dictionary<string, string>
    {
        [ActorAttributes.TenantId] = "tenant-1",
        [ActorAttributes.MfaAuthenticated] = "true"
    });

Console.WriteLine(actor.HasPermission("orders:cancel"));
Console.WriteLine(actor.HasPermission("orders:view", "tenant-1"));

IResourceLoader<CancelOrderCommand, Order> loader = new CancelOrderLoader();
var loadResult = await loader.LoadAsync(new CancelOrderCommand("order-1"), CancellationToken.None);

if (loadResult.IsSuccess)
{
    var authResult = new CancelOrderCommand(loadResult.Value.Id).Authorize(actor, loadResult.Value);
    Console.WriteLine(authResult.IsSuccess);
}

public sealed record Order(string Id, string OwnerId);

public sealed record CancelOrderCommand(string OrderId) : IAuthorizeResource<Order>
{
    public IResult Authorize(Actor actor, Order resource) =>
        actor.IsOwner(resource.OwnerId)
            ? Result.Ok()
            : Result.Fail(Error.Forbidden("Only the owner can cancel the order."));
}

public sealed class CancelOrderLoader : ResourceLoaderById<CancelOrderCommand, Order, string>
{
    protected override string GetId(CancelOrderCommand message) => message.OrderId;

    protected override Task<Result<Order>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Ok(new Order(id, "user-1")));
}
```

### ASP.NET Core registration

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

var services = new ServiceCollection();

services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim = "sub";
    options.PermissionsClaim = "permissions";
});

services.AddEntraActorProvider(options =>
{
    options.MapPermissions = claims => claims
        .Where(c => string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(c.Type, System.Security.Claims.ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
        .Select(c => c.Value)
        .ToHashSet();
});

services.AddDevelopmentActorProvider(options =>
{
    options.DefaultActorId = "development";
    options.DefaultPermissions = new HashSet<string> { "orders:read" };
});
```

## Cross-references

- [trellis-api-mediator.md](trellis-api-mediator.md)
- [trellis-api-results.md](trellis-api-results.md)
- [trellis-api-asp.md](trellis-api-asp.md)
- [trellis-api-testing-reference.md](trellis-api-testing-reference.md)
