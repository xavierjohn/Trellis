---
package: Trellis.Yarp
namespaces: [Trellis.Yarp]
types: [TrellisActorForwardingOptions, TrellisActorForwardingServiceCollectionExtensions, TrellisDiscoveryEndpointRouteBuilderExtensions]
version: v3
last_verified: 2026-06-05
audience: [llm]
---
# Trellis.Yarp — API Reference

**Package:** `Trellis.Yarp` (NOT AOT-compatible — YARP itself is not AOT-clean; consumers needing an AOT-compatible microservice path keep the gateway non-AOT and ship AOT-compatible downstream services depending only on `Trellis.Asp`).
**Namespaces:** `Trellis.Yarp`
**Purpose:** YARP gateway integration for Trellis. Re-mints a per-cluster internal JWT from the full Trellis `Actor` (id + permissions + forbidden permissions + ABAC attributes); exposes an OIDC discovery + JWKS endpoint pair so downstream services using `AddJwtBearer(o => o.Authority = gatewayUrl)` can fetch the active signing keys; emits redacted audit telemetry on every mint.

Pairs with the consumer-side `TrellisInternalJwtActorProvider` in `Trellis.Asp.Authorization` (see [Recipe 33](trellis-api-cookbook.md#recipe-33--strict-addjwtbearer-validation-profile-for-usetrellisinternaljwtactor) for the downstream side, [Recipe 34](trellis-api-cookbook.md#recipe-34--microservices-behind-yarp-end-to-end) for the gateway side end-to-end).

## Use this file when

- You are standing up a YARP gateway in front of Trellis microservices and need Path B (Trellis internal JWT) wired end-to-end.
- You need the exact signature of `AddTrellisActorForwarding`, `MapTrellisDiscoveryEndpoint`, or `TrellisActorForwardingOptions`.
- You are auditing the gateway-side mint contract: which claims are emitted, what gets logged, what symmetric / missing-`kid` configurations get rejected at startup.
- You are implementing the key-rotation runbook for a Trellis-fronted gateway.

## Patterns Index

| Goal | Canonical API / action | See |
|---|---|---|
| Register the YARP actor-forwarding transform pipeline | `builder.Services.AddReverseProxy().AddTrellisActorForwarding(configure)` | [`TrellisActorForwardingServiceCollectionExtensions`](#trellisactorforwardingservicecollectionextensions) |
| Publish OIDC discovery + JWKS to downstream services | `app.MapTrellisDiscoveryEndpoint()` | [`TrellisDiscoveryEndpointRouteBuilderExtensions`](#trellisdiscoveryendpointroutebuilderextensions) |
| Configure per-cluster audience | `options.AudiencePerCluster = cluster => "<audience>"` | [`TrellisActorForwardingOptions`](#trellisactorforwardingoptions) |
| Project permissions per cluster | `options.ProjectPermissionsFor = (cluster, perms) => perms.Where(...)` | [`TrellisActorForwardingOptions`](#trellisactorforwardingoptions) |
| Override `sub` for multi-IdP gateways (MUST do this when fronting >1 IdP) | `options.ActorIdResolver = actor => $"<ns>\|{actor.Id.Value}"` | [`TrellisActorForwardingOptions`](#trellisactorforwardingoptions) |
| Rotate signing keys with overlap window | Set new `SigningCredentials`; move the previous key into `PreviousSigningKeys` until the rotation window expires | [Recipe 33](trellis-api-cookbook.md#recipe-33--strict-addjwtbearer-validation-profile-for-usetrellisinternaljwtactor) (rotation runbook) |
| Emergency revoke a compromised key | Drop the compromised `kid` from `SigningCredentials` AND `PreviousSigningKeys`, redeploy gateway, force downstream JWKS refresh | [Recipe 34](trellis-api-cookbook.md#recipe-34--microservices-behind-yarp-end-to-end) (emergency revocation procedure) |

## Threat model

`Trellis.Yarp` treats the gateway as the authority for the downstream-internal trust boundary. **Signing-key compromise = full identity spoof until key revocation propagates.** The package designs for this scenario via:

- Short token lifetimes (default 5 minutes; startup validation caps at `[1m, 30m]`).
- `kid`-aware overlapping JWKS rotation.
- Audit-log redaction (no JWT body, no raw claim values, no actor IDs in any `[LoggerMessage]` event).
- Emergency revocation procedure (drop `kid` from JWKS → redeploy → force downstream refresh).

The startup validator rejects: missing `Issuer`, non-absolute `PublicBaseUrl`, null / symmetric / missing-`kid` `SigningCredentials`, rotation-ring `kid` collisions, lifetime outside `[1m, 30m]`, null callbacks. Additionally, the registration validator (`IHostedLifecycleService`) fails host start if `AddTrellisActorForwarding` was called but no `IActorProvider` is registered.

---

## TrellisActorForwardingOptions

Configuration for the YARP actor-forwarding transform. Bound via `AddOptions<TrellisActorForwardingOptions>().Configure(configure).ValidateOnStart()` inside `AddTrellisActorForwarding`.

| Member | Type | Default | Required? | Purpose |
|---|---|---|---|---|
| `Issuer` | `string` | (none) | **Yes** | JWT `iss` claim value AND OIDC discovery doc `issuer`. Conventionally a URL identifying the gateway (e.g. `"https://gateway.internal"`). |
| `SigningCredentials` | `SigningCredentials` | (none) | **Yes** | Asymmetric signing credential. `Key` MUST be asymmetric (`RsaSecurityKey` / `ECDsaSecurityKey`) and MUST have a non-empty `KeyId` (the `kid`). Startup validation rejects symmetric / null / missing-`kid` keys. |
| `PreviousSigningKeys` | `IReadOnlyList<SecurityKey>` | `[]` | No | Previous-generation signing keys still trusted during a rotation overlap window. Each entry MUST be asymmetric + non-empty `kid`. NOT used to sign new tokens; ARE published in JWKS. |
| `PublicBaseUrl` | `Uri` | (none) | **Yes** | Absolute public URL the gateway is reachable at. Used to build absolute URLs in the OIDC discovery document. **NOT inferred from `HttpRequest.Host`** (spoofable behind reverse proxies). |
| `AudiencePerCluster` | `Func<ClusterConfig, string>` | `cluster => cluster.ClusterId` | No | Selects the JWT `aud` claim value per destination cluster. Override to a per-cluster audience literal so each downstream pins `JwtBearerOptions.Audience` to a unique value (cross-audience confusion defense). |
| `ProjectPermissionsFor` | `Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>>` | pass-through | No | Projects `Actor.Permissions` onto the subset relevant to the destination cluster. Recommended convention: `(c, perms) => perms.Where(p => p.StartsWith(c.ClusterId + ".")).ToHashSet(...)`. |
| `ProjectForbiddenFor` | `Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>>` | pass-through | No | Projects `Actor.ForbiddenPermissions` per cluster. Same shape as `ProjectPermissionsFor`. **Contract integrity:** the count is always emitted, even when empty (deny-overrides-allow integrity invariant). |
| `ProjectAttributes` | `Func<ClusterConfig, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>` | pass-through | No | Projects `Actor.Attributes` per cluster. Output keys become JWT claim names. |
| `ActorIdResolver` | `Func<Actor, string>` | `actor => actor.Id.Value` | No | Computes the JWT `sub` claim. **MUST override** when fronting multiple IdPs / tenants to mint a namespaced subject (`$"{issuer}\|{tenant}\|{externalSub}"`) so `sub` is globally unique. `Actor` equality is identity-based on `Id` only — cross-IdP collisions are a real privilege-escalation risk. |
| `Lifetime` | `TimeSpan` | 5 minutes | No | Minted-token lifetime (`exp - iat`). Startup validation rejects values outside `[1 minute, 30 minutes]`. Pair with downstream `ClockSkew = 30s` (Recipe 33) so the replay window stays under ~6 minutes. |

### Behavioral notes

- **Per-cluster, not per-route.** v1 limitation: all per-cluster callbacks key on `ClusterConfig`. Two routes hitting the same cluster cannot have different audiences in v1.
- **No token caching.** Every request mints a fresh JWT. Caching is deferred to v1.1 (cache-key canonicalization, multi-instance miss rates, revocation SLA are nontrivial).
- **Transform always overwrites `Authorization`.** No `StripOriginalAuthorizationHeader` option; no `PreserveOriginalTokenAs`. Forwarding the original token alongside the gateway-minted one creates a downstream confusion attack (which header is authoritative?).
- **No actor → upstream `Authorization` is CLEARED.** When `IActorProvider` returns `Maybe<Actor>.None`, the transform sets `requestContext.ProxyRequest.Headers.Authorization = null` before YARP forwards the request. This fail-closed posture (P4 round-1 security review) prevents the upstream (external) bearer token from reaching the downstream service via YARP's default header-copy. Downstream policy decides whether anonymous is allowed.

---

## TrellisActorForwardingServiceCollectionExtensions

```csharp
public static IReverseProxyBuilder AddTrellisActorForwarding(
    this IReverseProxyBuilder builder,
    Action<TrellisActorForwardingOptions> configure);
```

Registers the actor-forwarding transform pipeline on YARP. Wires:

- `TrellisActorForwardingOptions` (validated at startup via `ValidateOnStart`).
- `TrellisActorForwardingOptionsValidator` (rejects symmetric keys, missing `kid`, lifetime outside `[1m, 30m]`, null callbacks, rotation-ring `kid` collisions).
- `TrellisActorJwtMinter` (singleton; holds the cached `JsonWebTokenHandler`).
- `TimeProvider` (`TryAddSingleton` — consumers can pre-register a test `FakeTimeProvider`).
- `TrellisActorForwardingTransformProvider` (the per-cluster build-time transform that adds the per-request transform).
- `TrellisActorForwardingRegistrationValidator` (`IHostedLifecycleService` — fails host start if no `IActorProvider` is registered).

**Caller responsibility — register `IActorProvider`.** This extension does NOT register an `IActorProvider`. The gateway typically uses `AddClaimsActorProvider` or `AddEntraActorProvider` from `Trellis.Asp` to hydrate the actor from the upstream JWT (the JWT the gateway validated at its boundary). Missing `IActorProvider` registration is a startup error, not a per-request error — the registration validator fails the host at startup with explicit guidance.

**YARP composition.** Place this call immediately after `services.AddReverseProxy().LoadFromConfig(...)`. The transform pipeline is built once per cluster at startup; per-request work resolves a scoped `IActorProvider` and the singleton `TrellisActorJwtMinter`.

---

## TrellisDiscoveryEndpointRouteBuilderExtensions

```csharp
public static IEndpointConventionBuilder MapTrellisDiscoveryEndpoint(
    this IEndpointRouteBuilder endpoints,
    string? oidcPath = null,        // defaults to "/.well-known/openid-configuration"
    string? jwksPath = null);       // defaults to "/.well-known/jwks.json"
```

Publishes the gateway's OIDC discovery document and JWKS as anonymous, cacheable HTTP endpoints. The discovery document advertises `TrellisActorForwardingOptions.Issuer` as the issuer and `PublicBaseUrl` joined with `jwksPath` as the `jwks_uri`. The JWKS document contains every key in the active rotation ring (current `SigningCredentials.Key` + every entry in `PreviousSigningKeys`).

**Anonymous endpoints.** Both endpoints are intentionally anonymous — JWKS MUST be reachable by downstream services before they have ever validated a token (chicken-and-egg). The published material is exclusively the asymmetric public keys and metadata about them; the corresponding private keys NEVER leave the gateway process.

**URLs come from options, never request context.** `HttpRequest.Host` is spoofable behind reverse proxies. Both endpoints construct their absolute URLs exclusively from `TrellisActorForwardingOptions.PublicBaseUrl` (startup-validated absolute) joined with the literal paths supplied here. Verified by `TrellisDiscoveryEndpointTests.MapTrellisDiscoveryEndpoint_JwksUri_IgnoresHttpRequestHostHeader`.

**Symmetric-key defense in depth.** The JWKS builder refuses to serialize any symmetric key, even though startup validation already rejects them. The two-layer check survives a future refactor that loosens validation and matches the v1-only-asymmetric contract.

**Private-key fields explicitly stripped.** The JWKS builder writes only the public components (`n`, `e` for RSA; `crv`, `x`, `y` for EC) plus the metadata fields (`kty`, `use`, `alg`, `kid`). Private components (`d`, `p`, `q`, `dp`, `dq`, `qi`, `k`) are explicitly not serialized — defense in depth in case a future `JsonWebKeyConverter` revision starts populating private components and a consumer hands the minter the private key.

---

## Internal JWT contract v1

Every minted JWT carries this exact claim set, matching the consumer-side `TrellisInternalJwtActorProvider` defaults:

| Claim | Value | Notes |
|---|---|---|
| `iss` | `options.Issuer` | |
| `aud` | `options.AudiencePerCluster(cluster)` | |
| `sub` | `options.ActorIdResolver(actor)` | Namespace via `ActorIdResolver` when fronting multiple IdPs. |
| `jti` | `Guid.NewGuid().ToString("N")` | Fresh per token; audit-log correlation key. |
| `iat` / `nbf` / `exp` | NumericDate (seconds since unix epoch) | `iat` = `nbf` = `TimeProvider.GetUtcNow()`; `exp` = `iat + options.Lifetime`. |
| `permissions` | Multi-valued (JSON array) | One element per projected permission. NEVER comma-joined or JSON-stringified — the consumer's strict-shape check rejects both. |
| `forbidden_permissions` | Multi-valued (JSON array) | Same shape as `permissions`. Empty array when projection result is empty. |
| `trellis_actor_contract_version` | `"1"` | Sentinel asserting the contract version. |
| `trellis_permissions_count` | Decimal string | Count of emitted `permissions` claims (including `"0"` for empty). |
| `trellis_forbidden_permissions_count` | Decimal string | Count of emitted `forbidden_permissions` claims (including `"0"` for empty). **Always emitted, even when zero** — the deny-overrides-allow contract integrity invariant. |
| One claim per `ProjectAttributes` entry | The entry value (single-valued) | Claim name = the entry key. |

The JWT header carries `alg` (matches `SigningCredentials.Algorithm`) and `kid` (matches `SigningCredentials.Key.KeyId`).

---

## Audit-log redaction contract

`TrellisActorJwtMinter` emits one `[LoggerMessage]` event per mint (Debug-level success, Error-level failure, Debug-level no-actor skip). All three events carry low-cardinality metadata only:

| Safe to log | Never logged |
|---|---|
| Cluster id | Raw JWT (the compact JWS string) |
| `kid` (signing key identifier) | Raw claim values (`sub`, permission strings, attribute values) |
| `jti` (fresh GUID per token — the audit-correlation key) | Actor id (the namespaced or raw `actor.Id.Value`) |
| `iss` / `aud` (gateway-controlled literals) | Tenant IDs / attribute values |
| `exp` (unix-seconds expiration timestamp) | Exception message (failure path) — can carry secret material from the key handle |
| Permission count, forbidden count | |
| Exception **type** name (failure path) | |

The `jti` makes every minted token correlatable to its mint event without leaking actor identity — this is the operational correlation key referenced in Recipe 34's emergency revocation procedure ("cross-reference `jti` against downstream services' authentication logs"). Redaction verified by `TrellisActorForwardingRequestTransformTests.ApplyAsync_AuditLog_ContainsKidAndCountsButNoActorIdNoClaimValues` which hard-asserts that no claim-value string, no actor id string, and the raw JWT do not appear in any log entry across the full event surface.

---

## See also

- [Recipe 33](trellis-api-cookbook.md#recipe-33--strict-addjwtbearer-validation-profile-for-usetrellisinternaljwtactor) — strict `AddJwtBearer` profile for the downstream consumer side.
- [Recipe 34](trellis-api-cookbook.md#recipe-34--microservices-behind-yarp-end-to-end) — end-to-end gateway + downstream worked example, key-rotation runbook, emergency revocation procedure.
- [`TrellisInternalJwtActorProvider`](trellis-api-asp.md#trellisinternaljwtactorprovider) — consumer-side companion that hydrates the full `Actor` from the JWT this package mints.
- [`Trellis.Asp` README](https://www.nuget.org/packages/Trellis.Asp) — Path B framing in the microservices section.
