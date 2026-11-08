---
name: recipe-07-resource-authorization
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Covers IActorProvider, IAuthorize and resource-based authorization. Use when adding authorization checks or obtaining checked actor and resource parameters."
---
# Recipe 7 — Authorization: `IActorProvider` + `IAuthorize` + resource-based auth

## Contents

- Problem
- What it shows
- Checked actor/resource parameters
- Existing provider lookup
- Pipeline migration
- Microservices
- Path C — OAuth2 token exchange / OBO

**Problem.** Resource-based ownership check on an update command (the 90% case) plus a static permission gate on a delete command, all via the mediator pipeline.

```csharp
using Mediator;
using Trellis;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.Mediator;
using Trellis.Primitives;

// CANONICAL OWNER CHECK — the 90% case. Implement IAuthorizeResource<TResource> for the
// owner rule and IIdentifyResource<TResource, TId> so the framework reuses the shared
// SharedResourceLoaderById<TResource, TId> instead of requiring a per-command loader.
public sealed record UpdateOrderCommand(OrderId OrderId, Money NewTotal)
    : ICommand<Result<Unit>>, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>
{
    // Typed VO carried straight through — no parse, no throw.
    // ASP.NET model binding (via IScalarValue<OrderId, string>) handles the
    // string→OrderId conversion at the API edge.
    public OrderId GetResourceId() => OrderId;

    public Trellis.IResult Authorize(Actor actor, Order resource) =>
        Result.Ensure(
            resource.OwnerId == actor.Id || actor.HasPermission("orders:write"),
            () => new Error.Forbidden(Code: "orders.owner", Resource: ResourceRef.For<Order>(OrderId)));
}

// Static permission gate (no resource load needed): every actor with the named permission
// and no matching forbidden permission can run the command. Use IAuthorize when the decision does not depend on
// any resource state.
public sealed record DeleteOrderCommand(OrderId OrderId) : ICommand<Result<Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:delete"];
}

// DI wiring
services.AddTrellisBehaviors();
services.AddClaimsActorProvider();               // ClaimsActorProvider for ASP.NET Core
// Pass every assembly that contains command/query types AND every assembly that contains
// IResourceLoader<,> implementations. In a layered app the loader typically lives in the
// ACL assembly, not the Application assembly — passing only the Application assembly will
// register ResourceAuthorizationBehavior<,,> without discovering the shared loader and the
// pipeline will fail at runtime when it cannot resolve IResourceLoader<TMessage, TResource>.
// Note: the scanner de-duplicates the `assemblies` parameter with a first-seen-order
// HashSet, and closed pipeline-behavior registrations are idempotent across repeated
// scans and explicit-plus-scanned overlap when service type and implementation type match.
// Passing the same assembly twice is safe.
services.AddResourceAuthorization(
    typeof(UpdateOrderCommand).Assembly,        // Application assembly (commands + IAuthorizeResource)
    typeof(OrderResourceLoader).Assembly);      // ACL assembly (IResourceLoader<,> implementations)
```

**What it shows.** Lead with `IAuthorizeResource<TResource>` + `IIdentifyResource<TResource, TId>` for the owner-on-loaded-resource case — that pair covers most domain authorization decisions, and the framework wires up `SharedResourceLoaderById<TResource, TId>` automatically so no per-command loader is needed. Fall back to `IAuthorize` for static permission gates that do not require a resource load. `IAuthorizeResource<TResource>` runs *after* the resource loader produces the loaded resource, then calls `Authorize(actor, resource)`; `IAuthorize` enforces an AND-permission gate via `AuthorizationBehavior<,>` before the handler runs.

**Checked actor/resource parameters.** Derive from `ActorCommandHandler` /
`ActorQueryHandler` for actor-only business logic, or `ActorResourceCommandHandler` /
`ActorResourceQueryHandler` for direct resource logic, and override protected `Handle`.
The standard context supplies the **identical Actor instance** checked by this dispatch's
authorization stages, after every declared gate passes. Resource bases additionally supply
the exact loaded resource, without provider/accessor constructor dependencies or a second
load. They do not require `IAuthorize` on a resource-only message.

