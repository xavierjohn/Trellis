namespace Trellis.Asp.Authorization;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// Configuration options for <see cref="ClaimsActorProvider"/>.
/// Controls which claims are used for actor identity and permissions.
/// </summary>
/// <remarks>
/// <para>
/// Default behavior maps the OIDC standard <c>sub</c> claim to
/// <see cref="Actor.Id"/> and the <c>permissions</c> claim to
/// <see cref="Actor.Permissions"/>. Override the claim names
/// to match your identity provider's token format.
/// </para>
/// <para>
/// <b>Nested-claim mapping</b> (ga-15). Both <see cref="ActorIdClaim"/> and
/// <see cref="PermissionsClaim"/> are matched against the flat
/// <see cref="System.Security.Claims.Claim.Type"/> string only — no JSON-path
/// or dotted traversal is performed. Two practical consequences:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Provider-prefixed claim names work as-is.</b> Set the option to the
///       full claim type the JWT handler emits, e.g.
///       <c>"http://schemas.microsoft.com/identity/claims/objectidentifier"</c>
///       (Entra <c>oid</c>) or <c>"extension_role"</c> (Entra External Identities
///       custom attribute). Whatever the issuer/handler exposes via
///       <c>ClaimsIdentity.FindFirst(name)</c> is what to put here.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>JSON-object claims are not auto-flattened.</b> When a JWT contains
///       a nested object (e.g. <c>{ "app_metadata": { "roles": [ ... ] } }</c>),
///       the JWT handler stores the value as the raw JSON string under
///       claim type <c>"app_metadata"</c>. To project nested values into
///       <see cref="Actor.Permissions"/>, subclass <see cref="ClaimsActorProvider"/>
///       and override <see cref="ClaimsActorProvider.GetCurrentActorAsync"/> to
///       parse the JSON yourself, or use <see cref="EntraActorProvider"/> with a
///       custom <see cref="EntraActorOptions.MapPermissions"/> delegate which
///       receives the full <see cref="System.Security.Claims.Claim"/> sequence.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Multi-valued JWT claims are flattened automatically</b> by
///       <c>JwtBearerHandler</c> — a JSON array such as
///       <c>"permissions": [ "orders:read", "orders:write" ]</c> arrives as
///       multiple <see cref="System.Security.Claims.Claim"/> instances of the
///       same <see cref="System.Security.Claims.Claim.Type"/>, which the default
///       provider already aggregates via <c>FindAll</c>.
///     </description>
///   </item>
/// </list>
/// </remarks>
public class ClaimsActorOptions
{
    /// <summary>
    /// The claim type used to resolve the actor's unique identifier.
    /// Defaults to <c>"sub"</c> (the RFC 7519 / OpenID Connect subject claim).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched against <see cref="System.Security.Claims.Claim.Type"/> verbatim;
    /// no dotted-path traversal. See the <see cref="ClaimsActorOptions"/> remarks
    /// for nested-claim guidance.
    /// </para>
    /// <para>
    /// <b>JwtBearer <c>MapInboundClaims</c> robustness.</b> ASP.NET Core's
    /// <c>JwtBearerOptions.MapInboundClaims</c> defaults to <see langword="true"/>,
    /// which remaps RFC 7519 short claim names (e.g. <c>"sub"</c>) onto WS-* long-form
    /// URNs (e.g. <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>)
    /// before they reach <see cref="System.Security.Claims.ClaimsIdentity"/>. If the
    /// configured <see cref="ActorIdClaim"/> is not found literally,
    /// <see cref="ClaimsActorProvider"/> automatically falls back to the well-known
    /// short↔long counterpart from the JWT inbound claim-name map — covering the
    /// OAuth2 / OIDC / Microsoft-identity subset that consumers realistically
    /// configure as an actor id (<c>sub</c>/<c>nameid</c> ↔
    /// <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>, <c>oid</c> ↔
    /// the Microsoft objectidentifier URN, <c>upn</c> ↔
    /// <see cref="System.Security.Claims.ClaimTypes.Upn"/>, <c>email</c> ↔
    /// <see cref="System.Security.Claims.ClaimTypes.Email"/>, <c>role</c>/
    /// <c>roles</c> ↔ <see cref="System.Security.Claims.ClaimTypes.Role"/>,
    /// <c>name</c>/<c>unique_name</c> ↔
    /// <see cref="System.Security.Claims.ClaimTypes.Name"/>, etc.) — so the default
    /// configuration just-works with either <c>MapInboundClaims = true</c> or
    /// <c>MapInboundClaims = false</c>. The fallback emits a debug-level log entry
    /// when it fires. Recommended for new services: set <c>MapInboundClaims = false</c>
    /// on <c>AddJwtBearer(...)</c> so claim names round-trip with their RFC names.
    /// </para>
    /// </remarks>
    public string ActorIdClaim { get; set; } = "sub";

