# Trellis.Asp.Authorization

Azure Entra ID v2.0 actor provider for [Trellis](https://github.com/xavierjohn/Trellis).

Maps JWT claims from Azure Entra ID v2.0 tokens to `Actor` with permissions, forbidden permissions, and ABAC attributes. Integrates `Trellis.Authorization` with ASP.NET Core.

## Quick Start

```csharp
using Trellis.Asp.Authorization;

// Register with default Entra v2.0 claim mappings
builder.Services.AddEntraActorProvider();
```

Default mapping:

| Actor Property | Source |
|---------------|--------|
| `Id` | `oid` claim |
| `Permissions` | `roles` claims |
| `ForbiddenPermissions` | Empty (override to populate) |
| `Attributes` | `tid`, `preferred_username`, `azp`, `azpacr`, `acrs`, `ip_address`, `mfa` |

`mfa` is derived from the `amr` claim and treats `mfa`, `Mfa`, and `MFA` equivalently.

## Customization

```csharp
builder.Services.AddEntraActorProvider(options =>
{
    // Flatten roles into granular permissions
    options.MapPermissions = claims => claims
        .Where(c => c.Type == "roles")
        .SelectMany(role => RolePermissionMap[role.Value])
        .ToHashSet();

    // Populate deny list from a custom claim
    options.MapForbiddenPermissions = claims => claims
        .Where(c => c.Type == "denied_permissions")
        .Select(c => c.Value)
        .ToHashSet();
});
```

If a custom `MapPermissions`, `MapForbiddenPermissions`, or `MapAttributes` delegate throws, `EntraActorProvider` wraps the original exception with context identifying which delegate failed.

## Requirements

- .NET 10.0+
- ASP.NET Core authentication middleware (e.g., `AddMicrosoftIdentityWebApi`)

See the [full documentation](https://xavierjohn.github.io/Trellis/) for details.
