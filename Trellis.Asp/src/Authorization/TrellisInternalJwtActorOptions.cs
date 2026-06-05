namespace Trellis.Asp.Authorization;

using System.Collections.Generic;
using Trellis.Authorization;

/// <summary>
/// Configuration options for <c>TrellisInternalJwtActorProvider</c>. Maps a verified
/// internal-network JWT (typically minted by a trusted gateway running the
/// <c>Trellis.Yarp</c> transform or an equivalent third-party gateway implementing the
/// same contract) onto the <see cref="Actor"/> surface — including
/// <see cref="Actor.ForbiddenPermissions"/> and <see cref="Actor.Attributes"/> the
/// stock <see cref="ClaimsActorProvider"/> intentionally omits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Trust model.</b> This provider expects the JWT to have been validated by
/// ASP.NET Core's <c>JwtBearerHandler</c> (or an equivalent
/// <see cref="System.Security.Claims.ClaimsPrincipal"/>-producing scheme). The provider
/// itself does NOT validate the JWT signature, issuer, audience, or lifetime — it only
/// translates the claims of an already-authenticated principal into an
/// <see cref="Actor"/>. The cookbook recipe accompanying this provider mandates the strict
/// <c>AddJwtBearer</c> validation profile (<c>ValidateIssuer</c>, <c>ValidateAudience</c>,
/// <c>ValidateLifetime</c>, <c>RequireSignedTokens</c>, tight <c>ClockSkew</c>); the
/// runtime cross-checks below (<see cref="ExpectedIssuer"/>,
/// <see cref="ExpectedAudience"/>) are defense-in-depth for consumers who haven't followed
/// the strict profile yet.
/// </para>
/// <para>
/// <b>Auth-scheme binding.</b> Unlike <see cref="ClaimsActorProvider"/> (which reads the
/// first authenticated identity from <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/>),
/// this provider explicitly authenticates the configured <see cref="AuthenticationScheme"/>
/// via <c>HttpContext.AuthenticateAsync(scheme)</c> and reads the resulting
/// <see cref="System.Security.Claims.ClaimsPrincipal"/>. A misconfigured middleware, dev-loopback
/// identity, or custom handler that plants a <see cref="System.Security.Claims.ClaimsPrincipal"/>
/// with matching claim names on <c>HttpContext.User</c> WITHOUT going through the configured
/// scheme would otherwise silently translate forged claims. This binding is the canonical
/// fail-closed posture for an internal-JWT contract.
/// </para>
/// <para>
/// <b>Contract integrity (sentinel + count claims).</b> The provider expects the gateway to
/// mint three contract claims so that an intermediate proxy stripping the deny-claim set
/// cannot be confused with the gateway evaluating the deny-claim set to empty (the latter
/// is normal; the former is a privilege-escalation footgun because deny-overrides-allow has
/// nothing to deny when the deny set is silently empty):
/// </para>
/// <list type="bullet">
///   <item><description><see cref="ContractVersionClaim"/> — must equal <see cref="ExpectedContractVersion"/>.</description></item>
///   <item><description><see cref="PermissionsCountClaim"/> — decimal integer, must equal the observed number of <see cref="PermissionsClaim"/> claims on the principal.</description></item>
///   <item><description><see cref="ForbiddenPermissionsCountClaim"/> — same shape for the deny set; MUST be emitted as <c>"0"</c> when the set is empty (empty MUST NOT be indistinguishable from absent).</description></item>
/// </list>
/// <para>
/// Mismatches at any step cause the provider to return <see cref="Maybe{T}.None"/>, which
/// the mediator pipeline maps to <see cref="Trellis.Error.AuthenticationRequired"/> (HTTP 401).
/// </para>
/// </remarks>
public sealed class TrellisInternalJwtActorOptions
{
    /// <summary>
    /// The ASP.NET Core authentication scheme to authenticate against per request. The provider
    /// calls <c>HttpContext.AuthenticateAsync(scheme)</c> and reads the resulting principal —
    /// NEVER trusts <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/> directly. Defaults
    /// to <c>"Bearer"</c>.
    /// </summary>
    public string AuthenticationScheme { get; set; } = "Bearer";

    /// <summary>
    /// The claim type used to resolve <see cref="Actor.Id"/>. Defaults to <c>"sub"</c> (RFC 7519
    /// / OIDC standard subject claim). The same short↔long claim-name fallback machinery used by
    /// <see cref="ClaimsActorProvider"/> is applied here so the default just-works against both
    /// <c>JwtBearerOptions.MapInboundClaims = true</c> and <c>false</c>.
    /// </summary>
    public string ActorIdClaim { get; set; } = "sub";

    /// <summary>
    /// The claim type used to populate <see cref="Actor.Permissions"/>. Multi-valued — each
    /// <see cref="System.Security.Claims.Claim"/> instance of this claim type contributes one
    /// permission. Defaults to <c>"permissions"</c>.
    /// </summary>
    public string PermissionsClaim { get; set; } = "permissions";

    /// <summary>
    /// The claim type used to populate <see cref="Actor.ForbiddenPermissions"/>. Multi-valued.
    /// Defaults to <c>"forbidden_permissions"</c>.
    /// </summary>
    public string ForbiddenPermissionsClaim { get; set; } = "forbidden_permissions";