    /// <summary>
    /// The claim type used to resolve the actor's permissions.
    /// Defaults to <c>"permissions"</c> (common convention in OIDC/JWT tokens).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched against <see cref="System.Security.Claims.Claim.Type"/> first; every matching
    /// claim contributes one entry. <see cref="ClaimsActorProvider"/> additionally queries the
    /// well-known short↔long counterpart from the JWT inbound claim-name map (<c>role</c>/
    /// <c>roles</c> ↔ <see cref="System.Security.Claims.ClaimTypes.Role"/>, etc.) and merges
    /// every match into a deduplicated set, so configurations like
    /// <c>PermissionsClaim = "roles"</c> or <c>PermissionsClaim = ClaimTypes.Role</c>
    /// just-work against either <c>JwtBearerOptions.MapInboundClaims = true</c> or
    /// <c>false</c>. The default <c>"permissions"</c> is not in the JWT inbound map, so its
    /// resolution remains literal-only. The fallback emits a debug-level log entry when it
    /// fires (i.e. when the configured name resolves nothing but a counterpart does). See
    /// the <see cref="ClaimsActorOptions"/> remarks for nested-claim guidance.
    /// </para>
    /// </remarks>
    public string PermissionsClaim { get; set; } = "permissions";

    /// <summary>
    /// When <see langword="true"/> (the default), <see cref="ClaimsActorProvider"/> emits
    /// startup-diagnostics log entries the first time a configured claim fails to resolve into
    /// the shape the provider expects: the configured <see cref="PermissionsClaim"/> resolving
    /// to zero entries on an authenticated identity that carries other claims, or to a single
    /// value that parses as a JSON object or array; and the configured
    /// <see cref="ActorIdClaim"/> resolving to nothing on an authenticated identity. Every
    /// diagnostic is throttled to fire at most once per application lifetime — they surface the
    /// silent-403 and silent-401 footguns without producing log spam.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why it exists.</b> The most common silent-403 cause is a misconfigured
    /// <see cref="PermissionsClaim"/> against a nested-JSON identity-provider claim shape
    /// (Auth0 <c>app_metadata.roles</c>, Azure B2C <c>extension_*</c> custom attributes,
    /// some Okta token shapes). The configured flat claim name finds nothing literal, the
    /// known-name fallback table does not cover the consumer's IdP, and the provider returns
    /// an empty permissions set — every <c>IAuthorize</c> command then short-circuits to
    /// 403 with no signal pointing at the claim mapping.
    /// </para>
    /// <para>
    /// The silent-401 variant is the same failure one step earlier: an unresolvable
    /// <see cref="ActorIdClaim"/> (typically a <c>JwtBearerOptions.MapInboundClaims</c>
    /// short↔long mismatch the fallback table does not cover) yields no actor at all, so a
    /// successfully authenticated caller is rejected with a bare 401.
    /// </para>
    /// <para>
    /// Set to <see langword="false"/> only when the diagnostics duplicate an existing health-check
    /// or claim-validation pipeline.
    /// </para>
    /// </remarks>
    public bool ValidateClaimShapeOnFirstUse { get; set; } = true;
}

