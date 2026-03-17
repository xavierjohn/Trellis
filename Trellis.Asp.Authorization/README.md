# Trellis.Asp.Authorization

Azure Entra ID v2.0 actor provider for [Trellis](https://github.com/xavierjohn/Trellis). Maps JWT claims to `Actor` with permissions, forbidden permissions, and ABAC attributes.

## Why a Separate Package?

`Trellis.Authorization` defines the `Actor`, `IActorProvider`, and authorization interfaces with zero framework dependencies. This package provides the ASP.NET Core integration — specifically, an `IActorProvider` implementation that hydrates `Actor` from `HttpContext.User` using Azure Entra ID v2.0 JWT claims.

Keeping them separate means:

- `Trellis.Authorization` can be used in domain/application layers without pulling in ASP.NET Core
- The Entra-specific mapping logic is isolated in the API layer where it belongs
- Teams using a different identity provider can implement their own `IActorProvider` without this package

## Installation

```
dotnet add package Trellis.Asp.Authorization
```

This transitively references `Trellis.Authorization` — no need to install both.

## Quick Start

```csharp
using Trellis.Asp.Authorization;

// Register with default Entra v2.0 claim mappings
builder.Services.AddEntraActorProvider();
```

## Default Claim Mapping

| Actor Property | Source |
|---------------|--------|
| `Id` | `oid` claim |
| `Permissions` | `roles` claims |
| `ForbiddenPermissions` | Empty (override to populate) |
| `Attributes` | `tid`, `preferred_username`, `azp`, `azpacr`, `acrs`, `ip_address`, `mfa` |

`mfa` is derived from the `amr` claim and treats `mfa`, `Mfa`, and `MFA` equivalently.

## Customization

Override any mapping delegate via `EntraActorOptions`:

```csharp
builder.Services.AddEntraActorProvider(options =>
{
    // Flatten roles into granular permissions
    options.MapPermissions = claims => claims
        .Where(c => c.Type == "roles")
        .SelectMany(role => RolePermissionMap[role.Value])
        .ToHashSet();

    // Use sub instead of oid
    options.IdClaimType = "sub";

    // Populate deny list
    options.MapForbiddenPermissions = claims => claims
        .Where(c => c.Type == "denied_permissions")
        .Select(c => c.Value)
        .ToHashSet();
});
```

If a custom `MapPermissions`, `MapForbiddenPermissions`, or `MapAttributes` delegate throws, `EntraActorProvider` wraps the original exception with context identifying which delegate failed.

## Types

| Type | Purpose |
|------|---------|
| `EntraActorProvider` | `IActorProvider` that maps JWT claims to `Actor` |
| `EntraActorOptions` | Configuration for claim-to-Actor mapping |
| `ServiceCollectionExtensions` | `AddEntraActorProvider()` DI registration |

## Package References

| Layer | Package |
|-------|---------|
| Domain/Application | `Trellis.Authorization` (auth types only) |
| API/Host | `Trellis.Asp.Authorization` (this package) |
| CQRS Pipeline | `Trellis.Mediator` (uses `IActorProvider` via DI) |

See the [full documentation](https://xavierjohn.github.io/Trellis/articles/integration-asp-authorization.html) for details.
