namespace Trellis.Yarp;

using System;
using System.Collections.Generic;
using global::Microsoft.IdentityModel.Tokens;
using global::Yarp.ReverseProxy.Configuration;
using Trellis.Authorization;

/// <summary>
/// Configuration for the Trellis YARP actor-forwarding transform. The transform
/// captures <see cref="ClusterConfig"/> at <c>TransformBuilderContext</c> time, hydrates
/// the full <see cref="Actor"/> on every request, and mints a fresh per-cluster JWT
/// the downstream consumer (typically <c>TrellisInternalJwtActorProvider</c> in
/// <c>Trellis.Asp</c>) hydrates back into the same <see cref="Actor"/> surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Trust model.</b> The gateway is the authority for the downstream-internal trust
/// boundary. Signing-key compromise = full identity spoof until key revocation propagates.
/// Mitigations baked into the package: short token lifetimes (<see cref="Lifetime"/>
/// capped to <c>[1m, 30m]</c> at startup validation), <c>kid</c>-aware overlapping JWKS
/// rotation (the discovery endpoint exposes every key in the rotation ring), audit-log
/// redaction, emergency revocation procedure. The cookbook recipe accompanying this
/// package documents the operational runbook.
/// </para>
/// <para>
/// <b>Asymmetric only.</b> v1 rejects symmetric keys at startup. Publishing symmetric
/// keys in JWKS would leak the signing secret; refusing to publish them silently breaks
/// the "downstream uses <c>AddJwtBearer(o =&gt; o.Authority = gateway)</c>" discovery
/// story. Asymmetric-only is the coherent v1 model. Defense in depth: the JWKS endpoint
/// builder ALSO refuses symmetric keys even though startup validation already rejects them.
/// </para>
/// <para>
/// <b>Per-cluster, not per-route.</b> v1 limitation: <see cref="AudiencePerCluster"/>,
/// <see cref="ProjectPermissionsFor"/>, <see cref="ProjectForbiddenFor"/>, and
/// <see cref="ProjectAttributes"/> all key on <see cref="ClusterConfig"/>. Two routes
/// hitting the same cluster cannot have different audiences in v1. Per-route projection
/// is a v1.1 candidate.
/// </para>
/// <para>
/// <b>No token caching in v1.</b> Every request mints a fresh JWT. Caching is deferred
/// to v1.1 because it introduces correctness surprises around cache-key canonicalization
/// (which fields of <see cref="Actor"/> participate?), multi-instance miss rates, and
/// revocation SLA (a cached token must be invalidated as soon as the actor's permission
/// set changes; that propagation contract needs explicit design).
/// </para>
/// <para>
/// <b>No <c>StripOriginalAuthorizationHeader</c> option.</b> The transform always
/// overwrites the inbound <c>Authorization</c> header. Forwarding the original token
/// alongside the gateway-minted one creates a downstream confusion attack (which header
/// is authoritative?). A <c>PreserveOriginalTokenAs</c> option is reserved for v1.1 if
/// real demand surfaces.
/// </para>
/// </remarks>
public sealed class TrellisActorForwardingOptions
{
    /// <summary>
    /// JWT <c>iss</c> claim value AND the OIDC discovery document's <c>issuer</c> field.
    /// Required. Must be a non-empty string. Conventionally a URL identifying the gateway
    /// (e.g., <c>"https://gateway.internal"</c>) so downstream consumers can pair
    /// <see cref="PublicBaseUrl"/> with <c>AddJwtBearer(o =&gt; o.Authority = "...")</c>.
    /// </summary>
    public required string Issuer { get; set; }

    /// <summary>
    /// Asymmetric signing credential used to sign every minted JWT. Required.
    /// <see cref="SigningCredentials.Key"/> MUST be asymmetric (typically
    /// <see cref="RsaSecurityKey"/> or <see cref="ECDsaSecurityKey"/>) and MUST have a
    /// non-empty <see cref="SecurityKey.KeyId"/> (the <c>kid</c>). Startup validation
    /// rejects symmetric keys, null keys, and missing <c>kid</c>.
    /// </summary>
    /// <remarks>
    /// During key rotation, populate <see cref="PreviousSigningKeys"/> with the
    /// outgoing key(s) so they remain trusted in the published JWKS for the duration of
    /// the rotation overlap window (see the cookbook's rotation runbook).
    /// </remarks>
    public required SigningCredentials SigningCredentials { get; set; }