/// <summary>
/// <see cref="IActorProvider"/> implementation that hydrates an <see cref="Actor"/>
/// from the current <see cref="HttpContext.User"/> using standard JWT/OIDC claims.
/// Works with any identity provider (Auth0, Keycloak, Okta, Entra, etc.).
/// </summary>
/// <remarks>
/// <para>
/// Register as scoped in DI via
/// <see cref="ServiceCollectionExtensions.AddClaimsActorProvider"/>.
/// Claim mapping is controlled by <see cref="ClaimsActorOptions"/>.
/// </para>
/// <para>
/// This provider assumes authentication has already occurred. It returns
/// <see cref="Maybe{T}.None"/> when the request has no usable authenticated actor —
/// no authenticated <see cref="System.Security.Claims.ClaimsIdentity"/>, or the configured
/// <see cref="ClaimsActorOptions.ActorIdClaim"/> is missing from the authenticated identity —
/// and the mediator authorization pipeline maps that to <see cref="Error.AuthenticationRequired"/>
/// (HTTP 401). It throws <see cref="InvalidOperationException"/> only when invoked outside
/// an HTTP request scope (no <c>HttpContext</c>); that is a configuration bug, not
/// authentication state, and surfaces as HTTP 500.
/// </para>
/// <para>
/// <b>Extending for nested or computed claims.</b> The default mapping is flat
/// (see the <see cref="ClaimsActorOptions"/> remarks). For provider-specific
/// shapes — JSON-object claims, dotted paths, claims that need to be merged with
/// an external store, or permissions derived from multiple raw claims — subclass
/// this provider and override <see cref="GetCurrentActorAsync"/>. The
/// <see cref="HttpContextAccessor"/> and <see cref="Options"/> properties are
/// <c>protected</c> precisely to support this. <see cref="EntraActorProvider"/>
/// is a worked example.
/// </para>
/// </remarks>
public class ClaimsActorProvider : IActorProvider, IProvideActorVaryHeaders
{
    /// <summary>
    /// HTTP request headers that contribute to this provider's actor identity. The default
    /// is <c>["Authorization"]</c> — the JWT-bearer assumption baked into the framework's
    /// bundled <c>AddClaimsActorProvider</c> / <c>AddEntraActorProvider</c> registration:
    /// the actor identity is derived from claims on the authenticated principal, which
    /// JwtBearer parses from the <c>Authorization</c> header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Non-bearer auth.</b> If your service authenticates with a different scheme
    /// (cookies, mTLS, forwarded headers, custom handler) the actor identity does NOT
    /// vary by <c>Authorization</c> and the default is wrong. Subclass
    /// <see cref="ClaimsActorProvider"/> and override
    /// <see cref="VaryByHeaders"/> to declare the correct header(s) — for example
    /// <c>["Cookie"]</c> for cookie-backed auth. Failing to do so allows an intermediate
    /// HTTP cache to serve actor A's response to actor B's request when consumers call
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/>.
    /// </para>
    /// <para>
    /// <b>Stability contract.</b> Overrides MUST return a collection whose contents do not
    /// change between requests. The framework re-reads <c>VaryByHeaders</c> on every
    /// response that opts into <c>VaryForActor()</c>; returning a mutable collection and
    /// mutating it between requests would emit inconsistent <c>Vary</c> headers across
    /// requests, breaking intermediate-cache partitioning. Return a frozen array literal
    /// (<c>["Cookie"]</c>) or a precomputed <see cref="System.Collections.Frozen.FrozenSet{T}"/>;
    /// do not return a <see cref="System.Collections.Generic.List{T}"/> field that the
    /// subclass mutates over time.
    /// </para>
    /// </remarks>
    public virtual IReadOnlyCollection<string> VaryByHeaders { get; } = ["Authorization"];

