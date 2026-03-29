# ASP.NET Core Authorization (Entra ID)

**Level:** Intermediate | **Time:** 15-20 min | **Prerequisites:** [Basics](basics.md), [Trellis.Authorization](https://github.com/xavierjohn/Trellis/tree/main/Trellis.Authorization)

Map Azure Entra ID v2.0 JWT claims to `Actor` using the **Trellis.Asp.Authorization** package. This package provides `EntraActorProvider`, an `IActorProvider` implementation that hydrates an `Actor` from `HttpContext.User` with permissions, forbidden permissions, and ABAC attributes.

> **Note:** This package assumes authentication middleware (e.g., `AddMicrosoftIdentityWebApi`) has already been configured. It handles **claim-to-Actor mapping**, not authentication itself.

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [Default Claim Mapping](#default-claim-mapping)
- [Customizing Mappings](#customizing-mappings)
- [ActorAttributes Constants](#actorattributes-constants)
- [Integration with Trellis.Mediator](#integration-with-trellismediator)
- [Caching Actor Provider](#caching-actor-provider)
- [Best Practices](#best-practices)

## Installation

```bash
dotnet add package Trellis.Asp.Authorization
```

This package transitively references `Trellis.Authorization` — no need to install both.

## Quick Start

```csharp
using Trellis.Asp.Authorization;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure authentication (e.g., Microsoft Identity)
builder.Services.AddAuthentication().AddJwtBearer();

// 2. Register the Entra actor provider
builder.Services.AddEntraActorProvider();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.Run();
```

That's it. `IActorProvider` is now available via DI, and `Actor` is automatically hydrated from the authenticated user's JWT claims on every request.

## Default Claim Mapping

| Actor Property | Source | Details |
|---------------|--------|---------|
| `Id` | `oid` claim | Object identifier from Entra ID |
| `Permissions` | `roles` claims | App role assignments |
| `ForbiddenPermissions` | *(empty)* | Override via `MapForbiddenPermissions` |
| `Attributes["tid"]` | `tid` claim | Tenant identifier |
| `Attributes["preferred_username"]` | `preferred_username` claim | User's display email/UPN |
| `Attributes["azp"]` | `azp` claim | Authorized party (client app ID) |
| `Attributes["azpacr"]` | `azpacr` claim | Client authentication method |
| `Attributes["acrs"]` | `acrs` claim | Auth context class reference |
| `Attributes["ip_address"]` | `HttpContext.Connection.RemoteIpAddress` | Client IP |
| `Attributes["mfa"]` | Derived from `amr` claim | `"true"` if `amr` contains `"mfa"` in any casing |

## Customizing Mappings

### Custom Permission Mapping

Flatten JWT roles into granular permissions:

```csharp
builder.Services.AddEntraActorProvider(options =>
{
    options.MapPermissions = claims => claims
        .Where(c => c.Type == "roles")
        .SelectMany(role => RolePermissionMap[role.Value])
        .ToHashSet();
});
```

### Scope-Based Permissions

Map delegated permissions from the `scp` claim:

```csharp
builder.Services.AddEntraActorProvider(options =>
{
    options.MapPermissions = claims => claims
        .Where(c => c.Type == "scp")
        .SelectMany(c => c.Value.Split(' '))
        .ToHashSet();
});
```

### Custom ID Claim

Use `sub` instead of `oid`:

```csharp
builder.Services.AddEntraActorProvider(options =>
{
    options.IdClaimType = "sub";
});
```

### Forbidden Permissions

Populate the deny list from an external source:

```csharp
builder.Services.AddEntraActorProvider(options =>
{
    options.MapForbiddenPermissions = claims => claims
        .Where(c => c.Type == "denied_permissions")
        .Select(c => c.Value)
        .ToHashSet();
});
```

### Custom Attributes

Add application-specific ABAC attributes:

```csharp
builder.Services.AddEntraActorProvider(options =>
{
    options.MapAttributes = (claims, httpContext) =>
    {
        var attrs = new Dictionary<string, string>();
        var region = claims.FirstOrDefault(c => c.Type == "region");
        if (region is not null)
            attrs["region"] = region.Value;
        return attrs;
    };
});
```

### Mapper Failure Behavior

Custom mapping delegates run during actor hydration on every request. If `MapPermissions`, `MapForbiddenPermissions`, or `MapAttributes` throws, `EntraActorProvider` wraps the original exception in an `InvalidOperationException` that identifies which mapping delegate failed. This makes misconfiguration easier to diagnose in logs and exception telemetry.

```csharp
builder.Services.AddEntraActorProvider(options =>
{
    options.MapPermissions = claims =>
    {
        // If this throws, the provider wraps it with EntraActorOptions.MapPermissions context
        return claims
            .Where(c => c.Type == "roles")
            .Select(c => c.Value)
            .ToHashSet();
    };
});
```

## ActorAttributes Constants

Use `ActorAttributes` constants for well-known attribute keys to avoid typos:

```csharp
using Trellis.Authorization;

if (actor.GetAttribute(ActorAttributes.MfaAuthenticated) == "true")
{
    // Allow sensitive operation
}

var tenantId = actor.GetAttribute(ActorAttributes.TenantId);
```

| Constant | Value | Source |
|----------|-------|--------|
| `TenantId` | `"tid"` | Entra `tid` claim |
| `PreferredUsername` | `"preferred_username"` | Entra `preferred_username` claim |
| `AuthorizedParty` | `"azp"` | Entra `azp` claim |
| `AuthorizedPartyAcr` | `"azpacr"` | Entra `azpacr` claim |
| `AuthContextClassReference` | `"acrs"` | Entra `acrs` claim |
| `IpAddress` | `"ip_address"` | `HttpContext.Connection.RemoteIpAddress` |
| `MfaAuthenticated` | `"mfa"` | Derived from `amr` claim, case-insensitive on the claim value |

## Integration with Trellis.Mediator

When using `Trellis.Mediator`, the `EntraActorProvider` is automatically used by authorization pipeline behaviors:

```csharp
// Program.cs
builder.Services.AddEntraActorProvider();
builder.Services.AddMediator(options =>
{
    options.PipelineBehaviors = [.. ServiceCollectionExtensions.PipelineBehaviors];
});

// Command with authorization
public sealed record DeleteDocumentCommand(string DocumentId)
    : ICommand<Result<Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["Documents.Delete"];
}
```

The `AuthorizationBehavior` resolves `IActorProvider` → `EntraActorProvider` → `Actor` from JWT claims → permission check, all automatically.

## Caching Actor Provider

When resolving an `Actor` requires asynchronous work — such as loading permissions from a database or calling an external authorization service — implement `IActorProvider` and use `AddCachingActorProvider<T>()` to wrap it with per-request caching.

### Implementation

```csharp
using Trellis.Authorization;

public class DbActorProvider(
    IHttpContextAccessor httpContextAccessor,
    IPermissionStore store) : IActorProvider
{
    public async Task<Actor> GetCurrentActorAsync(CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var permissions = await store.GetPermissionsAsync(userId, ct).ConfigureAwait(false);
        return Actor.Create(userId, permissions);
    }
}

// DI registration — wraps DbActorProvider with CachingActorProvider
services.AddCachingActorProvider<DbActorProvider>();
```

### Behavior with Trellis.Mediator

`IActorProvider` has a single async method `GetCurrentActorAsync()`. The `AuthorizationBehavior` calls this method directly. When you need to layer additional permission resolution (e.g., database lookups) on top of claim-based mapping, implement a custom `IActorProvider` and register it with `AddCachingActorProvider<T>()` to avoid redundant async calls within the same request.

## Best Practices

1. **Pre-flatten permissions** — Use `MapPermissions` to convert coarse JWT roles into granular permissions at hydration time, keeping all `HasPermission` checks O(1).
2. **Use `ActorAttributes` constants** — Avoid raw strings for attribute keys.
3. **Don't use `preferred_username` for authorization** — It can change when a user is renamed. Use it for display/audit only.
4. **Consider `azpacr` for sensitive operations** — Require certificate-based client auth (`"2"`) for admin endpoints.
5. **Leverage `acrs` for step-up auth** — Enforce Conditional Access authentication context for high-risk operations.
6. **Use `AddCachingActorProvider<T>()` for DB-backed permissions** — Wraps your provider with per-request caching and registers as scoped to match the request lifetime.
