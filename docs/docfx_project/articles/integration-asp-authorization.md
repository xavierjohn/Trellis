---
title: ASP.NET Core Authorization
package: Trellis.Asp
topics: [authorization, actor, entra, claims, abac, mediator, asp]
related_api_reference: [trellis-api-authorization.md, trellis-api-asp.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# ASP.NET Core Authorization

`Trellis.Asp.Authorization` translates an authenticated `ClaimsPrincipal` into a frozen `Actor` (id + permissions + forbidden permissions + ABAC attributes) so handlers, mediator behaviors, and endpoints stop parsing JWT claims directly.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Resolve actors from Azure Entra ID v2.0 tokens | `AddEntraActorProvider(options?)` | [Entra ID provider](#entra-id-provider) |
| Resolve actors from any flat-claim OIDC/JWT provider | `AddClaimsActorProvider(options?)` | [Generic claims provider](#generic-claims-provider) |
| Inject a fake actor in Development / integration tests | `AddDevelopmentActorProvider(options?)` | [Development provider](#development-provider) |
| Cache an async actor resolution per request | `AddCachingActorProvider<T>()` | [Caching wrapper](#caching-wrapper) |
| Flatten roles into permissions / add ABAC attributes | `EntraActorOptions.MapPermissions` / `MapAttributes` | [Customizing claim mapping](#customizing-claim-mapping) |
| Read well-known attributes safely | `Actor.GetAttribute(ActorAttributes.*)` | [ABAC attributes](#abac-attributes) |
| Enforce static permissions on a command | `IAuthorize.RequiredPermissions` | [Mediator integration](#mediator-integration) |
| Authorize against a loaded entity | `IAuthorizeResource<TResource>` | [Mediator integration](#mediator-integration) |
| Identify two `Actor` snapshots as the same principal | `actorA.Equals(actorB)` (Id-based) | [Surface at a glance](#surface-at-a-glance) |

## Use this guide when

- You host a Trellis service in ASP.NET Core and need to convert an authenticated principal into an `Actor` exactly once per request.
- You want one DI registration that gives every handler, behavior, and endpoint a consistent view of the caller.
- You want to customize how JWT claims map to permissions, deny lists, or ABAC attributes without forking the provider.
- You want development/integration-test seams that fail closed outside `IsDevelopment()`.

## Surface at a glance

`Trellis.Asp.Authorization` (namespace inside the `Trellis.Asp` package) exposes one set of DI extensions plus four `IActorProvider` implementations and matching options classes.

| API | Kind | Returns / Lifetime | Purpose |
|---|---|---|---|
| `AddEntraActorProvider(this IServiceCollection, Action<EntraActorOptions>?)` | DI extension | Scoped `IActorProvider` → `EntraActorProvider` | Entra v2.0 (`oid`/`roles`/`tid`/`amr` ...) → `Actor`. |
| `AddClaimsActorProvider(this IServiceCollection, Action<ClaimsActorOptions>?)` | DI extension | Scoped `IActorProvider` → `ClaimsActorProvider` | Generic flat-claim mapping (configurable `ActorIdClaim`, `PermissionsClaim`). |
| `AddDevelopmentActorProvider(this IServiceCollection, Action<DevelopmentActorOptions>?)` | DI extension | Scoped `IActorProvider` → `DevelopmentActorProvider` | Reads `X-Test-Actor` JSON header; throws outside `IsDevelopment()`. |
| `AddCachingActorProvider<T>(this IServiceCollection)` | DI extension | Scoped `IActorProvider` → `CachingActorProvider` wrapping `T` | Caches one resolution task per request scope. |
| `ClaimsActorProvider` | Class | Scoped, virtual `GetCurrentActorAsync` | Subclass for custom flat-claim providers. Permissions collected via `FindAll(PermissionsClaim)`. |
| `EntraActorProvider` | Class | Scoped, sealed | Falls back to short `oid` when `IdClaimType` is the default; rewraps mapper exceptions in `InvalidOperationException`. |
| `DevelopmentActorProvider` | Class | Scoped, sealed partial | Logs a warning and falls back when the header is malformed (unless `ThrowOnMalformedHeader = true`). |
| `CachingActorProvider` | Class | Scoped, sealed | Uses `LazyInitializer.EnsureInitialized` + `HttpContext.RequestAborted`; honors per-call `CancellationToken` via `Task.WaitAsync`. |
| `EntraActorOptions` / `ClaimsActorOptions` / `DevelopmentActorOptions` | Options | — | Mapping delegates / claim-type strings / dev defaults. |

Full signatures: [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md).

> [!NOTE]
> **`Actor` is conceptually an entity, not a value.** Equality is identity-based on `Id` only — two `Actor` instances with the same `Id` are equal even when their `Permissions`, `ForbiddenPermissions`, or `Attributes` snapshots differ. `Id` is meant to be a stable, externally-meaningful principal identifier (e.g. JWT `sub` / `oid` claim); the other properties are point-in-time state about that principal (granted/revoked over time, ABAC attributes change every request). This mirrors `Trellis.Entity<TId>` semantics without taking on the full `IAggregate` surface. `Actor` is a `sealed class` (not a `record`), so the `with`-expression syntax is not available — call the constructor (or `Actor.Create`) directly when you need a copy with changes.

## Installation

```bash
dotnet add package Trellis.Asp
```

The actor providers ship in `Trellis.Asp` under namespace `Trellis.Asp.Authorization`. Domain primitives (`Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<T>`) come from `Trellis.Authorization`.

## Quick start

Authenticate with `JwtBearer`, register `EntraActorProvider`, then read the current `Actor` from any endpoint or handler.

```csharp
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
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
        Mfa = actor.GetAttribute(ActorAttributes.MfaAuthenticated),
    });
});

app.Run();
```

> [!NOTE]
> The provider extracts an `Actor` from `HttpContext.User`. It does **not** validate tokens — keep your normal authentication middleware in place.

## Entra ID provider

| Member | Default | Override to... |
|---|---|---|
| `EntraActorOptions.IdClaimType` | `"http://schemas.microsoft.com/identity/claims/objectidentifier"` (falls back to short `"oid"`) | Use `sub`, employee ID, etc. |
| `EntraActorOptions.MapPermissions` | union of `roles` and `ClaimTypes.Role` claim values (case-insensitive type match) | Flatten roles into fine-grained permissions; merge DB-sourced grants. |
| `EntraActorOptions.MapForbiddenPermissions` | empty `HashSet<string>` | Project a deny-list claim or DB lookup. |
| `EntraActorOptions.MapAttributes` | extracts `tid`, `preferred_username`, `azp`, `azpacr`, `acrs`; adds `ip_address` from `Connection.RemoteIpAddress` and `mfa = "true"\|"false"` from any `amr` claim equal to `"mfa"` | Add tenant-scoped or request-scoped attributes. |

`EntraActorProvider` throws `InvalidOperationException` when no `HttpContext` is available, no authenticated `ClaimsIdentity` exists, or the configured `IdClaimType` is missing (and the short `oid` fallback also misses). Any exception thrown by `MapPermissions`, `MapForbiddenPermissions`, or `MapAttributes` is rewrapped in `InvalidOperationException` naming the failing delegate.

## Generic claims provider

For any flat-claim OIDC token where you can name the id and permissions claim types directly.

| Member | Default | Notes |
|---|---|---|
| `ClaimsActorOptions.ActorIdClaim` | `"sub"` | Verbatim claim-type match — no JSON-path traversal. |
| `ClaimsActorOptions.PermissionsClaim` | `"permissions"` | Multi-valued JWT claims arrive as repeated `Claim` instances and are aggregated via `FindAll`. |

```csharp
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

builder.Services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim = "sub";
    options.PermissionsClaim = "permissions";
});
```

`ClaimsActorProvider.GetCurrentActorAsync` is `virtual` — subclass it to compute permissions from nested claims, look them up in a store, etc., then register your subclass via `AddCachingActorProvider<TYourProvider>()` to amortize the cost across the request.

## Development provider

For local development and integration tests only. The provider reads an `X-Test-Actor` JSON header shaped like `{ "Id": "...", "Permissions": [...], "ForbiddenPermissions": [...], "Attributes": {...} }` (case-insensitive property matching).

| Member | Default | Notes |
|---|---|---|
| `DevelopmentActorOptions.DefaultActorId` | `"development"` | Used when the header is missing or empty. |
| `DevelopmentActorOptions.DefaultPermissions` | empty `HashSet<string>` | Used when the header is missing or empty. |
| `DevelopmentActorOptions.ThrowOnMalformedHeader` | `false` | When `true`, malformed JSON throws instead of logging a warning and falling back. |

```csharp
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Asp.Authorization;

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevelopmentActorProvider(options =>
    {
        options.DefaultActorId = "developer@local";
        options.DefaultPermissions = new HashSet<string> { "Products.Read", "Products.Write" };
    });
}
else
{
    builder.Services.AddEntraActorProvider();
}
```

> [!WARNING]
> `DevelopmentActorProvider.GetCurrentActorAsync` throws `InvalidOperationException` whenever `IHostEnvironment.IsDevelopment()` is `false` — even when the header is absent. Registering it in Production is a fail-fast safety net.

For test clients, the `WebApplicationFactoryExtensions.CreateClientWithActor(...)` helper in `Trellis.Testing.AspNetCore` writes the same header for you; see [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md).

## Caching wrapper

When resolving an actor requires extra async work (database lookups, remote calls), wrap the inner provider so a single resolution task is shared across the request scope.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

public interface IPermissionStore
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(string userId, CancellationToken ct);
}

public sealed class DatabaseActorProvider(
    IHttpContextAccessor httpContextAccessor,
    IPermissionStore permissionStore) : IActorProvider
{
    public async Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Missing user id.");

        var permissions = await permissionStore.GetPermissionsAsync(userId, cancellationToken);
        return Actor.Create(userId, permissions);
    }
}

builder.Services.AddSingleton<IPermissionStore, MyPermissionStore>();
builder.Services.AddCachingActorProvider<DatabaseActorProvider>();
```

`CachingActorProvider` issues the inner call with `HttpContext.RequestAborted` so the shared work is cancelled with the request, then forwards each caller's own `CancellationToken` via `Task.WaitAsync`.

## Customizing claim mapping

`EntraActorOptions` exposes three independent delegates — override only what you need.

### Flatten Entra roles into application permissions

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

builder.Services.AddEntraActorProvider(options =>
{
    var rolePermissionMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Catalog.Admin"]  = ["Products.Read", "Products.Write", "Products.Delete"],
        ["Catalog.Reader"] = ["Products.Read"],
    };

    options.MapPermissions = claims => claims
        .Where(c => string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase))
        .SelectMany(c => rolePermissionMap.TryGetValue(c.Value, out var perms) ? perms : Array.Empty<string>())
        .ToHashSet(StringComparer.Ordinal);
});
```

### Use delegated scopes (`scp`) instead of roles

```csharp
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

builder.Services.AddEntraActorProvider(options =>
{
    options.MapPermissions = claims => claims
        .Where(c => string.Equals(c.Type, "scp", StringComparison.OrdinalIgnoreCase))
        .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToHashSet(StringComparer.Ordinal);
});
```

### Add forbidden permissions and custom attributes

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

builder.Services.AddEntraActorProvider(options =>
{
    options.MapForbiddenPermissions = claims => claims
        .Where(c => string.Equals(c.Type, "denied_permissions", StringComparison.OrdinalIgnoreCase))
        .Select(c => c.Value)
        .ToHashSet(StringComparer.Ordinal);

    options.MapAttributes = (claims, httpContext) =>
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        var region = claims.FirstOrDefault(c => c.Type == "region")?.Value;
        if (!string.IsNullOrWhiteSpace(region))
            attributes["region"] = region;

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip))
            attributes[ActorAttributes.IpAddress] = ip;

        return attributes;
    };
});
```

> [!WARNING]
> Throwing inside any `Map*` delegate produces an `InvalidOperationException` with the message `EntraActorOptions.<delegate> threw an exception while mapping the authenticated user's claims.` — it does not silently default.

## ABAC attributes

`Actor.Attributes` is an immutable `FrozenDictionary<string, string>`. Read it via `GetAttribute` (returns `string?`) or `HasAttribute`, and key it with `ActorAttributes.*` constants instead of magic strings.

```csharp
using Trellis;
using Trellis.Authorization;

var tenantId = actor.GetAttribute(ActorAttributes.TenantId);

if (tenantId is null || !actor.HasPermission("Documents.Read", tenantId))
    return Result.Fail(new Error.Forbidden("documents.read") { Detail = "Wrong tenant." });

var clientApp = actor.GetAttribute(ActorAttributes.AuthorizedParty);
var mfaPassed = actor.GetAttribute(ActorAttributes.MfaAuthenticated) == "true";
```

`Actor.HasPermission(permission, scope)` checks for the joined string `permission:scope`, where `:` is `Actor.PermissionScopeSeparator`. See [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md#actor) for the full deny-overrides-allow rules.

## Mediator integration

When a request flows through `Trellis.Mediator`, prefer pipeline behaviors over per-handler permission checks — `AuthorizationBehavior<TMessage, TResponse>` (see [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)) calls `IActorProvider.GetCurrentActorAsync` once and short-circuits the pipeline with a typed `Error.Forbidden`.

### Static permission checks via `IAuthorize`

```csharp
using System.Collections.Generic;
using Mediator;
using Trellis;
using Trellis.Authorization;

public sealed record DeleteDocumentCommand(string DocumentId)
    : ICommand<Result<Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions { get; } = ["Documents.Delete"];
}
```

The behavior requires the actor to hold **every** listed permission (AND semantics). Returning `Result<Unit>` from the command is the canonical command shape.

### Resource-based checks via `IAuthorizeResource<TResource>`

Use this when the rule depends on a loaded entity, not just static permissions. Pair it with `IIdentifyResource<TResource, TId>` + a `SharedResourceLoaderById<TResource, TId>` so every command authorizing against the same resource type loads it the same way.

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;

public sealed record Document(string Id, string OwnerId);

public sealed record EditDocumentCommand(string DocumentId)
    : ICommand<Result<Unit>>, IAuthorizeResource<Document>, IIdentifyResource<Document, string>
{
    public string GetResourceId() => DocumentId;

    public IResult Authorize(Actor actor, Document resource) =>
        Result.Ensure(
            actor.IsOwner(resource.OwnerId) || actor.HasPermission("Documents.EditAny"),
            new Error.Forbidden("documents.edit") { Detail = "Only the owner can edit this document." });
}
```

Wire the loader and pipeline behaviors in `Program.cs`:

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;
using Trellis.Mediator;

builder.Services.AddEntraActorProvider();

// 1. Register the resource loader for this command.
//    DocumentResourceLoader implements IResourceLoader<EditDocumentCommand, Document>.
builder.Services.AddScoped<IResourceLoader<EditDocumentCommand, Document>, DocumentResourceLoader>();

// 2. Register the resource-authorization pipeline behavior.
//    Type arguments are <TMessage, TResource, TResponse> — the *command* type comes first,
//    then the resource type loaded for it, then the command's response type.
builder.Services.AddResourceAuthorization<EditDocumentCommand, Document, Result<Unit>>();

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.PipelineBehaviors = [.. Trellis.Mediator.ServiceCollectionExtensions.PipelineBehaviors];
});
```

> [!NOTE]
> `AddResourceAuthorization` lives in `Trellis.Mediator`. Forgetting `using Trellis.Mediator;` produces `CS1061: 'IServiceCollection' does not contain a definition for 'AddResourceAuthorization'`. The full registration shape is documented in [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md#servicecollectionextensions).

> [!IMPORTANT]
> **The `TResponse` type argument must satisfy `IResult` *and* `IFailureFactory<TResponse>`.** `Result<TValue>` (the canonical command response) satisfies both automatically. If you wire up a custom envelope response type that doesn't, `AddResourceAuthorization<TMessage, TResource, TResponse>()` (and the assembly-scanning overload, when it sees an `IAuthorizeResource<TResource>` message with that response type) **fails fast at registration** with `InvalidOperationException` — the security-marked command will not silently ship without resource authorization. See [Custom envelope response types](integration-mediator.md#custom-envelope-response-types) in the mediator article for the full contract.

## Composition

These providers compose three ways:

- **Provider stack.** `AddCachingActorProvider<TInner>()` decorates *any* `IActorProvider` — including a `ClaimsActorProvider` subclass you authored — without changing handler code.
- **Mediator pipeline.** Once an `IActorProvider` is registered, both `IAuthorize` (static) and `IAuthorizeResource<T>` (resource) checks run inside `AuthorizationBehavior` and fail with typed `Error.Forbidden`. ASP integration ([`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)) maps that to RFC 7807 `403`.
- **Result pipelines.** Inside handlers, `Actor` predicates return `bool`, so `Result.Ensure(actor.HasPermission(...), new Error.Forbidden(...))` plugs straight into `Bind` / `Map` chains.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Authorization;

public sealed record Document(string Id, string OwnerId);

public interface IDocumentRepository
{
    Task<Result<Document>> GetByIdAsync(string id, CancellationToken ct);
}

public static class DocumentService
{
    public static async Task<Result<Document>> LoadForEditAsync(
        IActorProvider actors,
        IDocumentRepository repo,
        string id,
        CancellationToken ct)
    {
        var actor = await actors.GetCurrentActorAsync(ct);

        return await repo.GetByIdAsync(id, ct)
            .EnsureAsync(
                doc => actor.IsOwner(doc.OwnerId) || actor.HasPermission("Documents.EditAny"),
                new Error.Forbidden("documents.edit") { Detail = "Only the owner can edit this document." });
    }
}
```

## Practical guidance

- **Never authorize on `preferred_username`.** It is for display and audit only and may change.
- **Use `ActorAttributes` constants.** Guarantees consistent casing and survives renames.
- **Map roles to permissions once, in `MapPermissions`.** Keeps runtime checks `O(1)` against a `FrozenSet<string>`.
- **Prefer scoped permissions** (`Documents.Read:tenant-123`) and `Actor.HasPermission(name, scope)` over a separate ABAC dictionary lookup.
- **Wrap expensive providers** with `AddCachingActorProvider<T>()` so re-checks during the same request stay cheap.
- **Keep `AddDevelopmentActorProvider` behind `IsDevelopment()`.** The provider also fails closed, but registration discipline avoids accidental dependency on `X-Test-Actor` from staging tests.
- **Let mapper exceptions bubble.** A buggy `MapPermissions` becomes `InvalidOperationException("EntraActorOptions.MapPermissions threw ...")`; surfacing it during development reveals bad role tables faster than any silent fallback.

## Cross-references

- Domain primitives (`Actor`, `IAuthorize`, `IAuthorizeResource<T>`, `ActorAttributes`): [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md)
- ASP DI surface and other ASP integration helpers: [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
- `Result`, `Error.Forbidden`, `Error.NotFound`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- `AuthorizationBehavior<TMessage, TResponse>` and `AddResourceAuthorization`: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- Test client header helper (`CreateClientWithActor`): [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)