    /// <summary>
    /// Well-known short → long claim name pairs that match
    /// <c>JwtSecurityTokenHandler.DefaultInboundClaimTypeMap</c> /
    /// <c>JsonWebTokenHandler.DefaultInboundClaimTypeMap</c>. When
    /// <c>JwtBearerOptions.MapInboundClaims = true</c> (the ASP.NET Core default),
    /// the handler emits the long-form WS-* URN onto the <see cref="ClaimsIdentity"/>
    /// instead of the short RFC 7519 / OIDC claim name. The provider treats these
    /// pairs as equivalent: if the configured claim name is not found, the
    /// counterpart from this map is tried as a fallback (Postel's-law robustness
    /// against ASP.NET Core's legacy MapInboundClaims default — same shape that
    /// <see cref="EntraActorProvider"/> uses for its <c>oid</c> lookup).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bidirectional lookup is performed in <see cref="ResolveClaimWithFallback"/>:
    /// configured short → mapped long, configured long → mapped short.
    /// Hardcoded to avoid taking a dependency on
    /// <c>System.IdentityModel.Tokens.Jwt</c> from this package; the mapping table is
    /// effectively frozen — no new entries have been added to the JWT inbound map
    /// in years.
    /// </para>
    /// <para>
    /// Scope: this table covers the curated OAuth2 / OIDC / Microsoft-identity subset
    /// of the JWT inbound map — every claim type a consumer might realistically
    /// configure as <see cref="ClaimsActorOptions.ActorIdClaim"/> or
    /// <see cref="ClaimsActorOptions.PermissionsClaim"/> (subject identifier, roles,
    /// tenant, UPN, AuthN context, identity provider). The OAuth scope claim
    /// <c>scp</c> is intentionally omitted (see the inline comment on the table for
    /// the rationale). Operational / transport / device / certificate metadata claims
    /// from the inbound map (<c>clientip</c>, <c>cert*</c>, <c>device*</c>, etc.) are
    /// intentionally omitted: they are not authentication state and configuring them
    /// as an actor id or permission would be a misconfiguration in its own right.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<string, string> KnownShortToLongClaimNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // RFC 7519 / OIDC standard claims
            ["sub"] = ClaimTypes.NameIdentifier,
            ["nameid"] = ClaimTypes.NameIdentifier,
            ["name"] = ClaimTypes.Name,
            ["unique_name"] = ClaimTypes.Name,
            ["email"] = ClaimTypes.Email,
            ["role"] = ClaimTypes.Role,
            ["roles"] = ClaimTypes.Role,
            ["family_name"] = ClaimTypes.Surname,
            ["given_name"] = ClaimTypes.GivenName,
            ["gender"] = ClaimTypes.Gender,
            ["birthdate"] = ClaimTypes.DateOfBirth,
            ["website"] = ClaimTypes.Webpage,
            ["actort"] = ClaimTypes.Actor,
            // Microsoft identity platform claims (Entra v1.0 / v2.0, AD FS). Most are
            // single-valued (oid, tid, upn, idp, acr); "amr" is commonly multi-valued
            // (e.g. ["pwd", "mfa"]) and that case is handled by the multi-valued
            // ResolveAllClaimsWithFallback path.
            //
            // The OAuth scope claim "scp" is intentionally NOT included: its value
            // is space-delimited (RFC 6749 §3.3, e.g. "orders.read orders.write")
            // and the provider snapshots claim values verbatim into a FrozenSet, so
            // wiring the fallback would still leave Actor.HasPermission("orders.read")
            // returning false. OAuth scope-as-permission requires a custom provider
            // that splits the value (or the consumer setting MapInboundClaims = false
            // and configuring their own scope-splitting subclass).
            ["upn"] = ClaimTypes.Upn,
            ["oid"] = "http://schemas.microsoft.com/identity/claims/objectidentifier",
            ["tid"] = "http://schemas.microsoft.com/identity/claims/tenantid",
            ["idp"] = "http://schemas.microsoft.com/identity/claims/identityprovider",
            ["acr"] = "http://schemas.microsoft.com/claims/authnclassreference",
            ["amr"] = "http://schemas.microsoft.com/claims/authnmethodsreferences",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Inverse lookup table built from <see cref="KnownShortToLongClaimNames"/>: each long-form
    /// URN maps to the <em>set</em> of short forms that alias to it. Multi-valued because
    /// several short forms can collapse onto the same long form (e.g. <c>"sub"</c> and
    /// <c>"nameid"</c> both alias to <see cref="ClaimTypes.NameIdentifier"/>; <c>"name"</c> and
    /// <c>"unique_name"</c> both alias to <see cref="ClaimTypes.Name"/>; <c>"role"</c> and
    /// <c>"roles"</c> both alias to <see cref="ClaimTypes.Role"/>). The reverse fallback must
    /// iterate every candidate short form — picking only one canonical short form would
    /// silently fail to resolve tokens carrying the alternate short variant and reproduce
    /// the same silent-401 footgun this fallback exists to prevent.
    /// </summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> KnownLongToShortClaimNames =
        KnownShortToLongClaimNames
            .GroupBy(kvp => kvp.Value, StringComparer.Ordinal)
            .ToFrozenDictionary(
                g => g.Key,
                g => g.Select(kvp => kvp.Key).ToFrozenSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

    /// <summary>
    /// Throttle flag for the empty-permissions diagnostic; flips from 0 to 1 the first time
    /// the diagnostic fires for any instance. Static so the once-per-app-lifetime semantic
    /// is preserved across the request-scoped provider lifetime.
    /// </summary>
    private static int s_emptyPermissionsWarningFired;

    /// <summary>
    /// Throttle flag for the JSON-shaped-claim diagnostic; flips from 0 to 1 the first time
    /// the diagnostic fires for any instance.
    /// </summary>
    private static int s_jsonShapedClaimErrorFired;

    /// <summary>
    /// Throttle flag for the unresolvable-actor-id diagnostic; flips from 0 to 1 the first time
    /// the diagnostic fires for any instance.
    /// </summary>
    private static int s_unresolvedActorIdWarningFired;

    /// <summary>
    /// Test-only hook for resetting the static throttles. Internal so the test project can
    /// exercise every diagnostic path without relying on assembly-reload tricks.
    /// </summary>
    internal static void ResetDiagnosticThrottlesForTests()
    {
        s_emptyPermissionsWarningFired = 0;
        s_jsonShapedClaimErrorFired = 0;
        s_unresolvedActorIdWarningFired = 0;
    }

    private readonly ILogger<ClaimsActorProvider>? _logger;

    /// <summary>
    /// Initializes a new <see cref="ClaimsActorProvider"/>.
    /// </summary>
    /// <param name="httpContextAccessor">Accessor for the current request's <see cref="HttpContext"/>.</param>
    /// <param name="options">Claim-name mapping options.</param>
    /// <param name="logger">
    /// Optional logger; when supplied, emits a debug-level message naming the configured
    /// claim and the resolved counterpart each time the short↔long claim-name fallback
    /// resolves a value (either the actor id or one or more permission claims) that the
    /// configured literal claim name did not produce. Logging is suppressed when the
    /// configured claim resolves literally — the diagnostic targets the silent-401 /
    /// silent-403 footgun caused by <c>JwtBearerOptions.MapInboundClaims = true</c>;
    /// the merge case where both forms contribute is normal operation and would only
    /// add noise. The actor-id resolution emits at most one entry per request; the
    /// permissions resolution may emit one entry per resolved counterpart (forward
    /// long-form match, plus one per matched reverse short form) — typically zero or
    /// one, capped by the size of the well-known map.
    /// </param>
    public ClaimsActorProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<ClaimsActorOptions> options,
        ILogger<ClaimsActorProvider>? logger = null)
    {
        HttpContextAccessor = httpContextAccessor;
        Options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The HTTP context accessor used to retrieve the current user.
    /// </summary>
    protected IHttpContextAccessor HttpContextAccessor { get; }

    /// <summary>
    /// The configured claim mapping options.
    /// </summary>
    protected ClaimsActorOptions Options { get; }

    /// <inheritdoc />
    public virtual Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // HttpContext missing is genuinely exceptional: the provider was invoked outside an
        // HTTP request scope (e.g., from a background worker or test bootstrap). That's a
        // configuration bug, not authentication state — throw rather than return None, which
        // would mask the bug as a 401.
        var httpContext = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No HttpContext available. Ensure this is called within an HTTP request scope.");

        // The request has no authenticated identity (no Authorization header, anonymous-tolerant
        // endpoint, expired/invalid token accepted as anonymous, etc.). Client-error state →
        // Maybe.None → the mediator pipeline emits 401.
        var identity = httpContext.User.Identities.FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity;
        if (identity is null)
            return Task.FromResult(Maybe<Actor>.None);

        // Authenticated identity is present but the configured ActorIdClaim is missing. From
        // the framework's POV we can't identify the actor — same client-facing outcome as no
        // identity. (Whether this is a token-shape issue or a server-side option misconfig
        // is indistinguishable here; both want a 401 retry by the client.)
        //
        // Fallback: if the configured claim is one of the well-known JWT short↔long pairs
        // (e.g., "sub" ↔ ClaimTypes.NameIdentifier), also try its counterpart. This is the
        // robustness measure for ASP.NET Core's JwtBearerOptions.MapInboundClaims = true
        // default, which silently remaps RFC 7519 short names onto WS-* long-form URNs.
        var actorId = ResolveClaimWithFallback(identity, Options.ActorIdClaim);
        if (string.IsNullOrWhiteSpace(actorId))
        {
            MaybeEmitUnresolvedActorIdDiagnostic(identity);
            return Task.FromResult(Maybe<Actor>.None);
        }

        var permissions = ResolveAllClaimsWithFallback(identity, Options.PermissionsClaim)
            .Select(c => c.Value)
            .ToFrozenSet();

        MaybeEmitClaimShapeDiagnostics(identity, permissions);

        var actor = Actor.Create(actorId, permissions);
        return Task.FromResult(Maybe.From(actor));
    }

