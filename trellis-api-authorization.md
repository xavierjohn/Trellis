# Trellis.Authorization — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-mediator.md, trellis-api-asp.md, trellis-api-testing.md.

**Packages:** `Trellis.Authorization` + `Trellis.Asp.Authorization` | **Namespaces:** `Trellis.Authorization`, `Trellis.Asp.Authorization`

---

## Trellis.Authorization

**Namespace: `Trellis.Authorization`**

## Actor (sealed record)

Represents the authenticated user making the current request. Contains identity, permissions, forbidden permissions, and contextual attributes. Hydrated during authentication middleware. Used by authorization behaviors to check permissions and resource ownership.

```csharp
Actor(string Id, IReadOnlySet<string> Permissions, IReadOnlySet<string> ForbiddenPermissions, IReadOnlyDictionary<string, string> Attributes)

static Actor Create(string id, IReadOnlySet<string> permissions)
bool HasPermission(string permission)
bool HasPermission(string permission, string scope)     // checks "permission:scope"
bool HasAllPermissions(IEnumerable<string> permissions)
bool HasAnyPermission(IEnumerable<string> permissions)
bool IsOwner(string resourceOwnerId)
bool HasAttribute(string key)
string? GetAttribute(string key)
```

## Interfaces

- **`IActorProvider`** — Provides the current authenticated actor asynchronously. Unified interface for all actor resolution (JWT claims, database lookups, etc.).
- **`IAuthorize`** — Declares required permissions; checked by authorization pipeline behavior.
- **`IAuthorizeResource<TResource>`** — Resource-based authorization; receives the loaded resource and actor.
- **`IResourceLoader<TMessage, TResource>`** — Loads the resource for resource-based authorization checks.
- **`ResourceLoaderById<TMessage, TResource, TId>`** — Base class for the common "extract ID from message, load by ID" pattern.

```csharp
interface IActorProvider { Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default); }
interface IAuthorize { IReadOnlyList<string> RequiredPermissions { get; } }
interface IAuthorizeResource<TResource> { IResult Authorize(Actor actor, TResource resource); }
interface IResourceLoader<TMessage, TResource> { Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken); }
abstract class ResourceLoaderById<TMessage, TResource, TId> : IResourceLoader<TMessage, TResource>
{
    protected abstract TId GetId(TMessage message);
    protected abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken);
}
```

## ActorAttributes Constants

Well-known attribute keys for ABAC (Attribute-Based Access Control): `IpAddress`, `MfaAuthenticated`, `TenantId`, `PreferredUsername`, etc.

```csharp
const string TenantId = "tid";
const string PreferredUsername = "preferred_username";
const string AuthorizedParty = "azp";
const string AuthorizedPartyAcr = "azpacr";
const string AuthContextClassReference = "acrs";
const string IpAddress = "ip_address";
const string MfaAuthenticated = "mfa";
```

---

## Trellis.Asp.Authorization — Actor Providers

**Namespace: `Trellis.Asp.Authorization`**

## ClaimsActorProvider (Generic OIDC/JWT)

Generic actor provider that maps standard OIDC/JWT claims to an `Actor`. Works with any identity provider (Auth0, Keycloak, Okta, Entra, etc.).

```csharp
// Registration
services.AddClaimsActorProvider();
services.AddClaimsActorProvider(options => {
    options.ActorIdClaim = "sub";           // default: "sub"
    options.PermissionsClaim = "permissions"; // default: "permissions"
});

// ClaimsActorProvider : IActorProvider
// Extracts Actor from HttpContext claims using configurable claim names
```

## EntraActorProvider (Production)

Production actor provider that maps Microsoft Entra ID (Azure AD) JWT claims to an `Actor`. Extends `ClaimsActorProvider` with Entra-specific claim mapping for permissions, forbidden permissions, and ABAC attributes.

```csharp
// Registration
services.AddEntraActorProvider();
services.AddEntraActorProvider(options => {
    options.IdClaimType = "sub";
    options.MapPermissions = claims => /* custom extraction */;
});

// EntraActorProvider : ClaimsActorProvider
// Extracts Actor from HttpContext claims (Entra ID / Azure AD)
```