    /// <summary>
    /// Previous-generation signing keys still trusted during a rotation overlap window.
    /// Each entry MUST be asymmetric and MUST have a non-empty <c>kid</c>. These keys
    /// are NOT used to mint new tokens (the current <see cref="SigningCredentials"/>
    /// signs everything), but they ARE published in JWKS so downstream services
    /// validating tokens minted by the previous generation continue to succeed during
    /// the overlap window. Empty list (the default) means no rotation is in flight.
    /// </summary>
    public IReadOnlyList<SecurityKey> PreviousSigningKeys { get; set; } = [];

    /// <summary>
    /// Public base URL the gateway is reachable at, used to build absolute URLs in the
    /// OIDC discovery document (<c>jwks_uri</c>, <c>issuer</c>). Required. MUST be
    /// absolute. NOT inferred from <see cref="Microsoft.AspNetCore.Http.HttpRequest"/>
    /// because <c>HttpRequest.Host</c> is spoofable behind reverse proxies and could
    /// inject attacker-controlled discovery URLs into the published document.
    /// </summary>
    public required Uri PublicBaseUrl { get; set; }

    /// <summary>
    /// Selects the JWT <c>aud</c> claim value for a given destination cluster. Defaults
    /// to <see cref="ClusterConfig.ClusterId"/>. Override to a per-cluster audience
    /// literal (e.g., <c>cluster =&gt; "incidents-service"</c>) so each downstream service
    /// can pin <c>JwtBearerOptions.Audience</c> to a unique value and reject tokens
    /// minted for any other cluster — the canonical defense against the cross-audience
    /// confusion attack where an attacker steals a token minted for cluster A and
    /// replays it against cluster B.
    /// </summary>
    public Func<ClusterConfig, string> AudiencePerCluster { get; set; } =
        static cluster => cluster.ClusterId;

    /// <summary>
    /// Projects the source <see cref="Actor.Permissions"/> set onto the subset relevant
    /// to the destination cluster. Defaults to pass-through (every permission is
    /// forwarded). The cookbook recommends
    /// <c>(cluster, perms) =&gt; perms.Where(p =&gt; p.StartsWith(cluster.ClusterId + ".")).ToHashSet(...)</c>
    /// as the convention so downstream services receive only the permissions in their
    /// namespace.
    /// </summary>
    public Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>> ProjectPermissionsFor { get; set; } =
        static (_, perms) => perms;

    /// <summary>
    /// Projects the source <see cref="Actor.ForbiddenPermissions"/> set onto the subset
    /// relevant to the destination cluster. Defaults to pass-through (every forbidden
    /// permission is forwarded). Same shape and conventions as
    /// <see cref="ProjectPermissionsFor"/>. Note the contract integrity invariant:
    /// the count of forbidden permissions MUST always be emitted (even when zero) so
    /// downstream services can distinguish "deny set evaluated to empty" from "deny
    /// claim stripped by a misbehaving proxy" — see the sentinel-claim contract in
    /// <c>TrellisInternalJwtActorProvider</c>.
    /// </summary>
    public Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>> ProjectForbiddenFor { get; set; } =
        static (_, forbidden) => forbidden;

    /// <summary>
    /// Projects the source <see cref="Actor.Attributes"/> map onto the subset relevant
    /// to the destination cluster. Defaults to pass-through (every attribute is
    /// forwarded). Override to drop attributes that aren't authorized for the cluster
    /// (e.g., a tenant-internal attribute that should not cross a cluster trust boundary).
    /// </summary>
    public Func<ClusterConfig, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> ProjectAttributes { get; set; } =
        static (_, attrs) => attrs;

    /// <summary>
    /// Computes the JWT <c>sub</c> claim from the <see cref="Actor"/>. Defaults to
    /// <c>actor =&gt; actor.Id.Value</c>. Consumers fronting multiple IdPs / tenants
    /// MUST override this to mint a namespaced subject
    /// (e.g., <c>$"{issuer}|{tenant}|{externalSub}"</c>) so the resulting <c>sub</c> is
    /// globally unique across the federated identity surface.
    /// <see cref="Actor"/> equality is identity-based on <see cref="Actor.Id"/> only,
    /// so cross-IdP collisions are a real privilege-escalation risk. The cookbook
    /// recommends the namespaced shape.
    /// </summary>
    public Func<Actor, string> ActorIdResolver { get; set; } =
        static actor => actor.Id.Value;

    /// <summary>
    /// Minted-token lifetime (the <c>exp - iat</c> window). Defaults to 5 minutes.
    /// Startup validation rejects values outside <c>[1 minute, 30 minutes]</c> — the
    /// recommendation is short-lived tokens; the cap is defense against accidental
    /// mis-configuration that would expand the post-compromise spoof window. Pair with
    /// downstream <c>ClockSkew = TimeSpan.FromSeconds(30)</c> (see Recipe 33) so the
    /// effective replay window stays under ~6 minutes for the default lifetime.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);
}
