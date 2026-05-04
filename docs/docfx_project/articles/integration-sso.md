---
title: Single Sign-On Integration
package: Trellis.Asp
topics: [sso, entra, openid, jwt, claims, actor, multi-tenant]
related_api_reference: [trellis-api-asp.md, trellis-api-authorization.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Single Sign-On Integration

`Trellis.Asp.Authorization` (shipped inside the `Trellis.Asp` package) turns an authenticated `JwtBearer` principal from any OIDC issuer (Entra ID, Auth0, Okta, Google, Keycloak) into a frozen `Actor` so handlers and endpoints stop parsing JWTs.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Validate Entra ID v2.0 tokens and project them onto an `Actor` | `AddJwtBearer` + `AddEntraActorProvider(options?)` | [Entra ID provider](#entra-id-provider) |
| Validate any flat-claim OIDC token (Auth0, Okta, Google) | `AddJwtBearer` + `AddClaimsActorProvider(options?)` | [Generic OIDC provider](#generic-oidc-provider) |
| Project nested JSON claims (Keycloak `realm_access.roles`) | Subclass `ClaimsActorProvider` + `AddClaimsActorProvider(opts?)` for options + `AddCachingActorProvider<T>()` for the wrap | [Nested-claim providers](#nested-claim-providers) |
| Use `roles` for app permissions (Entra app roles) | Default `EntraActorOptions.MapPermissions` | [Claim mapping](#claim-mapping) |
| Use delegated scopes (`scp`) for permissions | Override `EntraActorOptions.MapPermissions` | [Scope and permission extraction](#scope-and-permission-extraction) |
| Accept multi-tenant Entra tokens and pin allowed tenants | `JwtBearerOptions.TokenValidationParameters` + read `ActorAttributes.TenantId` | [Multi-tenant Entra](#multi-tenant-entra) |
| Use a fake actor in Development without a real IdP | `AddDevelopmentActorProvider(options?)` + `X-Test-Actor` header | [Development defaults](#development-defaults) |
| Combine SSO with Trellis authorization rules | `IAuthorize` / `IAuthorizeResource<T>` via Mediator | [Composition](#composition) |

## Use this guide when

- You front a Trellis service with `JwtBearer` and need a single, predictable `Actor` shape regardless of which OIDC provider issued the token.
- You want one configuration story for Entra (app roles), Auth0/Okta (delegated scopes), Google (sign-in only), and Keycloak (nested role claims).
- You need a Development-only seam (`X-Test-Actor`) that fail-closes outside `IsDevelopment()`.
- You need to host a multi-tenant Entra app and pin which tenants may call your API.

## Surface at a glance

`Trellis.Asp.Authorization` (namespace inside the `Trellis.Asp` package) exposes one set of DI extensions and four `IActorProvider` implementations.

| API | Kind | Purpose |
|---|---|---|
| `AddEntraActorProvider(this IServiceCollection, Action<EntraActorOptions>?)` | DI extension | Scoped `IActorProvider` → `EntraActorProvider` (Entra v2.0 claim shape). |
| `AddClaimsActorProvider(this IServiceCollection, Action<ClaimsActorOptions>?)` | DI extension | Scoped `IActorProvider` → `ClaimsActorProvider` (configurable flat-claim mapping). |
| `AddDevelopmentActorProvider(this IServiceCollection, Action<DevelopmentActorOptions>?)` | DI extension | Scoped `IActorProvider` → `DevelopmentActorProvider`; reads `X-Test-Actor` JSON header; throws outside `IsDevelopment()`. |
| `AddCachingActorProvider<T>(this IServiceCollection)` where `T : class, IActorProvider` | DI extension | Wraps `T` in `CachingActorProvider` so a single resolution task is shared per request. |
| `EntraActorOptions` | Options | `IdClaimType`, `MapPermissions`, `MapForbiddenPermissions`, `MapAttributes`. |
| `ClaimsActorOptions` | Options | `ActorIdClaim` (default `"sub"`), `PermissionsClaim` (default `"permissions"`). Verbatim claim-type match. |
| `DevelopmentActorOptions` | Options | `DefaultActorId`, `DefaultPermissions`, `ThrowOnMalformedHeader`. |
| `ActorAttributes` (in `Trellis.Authorization`) | Constants | Well-known attribute keys: `TenantId`, `PreferredUsername`, `AuthorizedParty`, `AuthorizedPartyAcr`, `AuthContextClassReference`, `IpAddress`, `MfaAuthenticated`. |

Full signatures: [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md).

## Installation

```bash
dotnet add package Trellis.Asp
```

The actor providers ship in `Trellis.Asp` under namespace `Trellis.Asp.Authorization`. Domain primitives (`Actor`, `IActorProvider`, `ActorAttributes`) come from `Trellis.Authorization`. The legacy `Trellis.Asp.Authorization` package was absorbed into `Trellis.Asp`.

## Quick start

Validate Entra v2.0 tokens with the standard ASP.NET Core `JwtBearer` middleware, register `EntraActorProvider`, and read the resolved `Actor` from a protected endpoint.

```csharp
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

var builder = WebApplication.CreateBuilder(args);
var auth = builder.Configuration.GetSection("Authentication");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth["Authority"];
        options.Audience  = auth["Audience"];
    });

builder.Services.AddAuthorization();
builder.Services.AddEntraActorProvider();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", [Authorize] async (IActorProvider actorProvider, CancellationToken ct) =>
{
    var actor = await actorProvider.GetCurrentActorAsync(ct);
    return Results.Ok(new
    {
        actor.Id,
        Permissions = actor.Permissions.OrderBy(p => p).ToArray(),
        TenantId = actor.GetAttribute(ActorAttributes.TenantId),
        Mfa      = actor.GetAttribute(ActorAttributes.MfaAuthenticated),
    });
});

app.Run();
```

Matching `appsettings.json`:

```json
{
  "Authentication": {
    "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
    "Audience":  "<api-client-id>"
  }
}
```

> [!NOTE]
> The actor provider extracts an `Actor` from `HttpContext.User`. It does not validate tokens — keep `AddJwtBearer` (or any other authentication scheme) in front of it.

## Provider configuration

Pick one provider per environment. The choice is driven by token shape, not by sign-in protocol.

| Provider | Use when |
|---|---|
| `EntraActorProvider` | Issuer is Microsoft Entra ID v2.0 and you want `oid` / `roles` / `tid` / `amr` mapped out of the box. |
| `ClaimsActorProvider` | Token has a flat actor-id claim and a flat permissions claim (Auth0 `permissions`, Okta `scp`, custom OIDC). |
| Subclass of `ClaimsActorProvider` | Token has nested or computed claims (Keycloak `realm_access.roles`, claims merged from a database). |
| `DevelopmentActorProvider` | Local development or integration tests — `IsDevelopment()` only. |

### Entra ID provider

Use `AddEntraActorProvider` for Microsoft Entra ID (Azure AD) v2.0 tokens. The default mapping recognizes the standard Entra claim set without extra configuration.

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

var auth = configuration.GetSection("Authentication");

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth["Authority"];   // https://login.microsoftonline.com/<tenant-id>/v2.0
        options.Audience  = auth["Audience"];    // api client id (no api:// prefix for v2.0 audiences)
    });

services.AddEntraActorProvider();
```

`EntraActorProvider` derives from `ClaimsActorProvider` and overrides `GetCurrentActorAsync` to apply the Entra-specific delegates. When `IdClaimType` is the default long objectidentifier URI it falls back to the short `"oid"` claim before failing. See `EntraActorOptions` defaults in [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md#entraactoroptions).

### Generic OIDC provider

For any provider whose token exposes the actor id and permissions as flat claim types — Auth0, Okta, Google, or a custom IdP — register `ClaimsActorProvider` and name the two claim types.

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

var auth = configuration.GetSection("Authentication");

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth["Authority"];   // e.g. https://your-tenant.auth0.com/
        options.Audience  = auth["Audience"];    // your API audience
    });

services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim     = auth["ActorIdClaim"]     ?? "sub";
    options.PermissionsClaim = auth["PermissionsClaim"] ?? "permissions";
});
```

| Provider | Typical `ActorIdClaim` | Typical `PermissionsClaim` |
|---|---|---|
| Auth0 (RBAC) | `sub` | `permissions` |
| Okta (custom claim) | `sub` | `permissions` (or `scp` — see [Scope and permission extraction](#scope-and-permission-extraction)) |
| Google sign-in | `sub` | none — token only proves identity; load app permissions from your own store |

If the token only proves identity (Google is the canonical case), `Actor.Permissions` will be empty. That is a valid starting point: gate endpoints with `[Authorize]` for "signed in" and load fine-grained permissions from your database via a custom `IActorProvider` (see [Composition](#composition)).

### Nested-claim providers

`ClaimsActorOptions` matches `Claim.Type` verbatim — no JSON-path traversal. When a token contains a nested object (e.g. Keycloak's `{ "realm_access": { "roles": [...] } }`), the JWT handler stores the value as a raw JSON string under claim type `"realm_access"`. Subclass `ClaimsActorProvider` to project it.

```csharp
using System;
using System.Collections.Frozen;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

internal sealed record RealmAccess([property: JsonPropertyName("roles")] string[] Roles);

[JsonSerializable(typeof(RealmAccess))]
internal partial class KeycloakJsonContext : JsonSerializerContext { }

public sealed class KeycloakActorProvider(
    IHttpContextAccessor accessor,
    IOptions<ClaimsActorOptions> options) : ClaimsActorProvider(accessor, options)
{
    public override Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = HttpContextAccessor.HttpContext?.User.Identities
            .FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity
            ?? throw new InvalidOperationException("No authenticated user.");

        var sub = identity.FindFirst(Options.ActorIdClaim)?.Value
            ?? throw new InvalidOperationException($"Claim '{Options.ActorIdClaim}' missing.");

        var raw = identity.FindFirst("realm_access")?.Value;
        var roles = raw is null
            ? FrozenSet<string>.Empty
            : (JsonSerializer.Deserialize(raw, KeycloakJsonContext.Default.RealmAccess)?.Roles
                ?? Array.Empty<string>()).ToFrozenSet();

        return Task.FromResult(Actor.Create(sub, roles));
    }
}

// Register IOptions<ClaimsActorOptions> first (the subclass shares the base
// options type), then wrap with the caching helper. AddClaimsActorProvider
// initially Replaces IActorProvider with ClaimsActorProvider; the subsequent
// AddCachingActorProvider then Replaces it again with CachingActorProvider
// wrapping KeycloakActorProvider — that final composition is the one resolved.
services.AddClaimsActorProvider(opts => opts.ActorIdClaim = "sub");
services.AddCachingActorProvider<KeycloakActorProvider>();
```

`AddClaimsActorProvider(...)` configures `IOptions<ClaimsActorOptions>` (which `KeycloakActorProvider` consumes through its base constructor). `AddCachingActorProvider<T>` then registers `T` as scoped via `TryAddScoped` and wraps it with `CachingActorProvider`, so the JSON parse runs once per request even if multiple handlers ask for the actor. Because every `AddXxxActorProvider` Replaces the `IActorProvider` slot, the order matters: register the inner-provider's options helper first, then wrap.

## JWT validation options

Trellis does not own JWT validation — that lives in `Microsoft.AspNetCore.Authentication.JwtBearer`. The fields `JwtBearerHandler` validates flow into Trellis as follows:

| `JwtBearerOptions` setting | Effect on Trellis |
|---|---|
| `Authority` | Determines OIDC discovery / signing keys. If validation fails, `HttpContext.User` is unauthenticated and any actor provider throws `InvalidOperationException("No authenticated user.")`. |
| `Audience` (or `TokenValidationParameters.ValidAudiences`) | Must match the token `aud`. Mismatched audiences never reach the actor provider — the request is rejected with `401`. |
| `TokenValidationParameters.ValidIssuers` | Required when `Authority` does not match the literal `iss` claim (Google emits both `https://accounts.google.com` and `accounts.google.com`). |
| `TokenValidationParameters.ValidateIssuer = false` | Multi-tenant Entra requires this; tenant pinning then lives in `MapAttributes` or a custom validator. See [Multi-tenant Entra](#multi-tenant-entra). |
| `Events.OnTokenValidated` | The hook for synthesizing extra `Claim` instances before Trellis sees them — useful when your IdP issues identity but your app issues permissions. |

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.Audience  = configuration["Authentication:Audience"];
        options.TokenValidationParameters.ValidIssuers =
        [
            "https://accounts.google.com",
            "accounts.google.com",
        ];
    });
```

## Claim mapping

Each provider exposes a small surface of mapping options. The full default tables (claim names, attribute keys, fall-back rules) live in [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md#entraactoroptions); this section covers the everyday overrides.

### Synthesizing app permissions during token validation

When the IdP only issues identity, add permission claims in `OnTokenValidated`. `ClaimsActorProvider` aggregates every `Claim` whose `Type` matches `PermissionsClaim` via `FindAll`, so multiple `Claim` instances of the same type are flattened automatically.

```csharp
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth["Authority"];
        options.Audience  = auth["Audience"];
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity)
                    return Task.CompletedTask;

                identity.AddClaim(new Claim("permissions", "todos:read"));

                var email = identity.FindFirst("email")?.Value;
                if (!string.IsNullOrWhiteSpace(email)
                    && email.EndsWith("@yourcompany.com", StringComparison.OrdinalIgnoreCase))
                {
                    identity.AddClaim(new Claim("permissions", "todos:create"));
                }

                return Task.CompletedTask;
            },
        };
    });

services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim     = "sub";
    options.PermissionsClaim = "permissions";
});
```

> [!NOTE]
> `ClaimsActorProvider` only reads two claim types and never writes to `Actor.Attributes` or `Actor.ForbiddenPermissions`. Anything richer — ABAC attributes, deny lists, or computed permissions — belongs on `EntraActorProvider` (via `MapAttributes` / `MapForbiddenPermissions`) or a subclass. The full ABAC story is in [ASP.NET Core Authorization → Customizing claim mapping](integration-asp-authorization.md#customizing-claim-mapping).

### Customizing the Entra mapping

`EntraActorOptions` has three independent delegates and one identifier knob.

| Member | Default | Override to... |
|---|---|---|
| `IdClaimType` | long `objectidentifier` URI (falls back to short `"oid"`) | Use `sub`, an employee ID, or a custom claim. |
| `MapPermissions` | union of `roles` and `ClaimTypes.Role` | Flatten Entra app roles into fine-grained permissions; merge DB-sourced grants. |
| `MapForbiddenPermissions` | empty `HashSet<string>` | Project a deny-list claim. |
| `MapAttributes` | `tid`, `preferred_username`, `azp`, `azpacr`, `acrs` from claims; `ip_address` from connection; `mfa` from any `amr == "mfa"` | Add tenant-scoped or request-scoped attributes. |

> [!WARNING]
> Any exception thrown from `MapPermissions`, `MapForbiddenPermissions`, or `MapAttributes` is rewrapped by `EntraActorProvider` as `InvalidOperationException("EntraActorOptions.<delegate> threw an exception while mapping the authenticated user's claims.")`. The provider never silently defaults.

## Scope and permission extraction

Two common shapes for "what is this token allowed to do":

**App roles (Entra application permissions).** Each role is a separate claim of type `roles`. The default `MapPermissions` already collects them.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

services.AddEntraActorProvider(options =>
{
    var rolePermissionMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Catalog.Admin"]  = ["products:read", "products:write", "products:delete"],
        ["Catalog.Reader"] = ["products:read"],
    };

    options.MapPermissions = claims => claims
        .Where(c => string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase))
        .SelectMany(c => rolePermissionMap.TryGetValue(c.Value, out var perms) ? perms : Array.Empty<string>())
        .ToHashSet(StringComparer.Ordinal);
});
```

**Delegated scopes.** Entra (and many other OAuth servers) emit a single `scp` claim whose value is a space-separated string. Split it before flattening into permissions.

```csharp
services.AddEntraActorProvider(options =>
{
    options.MapPermissions = claims => claims
        .Where(c => string.Equals(c.Type, "scp", StringComparison.OrdinalIgnoreCase))
        .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToHashSet(StringComparer.Ordinal);
});
```

Trellis does not enforce a permission naming convention — pick one (`products:read` or `Products.Read`) and stay consistent. Scoped permissions use `Actor.PermissionScopeSeparator` (`':'`); `actor.HasPermission("products:read", tenantId)` checks for the joined string `products:read:<tenantId>`. See [`trellis-api-authorization.md → Actor`](../api_reference/trellis-api-authorization.md#actor).

## Multi-tenant Entra

A multi-tenant Entra app accepts tokens from any tenant the app is consented in. `Authority` becomes `https://login.microsoftonline.com/common/v2.0` (or `/organizations/v2.0`), the token's `iss` is per-tenant, and your API decides which tenants are allowed.

```csharp
using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Trellis.Asp.Authorization;

var allowedTenants = configuration.GetSection("Authentication:AllowedTenants").Get<string[]>()
    ?? Array.Empty<string>();

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.microsoftonline.com/common/v2.0";
        options.Audience  = configuration["Authentication:Audience"];

        options.TokenValidationParameters.ValidateIssuer = false;
        options.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
            issuer.StartsWith("https://login.microsoftonline.com/", StringComparison.Ordinal)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException($"Untrusted issuer '{issuer}'.");
    });

services.AddEntraActorProvider();
```

Then enforce the tenant allow-list inside handlers using the `tid` attribute the default `MapAttributes` already populates:

```csharp
using System.Linq;
using Trellis;
using Trellis.Authorization;

var tenantId = actor.GetAttribute(ActorAttributes.TenantId);

if (tenantId is null || !allowedTenants.Contains(tenantId, StringComparer.Ordinal))
    return Result.Fail(new Error.Forbidden("tenant.not-allowed") { Detail = $"Tenant '{tenantId}' is not provisioned." });
```

> [!TIP]
> Tenant-scoped permissions ride on the same `Actor.HasPermission(name, scope)` helper: `actor.HasPermission("documents:read", tenantId)` checks `documents:read:<tenantId>`.

## Development defaults

`AddDevelopmentActorProvider` reads an `X-Test-Actor` JSON header (case-insensitive property names) and falls back to a configurable default actor when the header is missing. It throws `InvalidOperationException` whenever `IHostEnvironment.IsDevelopment()` is `false` — including in Production with no header — which is the fail-closed safety net.

```csharp
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Asp.Authorization;

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevelopmentActorProvider(options =>
    {
        options.DefaultActorId         = "developer@local";
        options.DefaultPermissions     = new HashSet<string> { "todos:read", "todos:create" };
        options.ThrowOnMalformedHeader = false;
    });
}
else
{
    builder.Services.AddEntraActorProvider();
}
```

Send a header from any HTTP client to impersonate a specific actor:

```powershell
$actor = '{"Id":"local-user","Permissions":["todos:read","todos:create"],"ForbiddenPermissions":[],"Attributes":{"tid":"local-tenant","mfa":"false"}}'
Invoke-RestMethod https://localhost:5001/me -Headers @{ "X-Test-Actor" = $actor }
```

For test clients, `WebApplicationFactoryExtensions.CreateClientWithActor(...)` writes the same header (see [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)).

> [!WARNING]
> Use the **same SSO provider** in staging and production — only `Authority` and `Audience` should differ between environments. A staging path that swaps `EntraActorProvider` for `ClaimsActorProvider` (or vice versa) will hide token-format and claim-mapping bugs until they hit real users.

## Composition

SSO sits on top of three Trellis seams: actor resolution, mediator authorization, and result pipelines.

- **Actor resolution.** Every `IActorProvider` is scoped, so handlers, behaviors, and endpoints see the same `Actor` for the duration of a request. `AddCachingActorProvider<T>()` decorates any inner provider — Entra, Claims, or your subclass — without changing handler code.
- **Mediator pipeline.** Once an `IActorProvider` is registered, `AuthorizationBehavior<TMessage, TResponse>` enforces `IAuthorize.RequiredPermissions` and `IAuthorizeResource<T>.Authorize(actor, resource)` and short-circuits the pipeline with a typed `Error.Forbidden`. Commands return `Result<Unit>`; ASP integration maps `Error.Forbidden` to RFC 7807 `403`.
- **Result pipelines.** Inside handlers, `Actor` predicates return `bool`, so `Result.Ensure(actor.HasPermission(...), new Error.Forbidden(...))` plugs straight into `Bind` / `Map` chains.

```csharp
using System.Collections.Generic;
using Mediator;
using Trellis;
using Trellis.Authorization;

public sealed record DeleteDocumentCommand(string DocumentId)
    : ICommand<Result<Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions { get; } = ["documents:delete"];
}
```

The full mediator + resource-loader wiring (and the `AddResourceAuthorization<TResource, TId, TLoader>` helper) is documented in [ASP.NET Core Authorization → Mediator integration](integration-asp-authorization.md#mediator-integration).

## Practical guidance

- **Never authorize on `preferred_username`.** It is a display claim and can change. Use `oid` (Entra) or `sub` (everyone else).
- **Use `ActorAttributes` constants.** `actor.GetAttribute(ActorAttributes.TenantId)` survives renames; `actor.GetAttribute("tid")` does not.
- **Keep one mapping site.** Flatten roles to permissions inside `MapPermissions` once, not in every handler.
- **Pick one naming convention for permissions.** `todos:read` and `Todos.Read` are different strings — `Actor.Permissions` is a `FrozenSet<string>` with ordinal comparison.
- **Wrap expensive providers** with `AddCachingActorProvider<T>()` so DB-backed permission lookups run once per request.
- **Keep `AddDevelopmentActorProvider` behind `IsDevelopment()`.** The provider also fails closed at runtime, but the registration discipline avoids accidental dependency on `X-Test-Actor` from staging integration tests.
- **Match the audience exactly.** The number-one cause of "token validates somewhere else but not here" is `Audience` not matching the token's `aud` claim.
- **Let mapper exceptions bubble.** A buggy `MapPermissions` becomes `InvalidOperationException("EntraActorOptions.MapPermissions threw ...")`; surfacing it during development reveals bad role tables faster than a silent fallback.

## Cross-references

- ASP DI surface (actor providers, options, response mapping): [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
- Domain primitives (`Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<T>`, `ActorAttributes`): [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md)
- `Result`, `Error.Forbidden`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Deeper authorization patterns (claim mapping, ABAC, mediator integration, resource loaders): [ASP.NET Core Authorization](integration-asp-authorization.md)
- End-to-end token-validation tests against real Entra: [Testing with Entra ID Tokens](integration-entra-testing.md)
- Test-client `X-Test-Actor` helper: [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)
