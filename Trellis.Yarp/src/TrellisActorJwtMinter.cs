namespace Trellis.Yarp;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using global::Microsoft.IdentityModel.JsonWebTokens;
using global::Microsoft.IdentityModel.Tokens;
using global::Yarp.ReverseProxy.Configuration;
using Trellis.Authorization;

/// <summary>
/// Result of a successful <see cref="TrellisActorJwtMinter.MintFor"/> call. Carries the
/// compact JWS (the actual token to write to <c>Authorization: Bearer</c>) plus the
/// low-cardinality metadata the audit log records (jti for correlation, iss/aud/kid
/// for routing-shape verification, exp for replay-window analysis). Centralizing the
/// metadata here avoids a second JWT parse on the audit-log path.
/// </summary>
/// <param name="CompactJws">The signed JWT in compact serialization form (header.payload.signature).</param>
/// <param name="Jti">The fresh GUID-N value of the <c>jti</c> claim — the audit correlation key.</param>
/// <param name="Issuer">The value of the <c>iss</c> claim (matches <see cref="TrellisActorForwardingOptions.Issuer"/>).</param>
/// <param name="Audience">The value of the <c>aud</c> claim (per-cluster, from <see cref="TrellisActorForwardingOptions.AudiencePerCluster"/>).</param>
/// <param name="ExpiresAt">The token's <c>exp</c> as a <see cref="DateTimeOffset"/> (UTC).</param>
/// <param name="Kid">The signing-key identifier emitted in the JWT header (from <see cref="SigningCredentials"/>.Key.KeyId).</param>
/// <param name="PermissionsCount">Count of <c>permissions</c> claims actually emitted in the token (the PROJECTED count, after <see cref="TrellisActorForwardingOptions.ProjectPermissionsFor"/>). May differ from the source actor's count when a projection callback filters.</param>
/// <param name="ForbiddenPermissionsCount">Count of <c>forbidden_permissions</c> claims actually emitted (the PROJECTED count, after <see cref="TrellisActorForwardingOptions.ProjectForbiddenFor"/>). Always emitted to satisfy the deny-overrides-allow contract integrity invariant, even when zero.</param>
internal readonly record struct TrellisActorMintResult(
    string CompactJws,
    string Jti,
    string Issuer,
    string Audience,
    DateTimeOffset ExpiresAt,
    string Kid,
    int PermissionsCount,
    int ForbiddenPermissionsCount);

/// <summary>
/// Mints a fresh per-cluster internal JWT from an <see cref="Actor"/>. Called from the
/// YARP actor-forwarding transform on every request that has an authenticated actor;
/// the resulting compact JWS is written to the upstream <c>Authorization</c> header.
/// </summary>
/// <remarks>
/// <para>
/// <b>v1 contract</b> (see <see cref="TrellisInternalJwtClaimNames"/> for the exact
/// claim names):
/// </para>
/// <list type="bullet">
///   <item><description><c>iss</c> — <see cref="TrellisActorForwardingOptions.Issuer"/>.</description></item>
///   <item><description><c>aud</c> — <see cref="TrellisActorForwardingOptions.AudiencePerCluster"/> applied to the destination <see cref="ClusterConfig"/>.</description></item>
///   <item><description><c>sub</c> — <see cref="TrellisActorForwardingOptions.ActorIdResolver"/> applied to the actor.</description></item>
///   <item><description><c>jti</c> — fresh <see cref="Guid.NewGuid"/> rendered as 32 hex characters (N format).</description></item>
///   <item><description><c>iat</c> / <c>nbf</c> — <c>TimeProvider.GetUtcNow()</c> (JWT NumericDate = unix seconds, truncated).</description></item>
///   <item><description><c>exp</c> — <c>iat + Lifetime</c>.</description></item>
///   <item><description><c>permissions</c> — multi-valued (one JSON-array element per permission), projected via <see cref="TrellisActorForwardingOptions.ProjectPermissionsFor"/>.</description></item>
///   <item><description><c>forbidden_permissions</c> — multi-valued, projected via <see cref="TrellisActorForwardingOptions.ProjectForbiddenFor"/>.</description></item>
///   <item><description><c>trellis_actor_contract_version</c> = <c>"1"</c>.</description></item>
///   <item><description><c>trellis_permissions_count</c> — decimal-string count of emitted permissions claims (including <c>"0"</c>).</description></item>
///   <item><description><c>trellis_forbidden_permissions_count</c> — decimal-string count of emitted forbidden claims (including <c>"0"</c>). The contract integrity invariant: empty MUST NOT be indistinguishable from absent (security review finding #5, Blocking).</description></item>
///   <item><description>One claim per <see cref="TrellisActorForwardingOptions.ProjectAttributes"/> entry, using the entry key as the claim name and the entry value as the (single-valued) claim value.</description></item>
/// </list>
/// <para>
/// The JWT header carries the <c>kid</c> taken from
/// <see cref="TrellisActorForwardingOptions.SigningCredentials"/>'
/// <see cref="SecurityKey.KeyId"/> (startup-validated non-empty) so downstream consumers
/// (<c>JwtBearerHandler</c> with JWKS discovery, or the air-gapped static-key-ring
/// profile) can resolve the right key during rotation.
/// </para>
/// </remarks>
internal sealed class TrellisActorJwtMinter
{
    private readonly IOptions<TrellisActorForwardingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly JsonWebTokenHandler _handler;

