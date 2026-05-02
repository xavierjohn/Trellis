---
title: Database-Backed Permissions
package: Trellis.EntityFrameworkCore
topics: [efcore, permissions, authorization, actor-provider, claims, abac, tenant, hybrid-auth]
related_api_reference: [trellis-api-efcore.md, trellis-api-authorization.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Database-Backed Permissions

Implement `IActorProvider` over EF Core so authentication stays in your identity provider while permissions live in the application database, manageable without redeploying.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Map `ClaimsPrincipal` to a Trellis `Actor` from DB-loaded permissions | Custom `IActorProvider` returning `Actor.Create(id, permissions)` | [Database-backed actor provider](#database-backed-actor-provider) |
| Load a flat permission set for an external user id | EF Core repository projecting `User → Roles → Permissions` to `IReadOnlySet<string>` | [Permission repository](#permission-repository) |
| Avoid duplicate permission lookups inside one HTTP request | `services.AddCachingActorProvider<T>()` | [DI wiring](#di-wiring) |
| Use stub actors in Development | `services.AddDevelopmentActorProvider()` | [DI wiring](#di-wiring) |
| Carry tenant / MFA context alongside permissions | Full `Actor` ctor with `attributes` keyed by `ActorAttributes.*` | [Carrying ABAC attributes](#carrying-abac-attributes) |
| Enforce a static permission on a command | `IAuthorize.RequiredPermissions` | [Composition](#composition) |
| Enforce ownership on a loaded resource | `IAuthorizeResource<TResource>` + `Result.Ensure(..., new Error.Forbidden(...))` | [Composition](#composition) |

## Use this guide when

- App roles in the JWT are too coarse, too static, or unavailable to the team that manages authorization.
- You need permission changes to take effect without a token refresh or auth-config redeploy.
- You want a single `Actor` shape (permissions + ABAC attributes) feeding both static and resource-based authorization.

## Surface at a glance

This guide composes existing Trellis surfaces — there is no DB-permissions package. The pieces:

| Type / member | Package | Purpose |
|---|---|---|
| `IActorProvider.GetCurrentActorAsync(ct)` | `Trellis.Authorization` | Contract you implement to resolve the current `Actor`. |
| `Actor.Create(string id, IReadOnlySet<string> permissions)` | `Trellis.Authorization` | Builds an actor with empty `ForbiddenPermissions` and empty `Attributes`. |
| `new Actor(id, permissions, forbiddenPermissions, attributes)` | `Trellis.Authorization` | Full ctor for ABAC / explicit deny. |
| `ActorAttributes.TenantId` / `MfaAuthenticated` / etc. | `Trellis.Authorization` | Well-known attribute keys. |
| `IAuthorize.RequiredPermissions` | `Trellis.Authorization` | Static permission requirement on a command/query. |
| `IAuthorizeResource<TResource>.Authorize(actor, resource)` | `Trellis.Authorization` | Resource-based authorization gate. |
| `services.AddCachingActorProvider<T>()` | `Trellis.Asp.Authorization` | Wraps your provider with request-scoped caching. |
| `services.AddDevelopmentActorProvider()` | `Trellis.Asp.Authorization` | Test-only provider driven by the `X-Test-Actor` header. |
| `RepositoryBase<TAggregate, TId>` + `db.SaveChangesResultUnitAsync(ct)` | `Trellis.EntityFrameworkCore` | Persist permission changes on the railway. |

Full signatures: [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md), [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md).

## Installation

```bash
dotnet add package Trellis.EntityFrameworkCore
dotnet add package Trellis.Asp
```

`Trellis.Asp` brings `Trellis.Authorization` transitively along with `AddCachingActorProvider<T>` / `AddDevelopmentActorProvider`. EF Core conventions and `SaveChangesResult*Async` come from `Trellis.EntityFrameworkCore`.

## Quick start

The minimal hybrid setup: identity provider authenticates, EF Core supplies permissions, `CachingActorProvider` deduplicates per request.

```csharp
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Asp.Authorization;

public interface IPermissionRepository
{
    Task<IReadOnlySet<string>> GetPermissionsForUserAsync(string externalUserId, CancellationToken ct);
}

public sealed class DatabaseActorProvider(
    IHttpContextAccessor accessor,
    IPermissionRepository repo) : IActorProvider
{
    public async Task<Actor> GetCurrentActorAsync(CancellationToken ct = default)
    {
        var principal = accessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HttpContext is available.");

        if (principal.Identity?.IsAuthenticated != true)
            throw new InvalidOperationException("The current request is not authenticated.");

        var externalId = principal.FindFirstValue("oid")
            ?? principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("No 'oid' or 'sub' claim was found.");

        var permissions = await repo.GetPermissionsForUserAsync(externalId, ct).ConfigureAwait(false);
        return Actor.Create(externalId, permissions);
    }
}

// Composition root — wire from Program.cs (or any DI extension method).
public static class CompositionRoot
{
    public static void AddDatabasePermissions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => configuration.Bind("AzureAd", options));

        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddCachingActorProvider<DatabaseActorProvider>();
    }
}
```

## Permission domain model

Keep the model boring: users, roles, permissions, and two many-to-many joins. The integration only needs to project to `IReadOnlySet<string>`.

```csharp
namespace MyService.Domain;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ICollection<Role> Roles { get; } = [];
}

public sealed class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<AppUser> Users { get; } = [];
    public ICollection<Permission> Permissions { get; } = [];
}

public sealed class Permission
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Role> Roles { get; } = [];
}

public static class Permissions
{
    public const string OrdersCreate   = "orders:create";
    public const string OrdersRead     = "orders:read";
    public const string OrdersReadAll  = "orders:read-all";
    public const string OrdersCancel   = "orders:cancel";
    public const string OrdersCancelAny = "orders:cancel-any";
}
```

A minimal mapping with unique indexes on `ExternalId`, `Role.Name`, and `Permission.Name`:

```csharp
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ExternalId).IsUnique();
            b.HasMany(x => x.Roles).WithMany(x => x.Users).UsingEntity("UserRoles");
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Name).IsUnique();
            b.HasMany(x => x.Permissions).WithMany(x => x.Roles).UsingEntity("RolePermissions");
        });

        modelBuilder.Entity<Permission>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Name).IsUnique();
        });
    }
}
```

> [!TIP]
> Centralise permission strings in one `Permissions` class. The same constants feed `IAuthorize.RequiredPermissions`, seed data, admin UI, and tests.

## Permission repository

The repository has one job: translate an authenticated external identity into a flat permission set. Use ordinal comparison so it matches `Actor`'s lookup semantics.

```csharp
using Microsoft.EntityFrameworkCore;

public sealed class PermissionRepository(AppDbContext db) : IPermissionRepository
{
    public async Task<IReadOnlySet<string>> GetPermissionsForUserAsync(
        string externalUserId, CancellationToken ct)
    {
        var permissions = await db.Users
            .Where(u => u.ExternalId == externalUserId)
            .SelectMany(u => u.Roles)
            .SelectMany(r => r.Permissions)
            .Select(p => p.Name)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return permissions.ToHashSet(StringComparer.Ordinal);
    }
}
```

The result type is exactly the `IReadOnlySet<string>` that `Actor.Create(id, permissions)` accepts — no adapter needed.

## Database-backed actor provider

The provider in [Quick start](#quick-start) is the canonical shape. It does three things:

| Step | Why |
|---|---|
| Read `HttpContext.User` from `IHttpContextAccessor` | `IActorProvider` is request-scoped; the principal is already populated by the auth middleware. |
| Extract `oid` (then fall back to `sub`) | Matches the `EntraActorProvider` precedence; both Entra v1.0 and v2.0 tokens resolve. |
| Call the repository, then `Actor.Create(...)` | `Actor.Create` snapshots permissions into a `FrozenSet<string>` for O(1) lookups. |

Throwing `InvalidOperationException` on missing context / unauthenticated requests follows the contract documented for `IActorProvider.GetCurrentActorAsync` ([`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md#iactorprovider)).

## Carrying ABAC attributes

When the actor needs more than permissions — tenant id, MFA flag, IP address — use the full `Actor` constructor and the well-known keys on `ActorAttributes`.

```csharp
using System.Collections.Generic;
using System.Security.Claims;
using Trellis.Authorization;

public sealed class TenantAwareActorProvider(
    IHttpContextAccessor accessor,
    IPermissionRepository repo) : IActorProvider
{
    public async Task<Actor> GetCurrentActorAsync(CancellationToken ct = default)
    {
        var ctx = accessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext is available.");
        var principal = ctx.User;

        var externalId = principal.FindFirstValue("oid")
            ?? principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("No 'oid' or 'sub' claim was found.");

        var permissions = await repo
            .GetPermissionsForUserAsync(externalId, ct)
            .ConfigureAwait(false);

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (principal.FindFirstValue("tid") is { } tid)
            attributes[ActorAttributes.TenantId] = tid;
        if (ctx.Connection.RemoteIpAddress is { } ip)
            attributes[ActorAttributes.IpAddress] = ip.ToString();

        return new Actor(
            id: externalId,
            permissions: permissions,
            forbiddenPermissions: new HashSet<string>(StringComparer.Ordinal),
            attributes: attributes);
    }
}
```

Downstream code reads attributes via `actor.GetAttribute(ActorAttributes.TenantId)` and uses them inside `IAuthorizeResource<TResource>` guards or as EF query-filter parameters.

## DI wiring

| Environment | Registration | Behavior |
|---|---|---|
| Production | `services.AddCachingActorProvider<DatabaseActorProvider>()` | Registers the concrete provider as scoped, then wraps it with `CachingActorProvider` so duplicate lookups inside one request reuse the same `Actor`. |
| Development | `services.AddDevelopmentActorProvider()` | Resolves an `Actor` from the `X-Test-Actor` header; throws outside the Development environment. |
| Test (in-memory) | `WebApplicationFactoryExtensions.CreateClientWithActor(actor)` | Sends the `X-Test-Actor` header consumed by `DevelopmentActorProvider`. |

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Trellis.Asp.Authorization;

if (environment.IsDevelopment())
{
    services.AddDevelopmentActorProvider();
}
else
{
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options => configuration.Bind("AzureAd", options));

    services.AddScoped<IPermissionRepository, PermissionRepository>();
    services.AddCachingActorProvider<DatabaseActorProvider>();
}
```

> [!NOTE]
> `AddCachingActorProvider<T>()` caches per request, not app-wide. Permission changes take effect on the next request — no manual invalidation required.

## Composition

A DB-backed `Actor` plugs into both authorization shapes the mediator behavior knows about. Static checks on the command, resource checks on the loaded aggregate. Commands return `Result<Unit>`.

```csharp
using Trellis;
using Trellis.Authorization;
using Trellis.Primitives;

public sealed partial class OrderId : RequiredGuid<OrderId>;
public sealed partial class ActorId : RequiredString<ActorId>;

public sealed record Order(OrderId Id, ActorId OwnerId);

public sealed record CancelOrderCommand(OrderId OrderId)
    : ICommand<Result<Unit>>,
      IAuthorize,
      IAuthorizeResource<Order>,
      IIdentifyResource<Order, OrderId>
{
    public IReadOnlyList<string> RequiredPermissions { get; } = [Permissions.OrdersCancel];

    public OrderId GetResourceId() => OrderId;

    public IResult Authorize(Actor actor, Order order) =>
        Result.Ensure(
            order.OwnerId.Value == actor.Id || actor.HasPermission(Permissions.OrdersCancelAny),
            new Error.Forbidden("orders.cancel")
                { Detail = "Only the owner can cancel this order." });
}
```

Pipeline ordering (from [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md#behavioral-notes)):

1. `AuthorizationBehavior` resolves the actor via your `IActorProvider`.
2. Static `IAuthorize.RequiredPermissions` are checked with `actor.HasAllPermissions(...)`.
3. The resource is loaded (per-command `IResourceLoader<TMessage, TResource>` or shared `SharedResourceLoaderById<TResource, TId>`).
4. `IAuthorizeResource<TResource>.Authorize(actor, resource)` runs.
5. The handler executes; on success `TransactionalCommandBehavior` commits via `IUnitOfWork`, mapping DB exceptions to typed `Error.Conflict` through `db.SaveChangesResultUnitAsync(ct)`.

EF query filters compose with the same `Actor` when a tenant attribute is in scope:

```csharp
modelBuilder.Entity<Order>().HasQueryFilter(o =>
    o.TenantId == tenantAccessor.CurrentTenantId);
```

The accessor reads `actor.GetAttribute(ActorAttributes.TenantId)` once per request — `CachingActorProvider` ensures a single DB lookup.

## Practical guidance

- **Permission strings are ordinal.** Match the casing used in seed data, `IAuthorize.RequiredPermissions`, and `actor.HasPermission(...)` exactly. `Actor` uses `StringComparison.Ordinal` everywhere.
- **Stage in the repository, commit in the pipeline.** Persist permission edits via `RepositoryBase<TAggregate, TId>`; `TransactionalCommandBehavior` calls `IUnitOfWork.CommitAsync(ct)`. Do not call `SaveChangesAsync` directly from a handler when `AddTrellisUnitOfWork<TContext>()` is registered.
- **Use `SaveChangesResultUnitAsync(ct)` for one-off admin scripts.** Outside the mediator pipeline, this overload returns `Result<Unit>` and maps duplicate-key / FK / concurrency exceptions to `Error.Conflict`.
- **Rely on request-scoped caching.** `AddCachingActorProvider<T>()` removes the need to memoise permissions yourself; emit a fresh request to pick up changes.
- **Mirror Entra precedence.** Read `oid` first, then fall back to `sub` — same order as `EntraActorProvider` so DB-backed code coexists with claims-based deployments.
- **Keep deny-list usage rare.** Add explicit denies to `Actor.ForbiddenPermissions` only when the domain genuinely needs an override; `Actor.HasPermission` already enforces deny-overrides-allow.

## Cross-references

- API surface (authorization primitives): [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md)
- API surface (EF Core repository / save helpers / UoW): [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md)
- `Result<T>`, `Error.Forbidden`, `Error.NotFound`, `Specification<T>`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Built-in `IActorProvider` implementations and DI helpers: [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md#namespace-trellisaspauthorization)
- Mediator authorization + transactional behaviors and registration order: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- Test-time actor injection (`CreateClientWithActor`): [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)
- EF Core integration overview (conventions, interceptors, `SaveChangesResult*`): [`integration-ef.md`](integration-ef.md)