    /// <summary>
    /// Logger used for the one-off claim-shape diagnostics. Derived providers that construct the
    /// base with a <see langword="null"/> logger (because they own a differently typed one)
    /// override this so diagnostics raised on base-delegated resolution paths still reach a logger.
    /// </summary>
    protected virtual ILogger? DiagnosticLogger => _logger;

    /// <summary>
    /// Emits a one-time warning when an authenticated identity is present but the configured
    /// <see cref="ClaimsActorOptions.ActorIdClaim"/> resolved to nothing. Without it the caller
    /// sees a bare 401 with no server-side signal that the token shape — not the credential —
    /// is the problem. Honours <see cref="ClaimsActorOptions.ValidateClaimShapeOnFirstUse"/>.
    /// </summary>
    /// <param name="identity">The authenticated identity whose claims failed to yield an actor id.</param>
    /// <param name="overrideLogger">Logger to use instead of this instance's logger; derived providers pass their own.</param>
    protected void MaybeEmitUnresolvedActorIdDiagnostic(ClaimsIdentity identity, ILogger? overrideLogger = null)
    {
        var logger = overrideLogger ?? DiagnosticLogger;
        if (!Options.ValidateClaimShapeOnFirstUse || logger is null)
            return;

        // An identity with no claims at all is an empty/anonymous-shaped principal, not a
        // claim-mapping mistake; warning on it would fire for ordinary unauthenticated traffic.
        if (!identity.Claims.Any())
            return;

        if (Interlocked.CompareExchange(ref s_unresolvedActorIdWarningFired, 1, 0) != 0)
            return;

        LogUnresolvedActorIdOnAuthenticatedIdentity(
            logger,
            Options.ActorIdClaim,
            identity.Claims.Select(c => c.Type).Distinct().Take(10).ToArray());
    }