    /// <summary>
    /// Map from logical attribute names exposed via <see cref="Actor.Attributes"/> to the
    /// underlying claim type emitted on the JWT. Use <see cref="ActorAttributes"/> constants for
    /// well-known keys (<c>tenant_id</c>, <c>mfa</c>, <c>ip</c>). Empty by default — only
    /// attributes explicitly mapped flow into the resulting <see cref="Actor.Attributes"/>.
    /// </summary>
    public Dictionary<string, string> AttributeClaimMap { get; set; } = [];

    /// <summary>
    /// Attribute names that MUST be present (and non-empty) on every request. Each name MUST
    /// also be a key in <see cref="AttributeClaimMap"/> (startup-validated). When a required
    /// attribute is missing, empty, or duplicated on the JWT, the provider fails closed with
    /// <see cref="Maybe{T}.None"/>.
    /// </summary>
    /// <remarks>
    /// Required attributes are the framework's defense against gateway-side mints that
    /// accidentally omit a tenant-isolation claim. A downstream that reads
    /// <c>actor.Attributes["tenant_id"]</c> with a default/wildcard fallback would otherwise
    /// silently cross tenants if the gateway mint omitted the claim.
    /// </remarks>
    public IReadOnlyList<string> RequiredAttributes { get; set; } = [];

    /// <summary>
    /// Optional expected issuer (<c>iss</c> claim) for runtime defense-in-depth. When non-empty,
    /// the provider compares the JWT's <c>iss</c> claim ordinal-equal to this value and fails
    /// closed on mismatch. NOT a substitute for <c>JwtBearerOptions.TokenValidationParameters.ValidIssuer</c>
    /// — the cookbook mandates issuer validation be configured at the <c>AddJwtBearer</c>
    /// level too.
    /// </summary>
    public string ExpectedIssuer { get; set; } = "";

    /// <summary>
    /// Optional expected audience (<c>aud</c> claim) for runtime defense-in-depth. When
    /// non-empty, the provider requires at least one <c>aud</c> claim ordinal-equal to this
    /// value and fails closed on mismatch.
    /// </summary>
    public string ExpectedAudience { get; set; } = "";

    /// <summary>
    /// When <c>true</c> (default), permission, forbidden-permission, and mapped-attribute claim
    /// values containing commas or starting with <c>[</c> / <c>{</c> are rejected — these
    /// shapes indicate a buggy gateway mint that comma-joined a set or serialized JSON into a
    /// single claim value, which would silently create one bogus permission named
    /// <c>"read,write,admin"</c> instead of three separate permissions. Set <c>false</c> only
    /// when a gateway legitimately mints values with commas (rare; not recommended).
    /// </summary>
    public bool StrictClaimShape { get; set; } = true;

    /// <summary>
    /// Escape hatch to disable the startup-validator's rule that rejects registered JWT claim
    /// names (<c>iss</c>, <c>aud</c>, <c>exp</c>, <c>nbf</c>, <c>iat</c>, <c>jti</c>,
    /// <c>sub</c>) as permission / forbidden-permission / attribute claim sources. The default
    /// (<c>false</c>) blocks an accidental privilege-escalation footgun where a consumer
    /// configures <c>PermissionsClaim = "iss"</c> and the gateway-controlled issuer string is
    /// treated as a permission grant. Setting this to <c>true</c> waives that protection;
    /// document the rationale in your composition root if you do.
    /// </summary>
    public bool UnsafeAllowRegisteredClaimNames { get; set; }

    /// <summary>
    /// The claim type carrying the contract-version sentinel. Defaults to
    /// <c>"trellis_actor_contract_version"</c>. Override only when interoperating with a
    /// third-party gateway that mints a different sentinel name with the same semantics; set
    /// <see cref="ExpectedContractVersion"/> to the version literal that gateway emits.
    /// </summary>
    public string ContractVersionClaim { get; set; } = "trellis_actor_contract_version";

    /// <summary>
    /// The claim type carrying the permissions count. Defaults to
    /// <c>"trellis_permissions_count"</c>. The value MUST be a decimal integer
    /// (no sign, no whitespace, invariant culture); the provider rejects malformed values.
    /// </summary>
    public string PermissionsCountClaim { get; set; } = "trellis_permissions_count";

    /// <summary>
    /// The claim type carrying the forbidden-permissions count. Defaults to
    /// <c>"trellis_forbidden_permissions_count"</c>. The gateway MUST emit this claim with
    /// value <c>"0"</c> when the forbidden set is empty so empty cannot be confused with
    /// absent.
    /// </summary>
    public string ForbiddenPermissionsCountClaim { get; set; } = "trellis_forbidden_permissions_count";

    /// <summary>
    /// The expected contract-version literal (<see cref="ContractVersionClaim"/> value). The
    /// provider fails closed when the observed value differs. Defaults to <c>"1"</c>.
    /// </summary>
    public string ExpectedContractVersion { get; set; } = "1";

    /// <summary>
    /// HTTP request headers that contribute to the actor's identity, surfaced via
    /// <c>IProvideActorVaryHeaders</c> so cached responses correctly partition by actor.
    /// Defaults to <c>["Authorization"]</c> for the typical Bearer-scheme case. When
    /// <see cref="AuthenticationScheme"/> is configured to a non-Bearer scheme (cookies, mTLS,
    /// custom handler), override this with the appropriate header(s); failing to do so allows
    /// an intermediate HTTP cache to serve actor A's response to actor B's request when
    /// consumers call <c>HttpResponseOptionsBuilder.VaryForActor()</c>.
    /// </summary>
    public IReadOnlyCollection<string> VaryByHeaders { get; set; } = ["Authorization"];
}
