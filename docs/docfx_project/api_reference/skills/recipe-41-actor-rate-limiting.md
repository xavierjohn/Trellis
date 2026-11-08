---
name: recipe-41-actor-rate-limiting
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows actor-aware ASP.NET rate limiting that partitions by canonical identity without loading permissions. Use when adding per-actor rate limits."
---
# Recipe 41 — Actor-aware rate limiting without loading permissions

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.Authorization`, `Trellis.ServiceDefaults`, `Trellis.Testing`.

## Contents

- [Middleware order and cost](#middleware-order-and-cost)
- [Identity, network and operational boundaries](#identity-network-and-operational-boundaries)

**Problem.** Partition ASP.NET Core rate limits by the provider's canonical actor identity
without loading database-backed permissions before any limit applies, or duplicating
subject extraction/provider normalization in the synchronous partitioner.

**Choose the path.** A custom claims-only provider and limiter call **one shared
application-owned synchronous normalizer**: no async prefetch is needed. Share the whole
canonical function, including authenticated-identity selection, subject fallback,
provider/issuer label and `ActorId` normalization, not just the label helper.
For an identity provider that genuinely needs asynchronous work, resolve
`IActorIdentityProvider` before the limiter and retain a private request-local snapshot.
Never use `.Result`, `.Wait()`, or blocking async calls in the partitioner.

The following application-owned sample is compiled in
`Examples/CookbookSnippets/Recipe41_ActorRateLimiting.cs`; its tests execute both paths,
network fallback, authorization ordering and idempotent replay. The small two-per-minute
budget is illustrative, not a Trellis default. Configure and validate the host's real
authentication scheme first. This sample assumes one trusted issuer and uses `"sample"`
as its application-owned provider label; multi-issuer hosts must use their provider's
canonical issuer mapping rather than arbitrary caller-supplied labels.

```csharp
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Asp.Idempotency;
using Trellis.Authorization;
using Trellis.ServiceDefaults;

public sealed class SharedClaimsActorProvider(IHttpContextAccessor accessor)
    : IActorProvider, IActorIdentityProvider
{
    public static Maybe<ActorId> CanonicalActorId(ClaimsPrincipal principal)
    {
        var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated);
        var subject = identity?.FindFirst("sub")?.Value
            ?? identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(subject)
            ? Maybe<ActorId>.None
            : Maybe.From(ActorId.Create($"sample:{subject.Trim()}"));
    }

    public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CanonicalActorId(CurrentPrincipal()));
    }

    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = CurrentPrincipal();
        return Task.FromResult(CanonicalActorId(principal).Map(id => Actor.Create(id,
            principal.Identities.First(i => i.IsAuthenticated).FindAll("permissions")
                .Select(c => c.Value).ToHashSet(StringComparer.Ordinal))));
    }

    private ClaimsPrincipal CurrentPrincipal() => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("Actor resolution requires an HTTP request.");
}

public sealed record ActorIdentityRateLimitMetadata;

public static class ActorRateLimitingSample
{
    private const string Policy = "actor";
    private static readonly object s_identitySnapshotKey = new();
    private sealed record IdentitySnapshot(Maybe<ActorId> Id);