    /// <summary>
    /// Emits one-off startup diagnostics surfacing the two silent-403 footguns this provider
    /// cannot otherwise reach: (1) the configured <see cref="ClaimsActorOptions.PermissionsClaim"/>
    /// resolved to zero entries on an authenticated identity that carries other claims, and
    /// (2) the configured claim resolved to a single value that parses as a JSON object or
    /// array. Both fire at most once per application lifetime regardless of how many requests
    /// trigger them.
    /// </summary>
    /// <remarks>
    /// <c>protected</c> so derived providers (notably <see cref="NestedJsonPathClaimsActorProvider"/>)
    /// can invoke the same diagnostic after their override resolves permissions through a
    /// different path; without this hook the empty-permissions warning would not fire when the
    /// derived provider's path-traversal produced an empty set. The <paramref name="overrideLogger"/>
    /// parameter lets a derived provider pass its own logger when the base's logger slot was
    /// left null (the typical case when the derived constructor wires its own typed logger).
    /// </remarks>
    protected void MaybeEmitClaimShapeDiagnostics(ClaimsIdentity identity, FrozenSet<string> permissions, ILogger? overrideLogger = null)
    {
        var logger = overrideLogger ?? DiagnosticLogger;
        if (!Options.ValidateClaimShapeOnFirstUse || logger is null)
            return;

        // Diagnostic 1: empty permissions on an authenticated identity that has other claims.
        // The Auth0 `app_metadata.roles` shape is the canonical example — the JWT contains
        // claims, but none of them match the configured (flat) PermissionsClaim name.
        if (permissions.Count == 0
            && identity.Claims.Any()
            && Interlocked.CompareExchange(ref s_emptyPermissionsWarningFired, 1, 0) == 0)
        {
            LogEmptyPermissionsOnAuthenticatedIdentity(
                logger,
                Options.PermissionsClaim,
                identity.Claims.Select(c => c.Type).Distinct().Take(10).ToArray());
        }

        // Diagnostic 2: the configured PermissionsClaim resolved to a single JSON-shaped value.
        // That shape is what JwtSecurityTokenHandler does for nested claims (Auth0
        // `app_metadata`, Azure B2C `extension_*`, some Okta token shapes). Probe TWO sources:
        //   (a) the literal claim's raw value — catches the misconfigured-flat-provider case
        //       even when the resolved permissions set is empty (count == 0)
        //   (b) the resolved single permission value — catches the case where the consumer's
        //       PermissionsClaim resolved cleanly to one entry whose value is a JSON document
        //       (the override may have produced this via path traversal, or the flat resolver
        //       did when only one matching claim was present)
        if (s_jsonShapedClaimErrorFired != 0)
            return;

        string? jsonProbeSource = null;
        if (permissions.Count == 1)
        {
            jsonProbeSource = permissions.First();
        }
        else if (permissions.Count == 0)
        {
            jsonProbeSource = identity.FindFirst(Options.PermissionsClaim)?.Value;
        }
        // permissions.Count > 1 → multiple real permissions; the diagnostic does not apply.

        if (string.IsNullOrWhiteSpace(jsonProbeSource) || (jsonProbeSource[0] != '{' && jsonProbeSource[0] != '['))
            return;

        if (!LooksLikeJsonObjectOrArray(jsonProbeSource))
            return;

        if (Interlocked.CompareExchange(ref s_jsonShapedClaimErrorFired, 1, 0) == 0)
            LogJsonShapedClaim(logger, Options.PermissionsClaim);
    }

