namespace Trellis.Asp.Authorization;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// <see cref="IActorProvider"/> that hydrates the FULL <see cref="Actor"/> surface
/// (id + permissions + forbidden permissions + ABAC attributes) from a verified
/// internal-network JWT. Pairs with a trusted gateway that mints the JWT using the
/// Trellis internal-JWT contract (sentinel + count claims). See
/// <see cref="TrellisInternalJwtActorOptions"/> for the contract details.
/// </summary>
/// <remarks>
/// <para>
/// <b>Auth-scheme binding.</b> Unlike <see cref="ClaimsActorProvider"/> (which reads the
/// first authenticated identity from <see cref="HttpContext.User"/>), this provider
/// explicitly authenticates the configured
/// <see cref="TrellisInternalJwtActorOptions.AuthenticationScheme"/> via
/// <see cref="AuthenticationHttpContextExtensions.AuthenticateAsync(HttpContext, string?)"/>
/// and reads the resulting <see cref="ClaimsPrincipal"/>. This is the canonical fail-closed
/// posture for an internal-JWT contract: a misconfigured middleware, dev-loopback identity,
/// or custom handler that plants a <see cref="ClaimsPrincipal"/> with matching claim names
/// on <see cref="HttpContext.User"/> WITHOUT going through the configured scheme cannot
/// silently translate forged claims.
/// </para>
/// <para>
/// <b>Failure-mode summary.</b> Returns <see cref="Maybe{T}.None"/> (mediator pipeline
/// maps to <see cref="Trellis.Error.AuthenticationRequired"/> / HTTP 401) on:
/// </para>
/// <list type="bullet">
///   <item><description>The configured authentication scheme failed to authenticate the request.</description></item>
///   <item><description>The authenticated principal has no <see cref="ClaimsIdentity.IsAuthenticated"/> identity.</description></item>
///   <item><description>The <see cref="TrellisInternalJwtActorOptions.ActorIdClaim"/> is missing or empty (with short↔long fallback when the default <c>"sub"</c> has been remapped to <see cref="ClaimTypes.NameIdentifier"/>).</description></item>
///   <item><description><see cref="TrellisInternalJwtActorOptions.ExpectedIssuer"/> / <see cref="TrellisInternalJwtActorOptions.ExpectedAudience"/> is configured and the JWT does not match (defense-in-depth).</description></item>
///   <item><description>The sentinel claim <see cref="TrellisInternalJwtActorOptions.ContractVersionClaim"/> is missing, present more than once, or its value does not equal <see cref="TrellisInternalJwtActorOptions.ExpectedContractVersion"/>.</description></item>
///   <item><description>A count claim (<see cref="TrellisInternalJwtActorOptions.PermissionsCountClaim"/>, <see cref="TrellisInternalJwtActorOptions.ForbiddenPermissionsCountClaim"/>) is missing, present more than once, malformed, negative, or does not match the observed claim count — protects the deny-overrides-allow contract against a proxy that strips deny claims.</description></item>
///   <item><description>A permission, forbidden-permission, or mapped-attribute value is comma-joined or JSON-shaped under <see cref="TrellisInternalJwtActorOptions.StrictClaimShape"/>.</description></item>
///   <item><description>A name in <see cref="TrellisInternalJwtActorOptions.RequiredAttributes"/> is missing on the JWT, empty, or duplicated.</description></item>
///   <item><description>An optional mapped attribute appears more than once (ambiguous).</description></item>
/// </list>
/// <para>
/// <b>Logging redaction.</b> Failure-path log entries carry low-cardinality metadata only —
/// scheme name, claim TYPE names, counts, and expected literal values that came from the
/// CONSUMER's options (not from the JWT). The provider NEVER logs the JWT body, raw claim
/// values, actor IDs, tenant IDs, or other PII. Verified by
/// <c>TrellisInternalJwtActorProviderTests.LoggingRedaction_NeverLogsClaimValues</c>.
/// </para>
/// </remarks>
public sealed partial class TrellisInternalJwtActorProvider : IActorProvider, IProvideActorVaryHeaders
{
    /// <summary>
    /// Short→long claim-name fallback for <see cref="TrellisInternalJwtActorOptions.ActorIdClaim"/>
    /// only. Mirrors the subset of
    /// <c>JwtSecurityTokenHandler.DefaultInboundClaimTypeMap</c> relevant to actor-id
    /// resolution. Other structural claims (permissions, forbidden permissions, attributes,
    /// sentinels) are application-controlled by the gateway mint and not subject to
    /// <c>JwtBearerOptions.MapInboundClaims</c> remapping.
    /// </summary>
    private static readonly FrozenDictionary<string, string> ActorIdShortToLong =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sub"] = ClaimTypes.NameIdentifier,
            ["nameid"] = ClaimTypes.NameIdentifier,
            ["oid"] = "http://schemas.microsoft.com/identity/claims/objectidentifier",
            ["upn"] = ClaimTypes.Upn,
            ["email"] = ClaimTypes.Email,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, FrozenSet<string>> ActorIdLongToShort =
        ActorIdShortToLong
            .GroupBy(kvp => kvp.Value, StringComparer.Ordinal)
            .ToFrozenDictionary(
                g => g.Key,
                g => g.Select(kvp => kvp.Key).ToFrozenSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TrellisInternalJwtActorOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new <see cref="TrellisInternalJwtActorProvider"/>.
    /// </summary>
    /// <param name="httpContextAccessor">Accessor for the current request's <see cref="HttpContext"/>.</param>
    /// <param name="options">Internal-JWT contract options.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger.Instance"/>. All log entries are PII-redacted by construction.</param>
    public TrellisInternalJwtActorProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<TrellisInternalJwtActorOptions> options,
        ILogger<TrellisInternalJwtActorProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(options);
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value ?? throw new ArgumentException(
            $"{nameof(IOptions<TrellisInternalJwtActorOptions>)}.{nameof(IOptions<TrellisInternalJwtActorOptions>.Value)} is null.",
            nameof(options));
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> VaryByHeaders => _options.VaryByHeaders;

    /// <inheritdoc />
    public async Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // HttpContext missing is genuinely exceptional: the provider was invoked outside an
        // HTTP request scope. Configuration bug → 500, not 401. Mirrors ClaimsActorProvider.
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No HttpContext available. Ensure this is called within an HTTP request scope.");

        // 1. Authenticate the configured scheme explicitly. NEVER read HttpContext.User
        //    directly — that would silently translate claims planted by other middleware
        //    (custom auth, dev-loopback identity, cookie auth) that did NOT go through the
        //    Bearer scheme this provider is paired with. Returning Maybe.None on auth failure
        //    surfaces as 401 via the mediator pipeline.
        var authResult = await httpContext.AuthenticateAsync(_options.AuthenticationScheme).ConfigureAwait(false);
        if (!authResult.Succeeded || authResult.Principal is null)
            return Maybe<Actor>.None;

        var identity = authResult.Principal.Identities.FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity;
        if (identity is null)
            return Maybe<Actor>.None;

        // 2. ActorId. Apply short↔long fallback (default "sub" gets remapped to
        //    ClaimTypes.NameIdentifier when JwtBearerOptions.MapInboundClaims = true).
        var actorId = ResolveActorIdWithFallback(identity, _options.ActorIdClaim);
        if (string.IsNullOrWhiteSpace(actorId))
            return Maybe<Actor>.None;

        // 3. Optional iss / aud cross-checks (defense-in-depth — AddJwtBearer should also be
        //    configured with strict TokenValidationParameters per the cookbook). Lookups must
        //    use ordinal claim-type matching (FindFirst/FindAll are case-INSENSITIVE, which
        //    would let a "ISS" reserved-name bypass at runtime match the literal "iss" claim
        //    even though the startup validator's reserved-name guard uses Ordinal).
        if (!string.IsNullOrEmpty(_options.ExpectedIssuer))
        {
            // No ClaimTypes.Authentication fallback: that claim type is unrelated to JWT
            // issuer and would let a principal satisfy ExpectedIssuer without carrying an
            // 'iss' claim. Fail closed when the literal iss claim is absent.
            var iss = FindFirstOrdinal(identity, "iss")?.Value;
            if (!string.Equals(iss, _options.ExpectedIssuer, StringComparison.Ordinal))
            {
                LogExpectedIssuerMismatch(_logger, _options.AuthenticationScheme, _options.ExpectedIssuer);
                return Maybe<Actor>.None;
            }
        }

        if (!string.IsNullOrEmpty(_options.ExpectedAudience))
        {
            var audMatched = FindAllOrdinal(identity, "aud").Any(c => string.Equals(c.Value, _options.ExpectedAudience, StringComparison.Ordinal));
            if (!audMatched)
            {
                LogExpectedAudienceMismatch(_logger, _options.AuthenticationScheme, _options.ExpectedAudience);
                return Maybe<Actor>.None;
            }
        }

        // 4. Contract-version sentinel — exactly one claim, exact value match.
        if (!TryGetExactlyOneClaim(identity, _options.ContractVersionClaim, out var contractVersion))
        {
            LogSentinelClaimMissingOrDuplicated(_logger, _options.AuthenticationScheme, _options.ContractVersionClaim);
            return Maybe<Actor>.None;
        }

        if (!string.Equals(contractVersion, _options.ExpectedContractVersion, StringComparison.Ordinal))
        {
            LogContractVersionMismatch(_logger, _options.AuthenticationScheme, _options.ExpectedContractVersion);
            return Maybe<Actor>.None;
        }

        // 5. Permissions count + observed permissions.
        if (!TryGetExactlyOneCountClaim(identity, _options.PermissionsCountClaim, out var permissionsCount))
            return Maybe<Actor>.None;

        var permissionClaims = FindAllOrdinal(identity, _options.PermissionsClaim).ToList();
        if (permissionClaims.Count != permissionsCount)
        {
            LogPermissionsCountMismatch(_logger, _options.AuthenticationScheme, permissionsCount, permissionClaims.Count);
            return Maybe<Actor>.None;
        }

        if (_options.StrictClaimShape && !ValidateClaimShapes(permissionClaims, _options.PermissionsClaim))
            return Maybe<Actor>.None;

        var permissions = permissionClaims.Select(c => c.Value).ToFrozenSet(StringComparer.Ordinal);

        // 6. Forbidden-permissions count + observed forbidden permissions. The count claim
        //    MUST be present and accurate even when zero — this is the deny-strip protection.
        if (!TryGetExactlyOneCountClaim(identity, _options.ForbiddenPermissionsCountClaim, out var forbiddenCount))
            return Maybe<Actor>.None;

        var forbiddenClaims = FindAllOrdinal(identity, _options.ForbiddenPermissionsClaim).ToList();
        if (forbiddenClaims.Count != forbiddenCount)
        {
            LogForbiddenPermissionsCountMismatch(_logger, _options.AuthenticationScheme, forbiddenCount, forbiddenClaims.Count);
            return Maybe<Actor>.None;
        }

        if (_options.StrictClaimShape && !ValidateClaimShapes(forbiddenClaims, _options.ForbiddenPermissionsClaim))
            return Maybe<Actor>.None;

        var forbiddenPermissions = forbiddenClaims.Select(c => c.Value).ToFrozenSet(StringComparer.Ordinal);

        // 7. Attributes. RequiredAttributes are mandatory & exactly-one; optional mapped
        //    attributes are at-most-one (duplicates are ambiguous → fail closed).
        if (!TryBuildAttributes(identity, out var attributes))
            return Maybe<Actor>.None;

        return Maybe.From(new Actor(actorId, permissions, forbiddenPermissions, attributes));
    }

