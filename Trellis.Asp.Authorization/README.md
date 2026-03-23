# Trellis.Asp.Authorization

ASP.NET Core actor providers for [Trellis](https://github.com/xavierjohn/Trellis). Provides two `IActorProvider` implementations:

- **`EntraActorProvider`** — Production use. Maps Azure Entra ID v2.0 JWT claims to `Actor` with permissions, forbidden permissions, and ABAC attributes.
- **`DevelopmentActorProvider`** — Development and testing. Reads `Actor` from the `X-Test-Actor` HTTP header with a production environment guard.

## Why a Separate Package?

`Trellis.Authorization` defines the `Actor`, `IActorProvider`, and authorization interfaces with zero framework dependencies. This package provides the ASP.NET Core integration — `IActorProvider` implementations that hydrate `Actor` from HTTP request context.

Keeping them separate means:

- `Trellis.Authorization` can be used in domain/application layers without pulling in ASP.NET Core
- The HTTP-specific actor resolution is isolated in the API layer where it belongs
- Teams using a different identity provider can implement their own `IActorProvider` without this package

## Installation

```
dotnet add package Trellis.Asp.Authorization
```

This transitively references `Trellis.Authorization` — no need to install both.

## Quick Start

```csharp
using Trellis.Asp.Authorization;

// Conditional registration — use the right provider for each environment
if (builder.Environment.IsDevelopment())
{
    // Reads Actor from X-Test-Actor header; falls back to default actor
    builder.Services.AddDevelopmentActorProvider();
}
else
{
    // Reads Actor from Entra ID JWT claims
    builder.Services.AddEntraActorProvider();
}
```

## DevelopmentActorProvider

Reads actor identity from the `X-Test-Actor` HTTP header. **Throws `InvalidOperationException` in production** if the header is present — preventing accidental deployment of the test bypass.

```csharp
builder.Services.AddDevelopmentActorProvider(options =>
{
    options.DefaultActorId = "admin";
    options.DefaultPermissions = new HashSet<string> { "orders:create", "orders:read" };
});
```

The header JSON schema matches `CreateClientWithActor` from `Trellis.Testing`:

```json
{
  "Id": "user-1",
  "Permissions": ["orders:create", "orders:read"],
  "ForbiddenPermissions": [],
  "Attributes": { "tid": "tenant-1" }
}
```

## EntraActorProvider

Maps Azure Entra ID v2.0 JWT claims to `Actor`.

```csharp
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
| `EntraActorProvider` | Production — maps Entra JWT claims to `Actor` |
| `EntraActorOptions` | Configuration for Entra claim mapping |
| `DevelopmentActorProvider` | Development/testing — reads `X-Test-Actor` header with production guard |
| `DevelopmentActorOptions` | Configuration for default actor and error handling |
| `ServiceCollectionExtensions` | `AddEntraActorProvider()` and `AddDevelopmentActorProvider()` DI registration |

## Package References

| Layer | Package |
|-------|---------|
| Domain/Application | `Trellis.Authorization` (auth types only) |
| API/Host | `Trellis.Asp.Authorization` (this package) |
| CQRS Pipeline | `Trellis.Mediator` (uses `IActorProvider` via DI) |

See the [full documentation](https://xavierjohn.github.io/Trellis/articles/integration-asp-authorization.html) for details.