    public static void ConfigureServices(IServiceCollection services, bool usePrefetch)
    {
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddTrellis(t => t.UseAsp().UseProblemDetails().UseIdempotency());
        services.AddInMemoryIdempotencyStore();
        services.AddCachingActorProvider<SharedClaimsActorProvider>();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
                usePrefetch ? PrefetchedPartitionKey(context) : ClaimsPartitionKey(context),
                static _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 2,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
            options.UseTrellisRejectionHandler();
        });
    }

    public static void ConfigurePipeline(WebApplication app, bool usePrefetch, bool authorizationFirst = false)
    {
        app.UseRouting();
        app.UseAuthentication();
        if (authorizationFirst)
            app.UseAuthorization();
        if (usePrefetch)
            UseIdentityPrefetch(app);
        app.UseRateLimiter();
        if (!authorizationFirst)
            app.UseAuthorization();
        app.UseTrellisIdempotency();

        app.MapGet("/limited", static () => Results.Ok())
            .WithMetadata(new ActorIdentityRateLimitMetadata()).RequireRateLimiting(Policy);
        app.MapPost("/payments", static () => Results.Ok(new { ReceiptId = Guid.NewGuid() }))
            .WithMetadata(new ActorIdentityRateLimitMetadata(), new IdempotentAttribute())
            .RequireRateLimiting(Policy).RequireAuthorization();
        app.MapGet("/ready", static () => Results.Ok()).AllowAnonymous().DisableRateLimiting();
    }

    public static void UseIdentityPrefetch(IApplicationBuilder app) =>
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<ActorIdentityRateLimitMetadata>() is not null)
            {
                var provider = context.RequestServices.GetRequiredService<IActorIdentityProvider>();
                var id = await provider.GetCurrentActorIdAsync(context.RequestAborted).ConfigureAwait(false);
                context.Items[s_identitySnapshotKey] = new IdentitySnapshot(id);
            }

            await next(context).ConfigureAwait(false);
        });

    public static string ClaimsPartitionKey(HttpContext context) =>
        PartitionKey(context, SharedClaimsActorProvider.CanonicalActorId(context.User));

    public static string PrefetchedPartitionKey(HttpContext context)
    {
        if (!context.Items.TryGetValue(s_identitySnapshotKey, out var value) || value is not IdentitySnapshot snapshot)
            throw new InvalidOperationException(
                "Actor identity prefetch did not run. Add the endpoint metadata and mount " +
                "UseIdentityPrefetch after routing/authentication and before UseRateLimiter.");

        return PartitionKey(context, snapshot.Id);
    }

    private static string PartitionKey(HttpContext context, Maybe<ActorId> identity) =>
        identity.Match(id => $"actor:{id.Value}", () => NetworkPartitionKey(context.Connection.RemoteIpAddress));

    private static string NetworkPartitionKey(IPAddress? address)
    {
        if (address is null)
            return "network:unknown";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return $"network:v4:{address}";

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"network:v6:{new IPAddress(bytes)}/64";
    }
}
```

Use the same `usePrefetch` value in service and pipeline configuration. For the
claims-only provider above, use `false`. Set it to `true` when replacing the provider
with one whose identity-only path can await; the partitioner then reads the completed
snapshot rather than resolving a provider synchronously. `UseIdentityPrefetch`,
`IdentitySnapshot`, route metadata, `CanonicalActorId` and partition helpers are
**application code, not shipped Trellis middleware/APIs**. Routes needing async identity
carry the metadata; unrelated, network-only and health routes do not. A missing snapshot
is a configuration fault, distinct from a successfully resolved `None` snapshot.

## Middleware order and cost

| Order after routing/authentication | Consequence |
|---|---|
| Optional identity prefetch → limiter → authorization → idempotency → endpoint | Requests reaching the applicable limiter consume permits even if later challenged/forbidden; exhaustion can produce 429 before a later 401/403. |
| Authorization → optional identity prefetch → limiter → idempotency → endpoint | Valid when deliberately retained: ASP.NET authorization challenges/forbids short-circuit before the limiter and consume no permits. The limiter does **not** protect authorization work. |

The client-style `UseAuthorization()` → `UseRateLimiter()` → `UseTrellisIdempotency()`
order therefore remains valid. Permission loading inside a later Mediator handler is
different from ASP.NET authorization: those later requests have already reached the
limiter. In either order, putting the limiter before idempotency charges replay requests.
`DefaultIdempotencyScopeResolver` and `ActorIdempotencyScopeResolver` still resolve
full `IActorProvider`; registering identity-only resolution does not change them.

Identity-only forbids permission/attribute loads, **not all I/O**. An expensive identity
lookup or authentication/authorization stage before this limiter needs a separate
upstream/network guard at a location that actually runs before that work. A second policy
in the same later `UseRateLimiter()` cannot protect earlier prefetch. Do not claim an
actor limiter protects a preceding database lookup.

## Identity, network and operational boundaries

Treat `ActorId` as opaque: do not split provider labels or replace it with raw `sub`,
email, or a separately parsed development header. The provider and limiter share one
canonical mapping. Built-in providers resolve through DI adapters; custom subclasses
opt in explicitly. `AddCachingActorProvider<T>()` is the one registration for both
supported interfaces, not two registrations that can drift.

Anonymous fallback has a **separate network namespace**, normalizes IPv4-mapped IPv6,
and groups IPv6 by an illustrative `/64`. Missing address uses one bounded shared
`network:unknown` bucket, never an unlimited bypass. NAT sharing and IPv6 grouping/budgets
are application policy; network fallback is not actor identity.

Use `RemoteIpAddress` only after the host's trusted-proxy configuration has established
the transport boundary. Do not read arbitrary `X-Forwarded-For` yourself. Keep raw
partition keys out of responses, logs and metric labels. Limiters shown here are
**per instance**, not cross-replica/distributed quota guarantees; choose edge/distributed
infrastructure when global enforcement is required.

In provider tests, assert agreement in one line using the DI-resolved interfaces:

```csharp
await actorProvider.ShouldHaveConsistentActorIdentityAsync(identityProvider, cancellationToken);
```

The helper lives in `Trellis.Testing`; import that namespace in tests. It resolves the
two paths sequentially and checks full-actor IDs without treating identity presence
as authorization.

**Retry-After.** The existing Trellis rejection adapter emits only lease-provided advice.
On .NET 10, sliding-window and concurrency limiters still do not provide an estimate;
the retry-advice documentation/characterization change does not synthesize one.
Fixed-window/token-bucket advice can also be absent. Clients handle 429 without a header
using bounded backoff and jitter, subject to the operation's retry safety.

**Anti-pattern → fix.** Duplicated subject extraction → one full canonical normalizer;
permission-loading full actor prefetch → identity-only provider; blocking partitioner →
await before the limiter and read a private snapshot; missing snapshot treated as
anonymous → explicit configuration error. Use `Maybe.Match` / guarded `TryGetValue`
instead of unsafe `.Value` access (TRLS003); no new authorization or limiter engine exists.

**References.** [Identity contract](../trellis-api-authorization.md#iactoridentityprovider),
[ASP identity registration](../trellis-api-asp.md#identity-only-provider-registration),
[testing assertion](../trellis-api-testing-reference.md#actoridentityassertionsextensions),
[429/retry contract](../trellis-api-asp.md#ratelimiteroptionsextensions),
[idempotency](../trellis-api-asp.md#namespace-trellisaspidempotency).