    private static bool LooksLikeJsonObjectOrArray(string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Multi-valued sibling of <see cref="ResolveClaimWithFallback"/>. Yields every claim on
    /// the identity that matches the configured claim name OR any counterpart in the curated
    /// <see cref="KnownShortToLongClaimNames"/> mapping table. Used for permissions resolution,
    /// where every contributing claim must be included in the resulting set; the caller's
    /// <see cref="FrozenSet{T}"/> construction dedupes overlapping values.
    /// </summary>
    /// <remarks>
    /// Logs a debug-level entry once per counterpart that fired (literal not found, fallback
    /// did find claims) so the diagnostic remains useful when the consumer has configured a
    /// claim name that's subject to <c>JwtBearerOptions.MapInboundClaims</c> remapping.
    /// <c>protected</c> so derived providers can use the same short↔long fallback when their
    /// own resolution path misses and they need to consult the flat claim shape as a backup.
    /// </remarks>
    protected IEnumerable<Claim> ResolveAllClaimsWithFallback(ClaimsIdentity identity, string configuredClaim)
    {
        var literalFound = false;
        foreach (var c in identity.FindAll(configuredClaim))
        {
            literalFound = true;
            yield return c;
        }

        // Forward direction: configured short form → its single long-form counterpart.
        if (KnownShortToLongClaimNames.TryGetValue(configuredClaim, out var longForm))
        {
            var counterpartFound = false;
            foreach (var c in identity.FindAll(longForm))
            {
                counterpartFound = true;
                yield return c;
            }

            if (counterpartFound && !literalFound)
                LogClaimNameFallback(_logger, configuredClaim, longForm);
        }

        // Reverse direction: configured long form → iterate every short form that maps to it.
        // Same "iterate every candidate" rule as ResolveClaimWithFallback.
        if (KnownLongToShortClaimNames.TryGetValue(configuredClaim, out var shortForms))
        {
            foreach (var shortForm in shortForms)
            {
                var counterpartFound = false;
                foreach (var c in identity.FindAll(shortForm))
                {
                    counterpartFound = true;
                    yield return c;
                }

                if (counterpartFound && !literalFound)
                    LogClaimNameFallback(_logger, configuredClaim, shortForm);
            }
        }
    }

    /// <summary>
    /// Looks up <paramref name="configuredClaim"/> on the identity; if not found, tries the
    /// well-known short↔long counterpart(s) from <see cref="KnownShortToLongClaimNames"/>.
    /// Returns the resolved value, or <see langword="null"/> when neither form is present.
    /// </summary>
    /// <remarks>
    /// The forward direction (configured short → long) has a single counterpart per short
    /// form. The reverse direction (configured long → short) iterates every short form that
    /// aliases to the long form (e.g., <c>NameIdentifier</c> ← {<c>sub</c>, <c>nameid</c>};
    /// <c>Name</c> ← {<c>name</c>, <c>unique_name</c>}; <c>Role</c> ← {<c>role</c>,
    /// <c>roles</c>}) — picking only one short form would silently fail to resolve tokens
    /// that carry the alternate variant.
    /// <c>protected</c> so derived providers can use the same short↔long fallback when their
    /// own resolution path misses and they need to consult the flat claim shape as a backup.
    /// </remarks>
    protected string? ResolveClaimWithFallback(ClaimsIdentity identity, string configuredClaim)
    {
        var literal = identity.FindFirst(configuredClaim)?.Value;
        if (literal is not null)
            return literal;

        // Forward direction: configured short form → its single long-form counterpart.
        if (KnownShortToLongClaimNames.TryGetValue(configuredClaim, out var longForm))
        {
            var forward = identity.FindFirst(longForm)?.Value;
            if (forward is not null)
            {
                LogClaimNameFallback(_logger, configuredClaim, longForm);
                return forward;
            }
        }

        // Reverse direction: configured long form → iterate every short form that maps to it.
        // Picking only one canonical short would miss tokens carrying the alternate variant
        // (e.g., "nameid" instead of "sub" for ClaimTypes.NameIdentifier).
        if (KnownLongToShortClaimNames.TryGetValue(configuredClaim, out var shortForms))
        {
            foreach (var shortForm in shortForms)
            {
                var reverse = identity.FindFirst(shortForm)?.Value;
                if (reverse is not null)
                {
                    LogClaimNameFallback(_logger, configuredClaim, shortForm);
                    return reverse;
                }
            }
        }

        return null;
    }

    private static readonly Action<ILogger, string, string, Exception?> _logClaimNameFallback =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(1, nameof(ClaimsActorProvider)),
            "ClaimsActorProvider resolved a claim via short<->long claim-name fallback. " +
            "Configured claim '{ConfiguredClaim}' was not present on the authenticated " +
            "identity; resolved via the well-known counterpart '{ResolvedClaim}'. This " +
            "indicates a mismatch between the configured claim name and " +
            "JwtBearerOptions.MapInboundClaims: a configured short / RFC 7519 / OIDC name " +
            "that resolved to a long-form URN means MapInboundClaims = true (the ASP.NET " +
            "Core default) remapped the inbound short name; a configured long-form URN " +
            "that resolved to a short name means MapInboundClaims = false left the short " +
            "name on the identity. Align the configured claim name with the JwtBearer " +
            "setting (or rely on this fallback) to keep diagnostics quiet. The fallback " +
            "continues to accept both forms.");