    /// <summary>
    /// Resolves the actor-id claim with short↔long fallback. Mirrors
    /// <see cref="ClaimsActorProvider.ResolveClaimWithFallback"/> for the actor-id-only
    /// subset of the JWT inbound claim-type map. Returns <see langword="null"/> when
    /// neither form is present.
    /// </summary>
    private static string? ResolveActorIdWithFallback(ClaimsIdentity identity, string configuredClaim)
    {
        var literal = FindFirstOrdinal(identity, configuredClaim)?.Value;
        if (literal is not null)
            return literal;

        if (ActorIdShortToLong.TryGetValue(configuredClaim, out var longForm))
        {
            var counterpart = FindFirstOrdinal(identity, longForm)?.Value;
            if (counterpart is not null)
                return counterpart;
        }

        if (ActorIdLongToShort.TryGetValue(configuredClaim, out var shortForms))
        {
            foreach (var shortForm in shortForms)
            {
                var counterpart = FindFirstOrdinal(identity, shortForm)?.Value;
                if (counterpart is not null)
                    return counterpart;
            }
        }

        return null;
    }

    /// <summary>
    /// Ordinal claim-type lookup. <see cref="ClaimsIdentity.FindFirst(string)"/> and
    /// <see cref="ClaimsIdentity.FindAll(string)"/> are CASE-INSENSITIVE — using them
    /// directly would let case-variant options (e.g. <c>PermissionsClaim = "ISS"</c>)
    /// bypass the validator's case-sensitive reserved-name guard and match a literal
    /// <c>iss</c> claim at runtime. For the internal-JWT contract, where the gateway mints
    /// claim names with a precise case convention, ordinal matching is the security-correct
    /// behavior.
    /// </summary>
    private static Claim? FindFirstOrdinal(ClaimsIdentity identity, string claimType) =>
        identity.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal));

    private static IEnumerable<Claim> FindAllOrdinal(ClaimsIdentity identity, string claimType) =>
        identity.Claims.Where(c => string.Equals(c.Type, claimType, StringComparison.Ordinal));

    /// <summary>
    /// Returns <see langword="true"/> iff <paramref name="identity"/> has exactly one claim
    /// of type <paramref name="claimType"/> and emits its value via <paramref name="value"/>.
    /// Missing or duplicated → <see langword="false"/>. Used for the sentinel and count
    /// claims where duplicates would create ambiguous fail-open/fail-closed behavior
    /// depending on lookup order. Uses ordinal claim-type matching (see
    /// <see cref="FindAllOrdinal"/> for the reason).
    /// </summary>
    private static bool TryGetExactlyOneClaim(ClaimsIdentity identity, string claimType, out string value)
    {
        value = "";
        var matches = FindAllOrdinal(identity, claimType).Take(2).ToList();
        if (matches.Count != 1)
            return false;
        value = matches[0].Value;
        return true;
    }

    /// <summary>
    /// Strict decimal-integer parse for count claims. Rejects whitespace, signs, hex,
    /// scientific notation, and overflow. Counts must be non-negative; gateway emitting
    /// <c>"0"</c> is the contract for empty sets.
    /// </summary>
    private bool TryGetExactlyOneCountClaim(ClaimsIdentity identity, string claimType, out int count)
    {
        count = 0;
        if (!TryGetExactlyOneClaim(identity, claimType, out var raw))
        {
            LogSentinelClaimMissingOrDuplicated(_logger, _options.AuthenticationScheme, claimType);
            return false;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            LogCountClaimMalformed(_logger, _options.AuthenticationScheme, claimType);
            return false;
        }

        count = parsed;
        return true;
    }

    /// <summary>
    /// Validates that no claim value contains a comma or starts with <c>[</c> / <c>{</c>.
    /// These shapes indicate a gateway-side bug that comma-joined a set or serialized JSON
    /// into a single claim value, which would silently create one bogus permission instead
    /// of N separate permissions. Logs the claim TYPE name (never the value) on rejection.
    /// </summary>
    private bool ValidateClaimShapes(IReadOnlyList<Claim> claims, string claimType)
    {
        foreach (var c in claims)
        {
            // Check JSON-shape first: it's unambiguous (starts with [ or {), whereas a
            // value containing commas COULD be either a comma-joined permission set or a
            // JSON array with multiple elements. Reporting "json-shaped" for an array value
            // gives consumers the more specific diagnostic.
            if (LooksJsonShaped(c.Value))
            {
                LogStrictClaimShapeRejection(_logger, _options.AuthenticationScheme, claimType, "json-shaped");
                return false;
            }

            if (LooksCommaJoined(c.Value))
            {
                LogStrictClaimShapeRejection(_logger, _options.AuthenticationScheme, claimType, "comma-joined");
                return false;
            }
        }

        return true;
    }

    private static bool LooksCommaJoined(string value) => value.Contains(',');

    private static bool LooksJsonShaped(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var trimmed = value.AsSpan().TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '[' || trimmed[0] == '{');
    }

    /// <summary>
    /// Builds the <see cref="Actor.Attributes"/> dictionary from
    /// <see cref="TrellisInternalJwtActorOptions.AttributeClaimMap"/>. RequiredAttributes
    /// must be present with exactly one non-empty value; optional attributes are
    /// at-most-one (duplicates fail closed because ambiguous). Strict-shape validation
    /// also applies to mapped-attribute claim values.
    /// </summary>
    private bool TryBuildAttributes(ClaimsIdentity identity, out IReadOnlyDictionary<string, string> attributes)
    {
        var result = new Dictionary<string, string>(_options.AttributeClaimMap?.Count ?? 0, StringComparer.Ordinal);
        // RequiredAttributes lookup must use the SAME comparer as AttributeClaimMap; otherwise
        // a case-insensitive map ([Tenant_Id]) with case-variant RequiredAttributes (["tenant_id"])
        // could pass startup validation but be treated as optional at runtime — silently
        // dropping a required tenant / MFA assertion.
        var mapComparer = _options.AttributeClaimMap?.Comparer ?? StringComparer.Ordinal;
        var required = _options.RequiredAttributes is { Count: > 0 } reqList
            ? new HashSet<string>(reqList, mapComparer)
            : new HashSet<string>(mapComparer);

        if (_options.AttributeClaimMap is not null)
        {
            foreach (var (attrName, claimType) in _options.AttributeClaimMap)
            {
                var claims = FindAllOrdinal(identity, claimType).Take(2).ToList();
                var isRequired = required.Contains(attrName);

                if (claims.Count == 0)
                {
                    if (isRequired)
                    {
                        LogRequiredAttributeMissing(_logger, _options.AuthenticationScheme, attrName);
                        attributes = null!;
                        return false;
                    }

                    continue;
                }

                if (claims.Count > 1)
                {
                    LogAttributeDuplicated(_logger, _options.AuthenticationScheme, attrName);
                    attributes = null!;
                    return false;
                }

                var value = claims[0].Value;
                if (string.IsNullOrEmpty(value))
                {
                    if (isRequired)
                    {
                        LogRequiredAttributeEmpty(_logger, _options.AuthenticationScheme, attrName);
                        attributes = null!;
                        return false;
                    }

                    continue;
                }

                if (_options.StrictClaimShape)
                {
                    if (LooksJsonShaped(value))
                    {
                        LogStrictClaimShapeRejection(_logger, _options.AuthenticationScheme, claimType, "json-shaped");
                        attributes = null!;
                        return false;
                    }

                    if (LooksCommaJoined(value))
                    {
                        LogStrictClaimShapeRejection(_logger, _options.AuthenticationScheme, claimType, "comma-joined");
                        attributes = null!;
                        return false;
                    }
                }

                result[attrName] = value;
            }
        }

        attributes = result;
        return true;
    }

    // -- LoggerMessage definitions. All entries carry low-cardinality metadata only:
    //    scheme name, claim TYPE names, counts, and consumer-configured literal expected
    //    values (never the observed claim value, never the JWT, never PII). The redaction
    //    contract is verified by TrellisInternalJwtActorProviderTests.

    [LoggerMessage(
        EventId = 1,
        EventName = "InternalJwtSentinelMissingOrDuplicated",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: required sentinel/count claim {ClaimType} is missing or appears more than once.")]
    private static partial void LogSentinelClaimMissingOrDuplicated(ILogger logger, string scheme, string claimType);

    [LoggerMessage(
        EventId = 2,
        EventName = "InternalJwtContractVersionMismatch",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: contract-version mismatch, expected {ExpectedVersion}.")]
    private static partial void LogContractVersionMismatch(ILogger logger, string scheme, string expectedVersion);

    [LoggerMessage(
        EventId = 3,
        EventName = "InternalJwtCountClaimMalformed",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: count claim {ClaimType} value is malformed (must be a non-negative invariant-culture decimal integer).")]
    private static partial void LogCountClaimMalformed(ILogger logger, string scheme, string claimType);

    [LoggerMessage(
        EventId = 4,
        EventName = "InternalJwtPermissionsCountMismatch",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: permissions-count mismatch, claimed {ClaimedCount} but observed {ObservedCount} permission claims.")]
    private static partial void LogPermissionsCountMismatch(ILogger logger, string scheme, int claimedCount, int observedCount);

    [LoggerMessage(
        EventId = 5,
        EventName = "InternalJwtForbiddenPermissionsCountMismatch",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: forbidden-permissions-count mismatch, claimed {ClaimedCount} but observed {ObservedCount} forbidden-permission claims (deny-claim stripping defense).")]
    private static partial void LogForbiddenPermissionsCountMismatch(ILogger logger, string scheme, int claimedCount, int observedCount);

    [LoggerMessage(
        EventId = 6,
        EventName = "InternalJwtStrictClaimShapeRejection",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: claim {ClaimType} value is {Kind} which would silently create one bogus value. Fix the gateway mint or set StrictClaimShape = false.")]
    private static partial void LogStrictClaimShapeRejection(ILogger logger, string scheme, string claimType, string kind);

    [LoggerMessage(
        EventId = 7,
        EventName = "InternalJwtRequiredAttributeMissing",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: required attribute {AttributeName} is missing on the JWT.")]
    private static partial void LogRequiredAttributeMissing(ILogger logger, string scheme, string attributeName);

    [LoggerMessage(
        EventId = 8,
        EventName = "InternalJwtRequiredAttributeEmpty",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: required attribute {AttributeName} is present but its value is empty.")]
    private static partial void LogRequiredAttributeEmpty(ILogger logger, string scheme, string attributeName);

    [LoggerMessage(
        EventId = 9,
        EventName = "InternalJwtAttributeDuplicated",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: attribute {AttributeName} appears more than once (ambiguous semantics).")]
    private static partial void LogAttributeDuplicated(ILogger logger, string scheme, string attributeName);

    [LoggerMessage(
        EventId = 10,
        EventName = "InternalJwtExpectedIssuerMismatch",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: 'iss' claim does not match expected issuer {ExpectedIssuer}.")]
    private static partial void LogExpectedIssuerMismatch(ILogger logger, string scheme, string expectedIssuer);

    [LoggerMessage(
        EventId = 11,
        EventName = "InternalJwtExpectedAudienceMismatch",
        Level = LogLevel.Warning,
        Message = "Internal-JWT scheme {Scheme} rejected: no 'aud' claim matches expected audience {ExpectedAudience}.")]
    private static partial void LogExpectedAudienceMismatch(ILogger logger, string scheme, string expectedAudience);
}
