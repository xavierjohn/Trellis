# Trellis.Yarp

YARP gateway integration for Trellis. Re-mints a per-cluster internal JWT from the full Trellis `Actor` (id + permissions + forbidden permissions + ABAC attributes), exposes an OIDC discovery + JWKS endpoint pair so downstream services can use `AddJwtBearer(o => o.Authority = gatewayUrl)` for transparent key rotation, and emits redacted audit telemetry on every mint.

Pairs with the consumer-side `TrellisInternalJwtActorProvider` in `Trellis.Asp`.

## Key features

- **`AddTrellisActorForwarding`** — `IReverseProxyBuilder` extension; per-request transform that mints a fresh per-cluster JWT from the full `Actor` and overwrites the upstream `Authorization` header.
- **`MapTrellisDiscoveryEndpoint`** — exposes `/.well-known/openid-configuration` + `/.well-known/jwks.json`. JWKS includes every key in the active rotation ring.
- **Asymmetric-only signing**, `kid` required on every key (startup-validated).
- **Sentinel + count claims** — `trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count` (always emitted, even when zero) + fresh `jti` per token. Detects the privilege-escalation footgun where a misbehaving proxy strips the deny-permission set.
- **Redacted audit telemetry** — every mint emits a `[LoggerMessage]` event carrying only low-cardinality metadata: `kid`, `jti`, `iss`, `aud`, `exp` (unix-seconds), and projected permission / forbidden counts. NEVER the raw JWT, raw claim values, or actor IDs.

## Security boundary

Signing-key compromise = full identity spoof until key revocation propagates. Mitigations: short token lifetimes (capped `[1m, 30m]` at startup), `kid`-aware overlapping JWKS rotation, audit-log redaction, emergency revocation procedure.

**Not AOT-compatible** (YARP itself is not AOT-clean).

See the [Trellis cookbook](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html) for the full "Microservices behind YARP, end-to-end" recipe.