    private static void LogClaimNameFallback(ILogger<ClaimsActorProvider>? logger, string configured, string resolved)
    {
        if (logger is not null)
            _logClaimNameFallback(logger, configured, resolved, null);
    }

    private static readonly Action<ILogger, string, string[], Exception?> _logUnresolvedActorIdOnAuthenticatedIdentity =
        LoggerMessage.Define<string, string[]>(
            LogLevel.Warning,
            new EventId(5, nameof(ClaimsActorProvider)),
            "ClaimsActorProvider could not resolve an actor id on an authenticated identity. " +
            "Configured ActorIdClaim '{ConfiguredClaim}' (and its known-name fallback) matched " +
            "no claims on a token that does carry other claims ({PresentClaimTypes}); every " +
            "request will short-circuit to HTTP 401 until the mapping is corrected, even though " +
            "the credential itself authenticated successfully. Common causes: (1) JwtBearerOptions" +
            ".MapInboundClaims (default true) remapped the RFC 7519 short name onto its WS-* " +
            "long-form URN, or vice versa; (2) the ActorIdClaim name is wrong for this identity " +
            "provider — inspect the listed claim types and align the option. This diagnostic " +
            "fires at most once per application lifetime.");

    private static void LogUnresolvedActorIdOnAuthenticatedIdentity(
        ILogger logger,
        string configuredClaim,
        string[] presentClaimTypes) =>
        _logUnresolvedActorIdOnAuthenticatedIdentity(logger, configuredClaim, presentClaimTypes, null);

    private static readonly Action<ILogger, string, string[], Exception?> _logEmptyPermissionsOnAuthenticatedIdentity =
        LoggerMessage.Define<string, string[]>(
            LogLevel.Warning,
            new EventId(2, nameof(ClaimsActorProvider)),
            "ClaimsActorProvider resolved zero permissions on an authenticated identity. " +
            "Configured PermissionsClaim '{ConfiguredClaim}' (and its known-name fallback) " +
            "matched no claims on a token that does carry other claims ({PresentClaimTypes}); " +
            "every IAuthorize command will short-circuit to HTTP 403 until the mapping is " +
            "corrected. Common causes: (1) the identity provider emits permissions under a " +
            "nested JSON claim shape (Auth0 'app_metadata.roles', Azure B2C 'extension_*'); " +
            "configure NestedJsonPathClaimsActorProvider with the dotted path. (2) The " +
            "PermissionsClaim name is wrong for this provider; inspect the JWT and align " +
            "the option. This diagnostic fires at most once per application lifetime.");

    private static void LogEmptyPermissionsOnAuthenticatedIdentity(
        ILogger logger,
        string configuredClaim,
        string[] presentClaimTypes) =>
        _logEmptyPermissionsOnAuthenticatedIdentity(logger, configuredClaim, presentClaimTypes, null);

    private static readonly Action<ILogger, string, Exception?> _logJsonShapedClaim =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(ClaimsActorProvider)),
            "ClaimsActorProvider resolved configured PermissionsClaim '{ConfiguredClaim}' to a " +
            "single value that parses as a JSON object or array. The default flat-claim mapping " +
            "stores the raw JSON string as the permission value, which is almost never what the " +
            "consumer intended. The shape is typical of nested-claim identity providers " +
            "(Auth0 'app_metadata', Azure B2C 'extension_*', some Okta token shapes); switch to " +
            "Trellis.Asp.Authorization.NestedJsonPathClaimsActorProvider and configure the dotted " +
            "JSON path to the actual permissions array (for example 'app_metadata.roles'). " +
            "This diagnostic fires at most once per application lifetime; set " +
            "ClaimsActorOptions.ValidateClaimShapeOnFirstUse = false to suppress.");

    private static void LogJsonShapedClaim(ILogger logger, string configuredClaim) =>
        _logJsonShapedClaim(logger, configuredClaim, null);
}