## DevelopmentActorProvider (Development/Testing)

Development/testing actor provider that reads `Actor` from the `X-Test-Actor` HTTP header (JSON). Falls back to a configurable default actor. **Throws unconditionally in any non-Development environment** to prevent accidental use in Production.

```csharp
// Registration — for development environments
services.AddDevelopmentActorProvider();
services.AddDevelopmentActorProvider(options => {
    options.DefaultActorId = "admin";
    options.DefaultPermissions = new HashSet<string> { "orders:create", "orders:read" };
    options.ThrowOnMalformedHeader = false; // default
});

// DevelopmentActorProvider : IActorProvider
// Throws InvalidOperationException unconditionally in non-Development environments
// Reads Actor from X-Test-Actor HTTP header (JSON) in Development
// Falls back to configurable default Actor when header absent
// Header JSON schema: { "Id": "...", "Permissions": [...], "ForbiddenPermissions": [...], "Attributes": {...} }

// Conditional registration pattern:
if (builder.Environment.IsDevelopment())
    services.AddDevelopmentActorProvider();
else
    services.AddEntraActorProvider();
```

## CachingActorProvider (Decorator)

Caching decorator that wraps any `IActorProvider` and caches the result per-scope. Use with database-backed providers to avoid redundant queries within a single request.

```csharp
// Registration — wraps DatabaseActorProvider with per-request caching
services.AddCachingActorProvider<DatabaseActorProvider>();

// CachingActorProvider : IActorProvider
// Caches the Task<Actor> from the first call; subsequent calls return the same result
```

| Type | Purpose |
|------|---------|
| `ClaimsActorProvider` | Generic OIDC/JWT — maps configurable claims to `Actor` |
| `ClaimsActorOptions` | Configuration for generic claim mapping (`ActorIdClaim`, `PermissionsClaim`) |
| `EntraActorProvider` | Production — maps Entra JWT claims to `Actor` (extends `ClaimsActorProvider`) |
| `EntraActorOptions` | Configuration for Entra claim mapping |
| `DevelopmentActorProvider` | Development/testing — reads `X-Test-Actor` header |
| `DevelopmentActorOptions` | Configuration for default actor and error handling |
| `CachingActorProvider` | Decorator — caches inner provider per-scope |
| `ServiceCollectionExtensions` | `AddClaimsActorProvider()`, `AddEntraActorProvider()`, `AddDevelopmentActorProvider()`, `AddCachingActorProvider<T>()` |

### ClaimsActorOptions

Configures how `ClaimsActorProvider` extracts actor identity from JWT claims.

```csharp
public class ClaimsActorOptions
{
    string ActorIdClaim { get; set; }     // default: "sub"
    string PermissionsClaim { get; set; } // default: "permissions"
}
```

### EntraActorOptions

Configures how `EntraActorProvider` extracts actor identity from JWT claims.

```csharp
public sealed class EntraActorOptions
{
    string IdClaimType { get; set; }  // default: "http://schemas.microsoft.com/identity/claims/objectidentifier"
    Func<IEnumerable<Claim>, IReadOnlySet<string>> MapPermissions { get; set; }  // default: reads "roles" claims
    Func<IEnumerable<Claim>, IReadOnlySet<string>> MapForbiddenPermissions { get; set; }  // default: empty set
    Func<IEnumerable<Claim>, HttpContext, IReadOnlyDictionary<string, string>> MapAttributes { get; set; }  // default: tid, preferred_username, azp, IP, MFA
}

// Usage
services.AddEntraActorProvider(options =>
{
    options.IdClaimType = "sub";
    options.MapPermissions = claims => claims.Where(c => c.Type == "scope").Select(c => c.Value).ToHashSet();
});
```

### DevelopmentActorOptions

Configures the `DevelopmentActorProvider` used during local development. Reads the `X-Test-Actor` header as JSON.

```csharp
public sealed class DevelopmentActorOptions
{
    string DefaultActorId { get; set; }  // default: "development"
    IReadOnlySet<string> DefaultPermissions { get; set; }  // default: empty
    bool ThrowOnMalformedHeader { get; set; }  // default: false
}
```

---
