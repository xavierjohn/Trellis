# Trellis.Yarp

YARP gateway integration for Trellis. Re-mints a per-cluster internal JWT from the full Trellis `Actor` (id + permissions + forbidden permissions + ABAC attributes), exposes an OIDC discovery + JWKS endpoint pair so downstream services can configure `AddJwtBearer(o => o.Authority = gatewayUrl)` for transparent key rotation, and emits redacted audit telemetry on every mint.

Pairs with the consumer-side `TrellisInternalJwtActorProvider` in `Trellis.Asp` (Recipe 33 of the Trellis cookbook documents the consumer side).

## Key features

- **`AddTrellisActorForwarding`** — `IReverseProxyBuilder` extension that hooks a per-request transform into YARP, captures `ClusterConfig` at transform-build time, mints a fresh per-cluster JWT from the full Trellis `Actor`, and overwrites the upstream `Authorization` header.
- **`MapTrellisDiscoveryEndpoint`** — exposes `/.well-known/openid-configuration` and `/.well-known/jwks.json` constructed from the configured `Issuer` and `PublicBaseUrl`. JWKS publishes the active `SigningCredentials.Key` plus every entry in `PreviousSigningKeys` verbatim — so downstream services using `JwtBearerHandler` auto-refresh transparently during a rotation. **The operator is responsible for removing entries from `PreviousSigningKeys` once the rotation overlap window expires** (token-lifetime + clock-skew); the JWKS endpoint does not filter by age.
- **Asymmetric-only signing.** v1 rejects symmetric keys at startup. Publishing symmetric keys in JWKS would leak the signing secret; refusing to publish them silently breaks the "downstream uses `AddJwtBearer(o.Authority = gateway)`" discovery story. Asymmetric-only is the coherent v1 model.
- **`kid` required on every signing credential.** Startup-validated. Every minted JWT emits `kid` in the header so downstream `JwtBearerHandler` (and air-gapped static-key-ring consumers) can resolve the right key during rotation.
- **Sentinel + count claims** (contract with `TrellisInternalJwtActorProvider`). Every minted JWT includes `trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count` (always emitted, even when zero, to distinguish empty from absent — the deny-overrides-allow contract integrity invariant). Plus a fresh `jti` per token for audit correlation.
- **Redacted audit telemetry.** Every mint emits a `[LoggerMessage]` event with only low-cardinality metadata: `kid`, `jti`, `iss`, `aud`, `exp` (unix-seconds), and the projected `permissions_count` / `forbidden_permissions_count` (counts of what's actually emitted in the token, not the source actor's counts). NEVER logs the JWT body, raw claim values, actor IDs, or PII.

## Security boundary

`Trellis.Yarp` treats the gateway as the authority for the downstream-internal trust boundary. **Signing-key compromise = full identity spoof until key revocation propagates.** Mitigations baked into the package:

- Short token lifetimes (default 5 minutes; capped to `[1m, 30m]` at startup validation).
- `kid`-aware overlapping JWKS rotation (active + previous keys exposed in JWKS for the rotation window).
- Emergency revocation procedure: drop the compromised `kid` from JWKS, redeploy the gateway, restart downstream services to flush their cached config.
- Audit-log redaction (every mint correlatable via `jti` without leaking claim contents).

The cookbook recipe ("Microservices behind YARP, end-to-end") documents the full operational runbook.

## When NOT to use

- **AOT-only deployments.** `Trellis.Yarp` is not AOT-compatible (YARP itself is not AOT-clean). Use the Path A pass-through pattern (Recipe 7) instead — the gateway just forwards the validated external JWT.
- **A→B service-to-service calls.** v1 is ingress-only. Cross-service propagation is the user's responsibility (or use `Microsoft.Identity.Web` OBO when external resource servers are involved).
- **Symmetric signing requirement.** Out of scope for v1. Use a third-party JWT-minting layer or wait for v1.1.

## See also

- [Recipe 33](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html#recipe-33--strict-addjwtbearer-validation-profile-for-usetrellisinternaljwtactor) — strict `AddJwtBearer` profile for the downstream side.
- `TrellisInternalJwtActorProvider` in `Trellis.Asp.Authorization` — the consumer-side companion that hydrates the full `Actor` from the JWT this package mints.
- `Trellis.Asp` README — Path B (Trellis internal JWT) framing in the microservices section.