```csharp
public sealed record ReadOrderQuery(OrderId OrderId)
    : IQuery<Result<Order>>, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>
{
    public OrderId GetResourceId() => OrderId;
    public IResult Authorize(Actor actor, Order resource) =>
        Result.Ensure(resource.OwnerId == actor.Id, static () => new Error.Forbidden("orders.owner"));
}

public sealed class ReadOrderHandler : ActorResourceQueryHandler<ReadOrderQuery, Order, Result<Order>>
{
    protected override ValueTask<Result<Order>> Handle(
        ReadOrderQuery query, Actor actor, Order order, CancellationToken cancellationToken)
        => new(Result.Ok(order));
}
```

Keep the existing shared-loader registration and add the query's resource registration.
Ordinary accessor-based handlers remain valid; an actor-free business body need not adopt
these bases. See [all six bases and migration](../trellis-api-mediator.md#actor-aware-handler-bases).

**Existing provider lookup.** `await actorProvider.RequireActorAsync(cancellationToken)`
still performs another lookup, not a dispatch-snapshot read. It requires established actor
presence and a stable or explicitly cached provider when its result must match authorization.
For claims-backed hosts, register `AddCachingActorProvider<ClaimsActorProvider>()` after
`AddClaimsActorProvider`; wrap the same scoped provider used by authorization. The helper
throws when the presence invariant fails; it does not authenticate, check permissions, or
cache independently. Endpoints without that invariant still use `GetCurrentActorAsync`
and handle `None`.

**Pipeline migration.** Normal helpers install `AuthorizationContextBehavior`
automatically. Hand-built pipelines must place it before authorization; missing context is
a diagnosed configuration fault, not an implicit provider lookup. Nested sends have their
own snapshots, even inside the same DI scope. Native AOT hosts use the [literal closed
generator configuration](../trellis-api-mediator.md#native-aot-registration), not open
behaviors closed dynamically over struct Result responses.

For the same shared-loader shape without assembly scanning, register the implementation once and the behavior/accessor/adapter together per message:

```csharp
services.AddScoped<SharedResourceLoaderById<Order, OrderId>, OrderResourceLoader>();
services.AddSharedResourceAuthorization<UpdateOrderCommand, Order, OrderId, Result<Trellis.Unit>>();
```

The equivalent builder slot is `UseSharedResourceAuthorization<TMessage,TResource,TId,TResponse>()`; it also enables the standard Mediator behaviors. Both helpers preserve existing per-message loaders and leave the shared-loader implementation explicitly registered. The lower-level `AddResourceAuthorization<TMessage,TResource,TResponse>()` and `UseResourceAuthorization<TMessage,TResource,TResponse>()` remain available for custom loaders; neither adds the shared-loader bridge. See [`trellis-api-servicedefaults.md`](../trellis-api-servicedefaults.md#trellisservicebuilder). For multi-hop authorization, see [Recipe 24](recipe-24-indirect-authorization.md).

**Microservices.** The setup above works identically behind a reverse proxy / API gateway — the simplest microservices pattern is **token pass-through**: the gateway validates the incoming external JWT (Auth0 / Entra / Keycloak / etc.) and forwards it as-is, and each microservice configures `AddJwtBearer(o => o.Authority = "https://idp")` against the SAME external IDP. No new Trellis packages required.

```csharp
// Microservice composition — token pass-through path.
// Gateway validated the JWT; this service validates again against the same IDP and
// hydrates the Actor from the standard claims.
builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
{
    o.Authority = "https://your-idp.example";   // SAME IDP the gateway validated against
    o.Audience = "your-service";                // pin per-service audience
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "https://your-idp.example",
        ValidateAudience = true, ValidAudience = "your-service",
        ValidateLifetime = true, RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
});

builder.Services.AddTrellis(o => o
    .UseClaimsActorProvider(c => { c.ActorIdClaim = "sub"; c.PermissionsClaim = "permissions"; })
    .UseResourceAuthorization()
    .UseResourceAuthorization<UpdateOrderCommand, Order, Result<Unit>>());
```

This pattern works today with current Trellis. Choose it when the external IDP is stable, all microservices share the same trust root, and you don't need per-cluster permission projection or shorter-than-IDP token lifetimes.

**Path B — Trellis internal JWT (`AddTrellisInternalJwtActorProvider` from `Trellis.Microservices.AspNetCore`).** When the constraints above don't hold — you need per-cluster audience isolation, gateway-side permission projection, shorter token lifetimes than the external IDP allows, or you want downstream services decoupled from the external IDP's claim shape — switch to the Trellis internal-JWT contract. A trusted gateway (typically `Trellis.Yarp`, but any gateway implementing the same minting contract works) re-mints a fresh per-cluster JWT carrying the FULL resolved `Actor` shape, including `ForbiddenPermissions` and ABAC `Attributes`. The minter, downstream actor provider, and shared contract constants ship in the [`xavierjohn/Trellis.Microservices`](https://github.com/xavierjohn/Trellis.Microservices) repository (packages: `Trellis.Yarp`, `Trellis.Microservices.AspNetCore`, `Trellis.Microservices.Abstractions`). Downstream microservices add `Trellis.Microservices.AspNetCore` and call `services.AddTrellisInternalJwtActorProvider(...)` directly:

```csharp
// Microservice composition — Path B (Trellis internal JWT).
// Gateway minted a fresh internal JWT for this cluster; this service validates it against
// the gateway's signing key and hydrates the FULL Actor surface (including forbidden
// permissions + ABAC attributes) from the gateway-controlled claim shape.
using Trellis.Microservices.AspNetCore;  // ServiceCollectionExtensions.AddTrellisInternalJwtActorProvider

builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
{
    o.Authority = "https://gateway.internal";
    o.Audience = "incidents-service";
    o.MapInboundClaims = false;                       // keep raw JWT claim names (e.g. "tid", not the Microsoft tenant URI)
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "https://gateway.internal",
        ValidateAudience = true, ValidAudience = "incidents-service",
        ValidateLifetime = true, RequireSignedTokens = true,
        ValidAlgorithms = ["RS256"],                  // gateway uses asymmetric signing
        ClockSkew = TimeSpan.FromSeconds(30),         // tight skew for internal network
        TryAllIssuerSigningKeys = false,              // never fall back to "try every key" — see microservices cookbook Recipe 1
    };
});

builder.Services.AddTrellisInternalJwtActorProvider(c =>
{
    c.RequiredAttributes = ["tenant_id"];          // fail closed on missing tenant
    c.AttributeClaimMap["tenant_id"] = "tid";
    c.AttributeClaimMap["mfa"] = "amr_normalized";
    c.ExpectedIssuer = "https://gateway.internal"; // defense-in-depth runtime check
    c.ExpectedAudience = "incidents-service";
});

builder.Services.AddTrellis(o => o
    .UseResourceAuthorization()
    .UseResourceAuthorization<UpdateOrderCommand, Order, Result<Unit>>());
```

The internal-JWT contract requires the gateway to mint three sentinel claims (`trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count`) so a misbehaving proxy cannot strip the deny set silently — the deny-overrides-allow contract integrity invariant. The strict validation profile shown above is mandatory; the [microservices cookbook](https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md) Recipe 1 spells out the air-gapped (static-key-ring) variant, the tenant-isolation defense-in-depth check, and the key-rotation runbook; Recipe 2 covers the gateway-side end-to-end.

**Path C — OAuth2 token exchange / OBO** is out of scope for Trellis v1. Use `Microsoft.Identity.Web`'s OBO support directly when an enterprise multi-tenant SaaS needs RFC 8693 token exchange against the IDP.

The three-way decision matrix (when to choose each path) is documented in the upcoming `authorization-microservices.md` article.