    public TrellisActorJwtMinter(
        IOptions<TrellisActorForwardingOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
        _handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
    }

    /// <summary>
    /// Mints a fresh signed JWT carrying the full <see cref="Actor"/> surface for the
    /// destination <paramref name="cluster"/>. Returns the compact JWS together with the
    /// low-cardinality metadata the audit log records (jti, iss, aud, exp, kid).
    /// </summary>
    /// <param name="actor">The actor authenticated at the gateway boundary. Required.</param>
    /// <param name="cluster">The destination YARP cluster the request is being forwarded to. Required.</param>
    /// <returns>The minted token plus its audit-correlation metadata.</returns>
    public TrellisActorMintResult MintFor(Actor actor, ClusterConfig cluster)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(cluster);

        var options = _options.Value;

        var audience = options.AudiencePerCluster(cluster);
        var projectedPermissions = options.ProjectPermissionsFor(cluster, actor.Permissions);
        var projectedForbidden = options.ProjectForbiddenFor(cluster, actor.ForbiddenPermissions);
        var projectedAttributes = options.ProjectAttributes(cluster, actor.Attributes);

        var jti = Guid.NewGuid().ToString("N");

        var subject = BuildSubject(
            jti,
            options.ActorIdResolver(actor),
            projectedPermissions,
            projectedForbidden,
            projectedAttributes);

        var issuedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = issuedAt + options.Lifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            Issuer = options.Issuer,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            SigningCredentials = options.SigningCredentials,
        };

        var compactJws = _handler.CreateToken(descriptor);

        return new TrellisActorMintResult(
            CompactJws: compactJws,
            Jti: jti,
            Issuer: options.Issuer,
            Audience: audience,
            ExpiresAt: new DateTimeOffset(expiresAt, TimeSpan.Zero),
            Kid: options.SigningCredentials.Key.KeyId,
            PermissionsCount: projectedPermissions.Count,
            ForbiddenPermissionsCount: projectedForbidden.Count);
    }

    private static ClaimsIdentity BuildSubject(
        string jti,
        string actorId,
        IReadOnlySet<string> permissions,
        IReadOnlySet<string> forbidden,
        IReadOnlyDictionary<string, string> attributes)
    {
        var identity = new ClaimsIdentity();

        identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.Subject, actorId));
        identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.JwtId, jti));
        identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.ContractVersion, TrellisInternalJwtClaimNames.CurrentContractVersion));
        identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.PermissionsCount, permissions.Count.ToString(CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.ForbiddenPermissionsCount, forbidden.Count.ToString(CultureInfo.InvariantCulture)));

        foreach (var permission in permissions)
            identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.Permissions, permission));

        foreach (var permission in forbidden)
            identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.ForbiddenPermissions, permission));

        foreach (var (claimName, value) in attributes)
            identity.AddClaim(new Claim(claimName, value));

        return identity;
    }
}
