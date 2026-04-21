namespace Trellis.Asp.Authorization;

using Trellis.Authorization;

/// <summary>
/// Configuration options for <see cref="EntraActorProvider"/>.
/// Controls how Azure Entra ID v2.0 JWT claims are mapped to <see cref="Actor"/> properties.
/// </summary>
/// <remarks>
/// <para>
/// Default behavior:
/// <list type="bullet">
///   <item><see cref="Actor.Id"/> is set from the <c>oid</c> (Object ID) claim.</item>
///   <item><see cref="Actor.Permissions"/> are mapped from claims of type <c>roles</c>.</item>
///   <item><see cref="Actor.ForbiddenPermissions"/> is empty.</item>
///   <item><see cref="Actor.Attributes"/> includes <c>tid</c>, <c>preferred_username</c>,
///     <c>azp</c>, <c>azpacr</c>, <c>acrs</c>, <c>ip_address</c>, and <c>mfa</c>
///     (see <see cref="ActorAttributes"/>).</item>
/// </list>
/// </para>
/// <para>
/// Override any delegate to customize mapping. All delegates receive the
/// authenticated user's claims as <see cref="IEnumerable{T}"/> of
/// <see cref="System.Security.Claims.Claim"/>. <see cref="MapAttributes"/>
/// additionally receives the <see cref="Microsoft.AspNetCore.Http.HttpContext"/>
/// for request-level context (e.g., IP address).
/// </para>
/// </remarks>
public sealed class EntraActorOptions
{
    /// <summary>
    /// The claim type used to resolve the actor's unique identifier.
    /// Defaults to <c>"http://schemas.microsoft.com/identity/claims/objectidentifier"</c>
    /// (the <c>oid</c> claim in Entra v2.0 tokens).
    /// </summary>
    public string IdClaimType { get; set; } =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>
    /// Maps the authenticated user's claims to the <see cref="Actor.Permissions"/> set.
    /// Receives the claims as <see cref="IEnumerable{T}"/> of <see cref="System.Security.Claims.Claim"/>.
    /// Defaults to extracting all claims of type <c>roles</c>.
    /// </summary>
    /// <remarks>
    /// Override to flatten JWT roles into granular permissions, merge with
    /// database-sourced permissions, or map scoped permissions.
    /// </remarks>
    public Func<IEnumerable<System.Security.Claims.Claim>, IReadOnlySet<string>> MapPermissions { get; set; } =
        claims => claims
            .Where(c => string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.Type, System.Security.Claims.ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .ToHashSet();

    /// <summary>
    /// Maps the authenticated user's claims to the <see cref="Actor.ForbiddenPermissions"/> set.
    /// Receives the claims as <see cref="IEnumerable{T}"/> of <see cref="System.Security.Claims.Claim"/>.
    /// Defaults to an empty set — override to populate from an external store
    /// or a custom claim type.
    /// </summary>
    public Func<IEnumerable<System.Security.Claims.Claim>, IReadOnlySet<string>> MapForbiddenPermissions { get; set; } =
        _ => new HashSet<string>();

    /// <summary>
    /// Maps claims and HTTP context to <see cref="Actor.Attributes"/>.
    /// Defaults to extracting <c>tid</c>, <c>preferred_username</c>, <c>azp</c>,
    /// <c>azpacr</c>, <c>acrs</c> from claims, <c>ip_address</c> from the connection,
    /// and <c>mfa</c> from the <c>amr</c> claim.
    /// </summary>
    /// <remarks>
    /// Override to add custom attributes or change how existing ones are derived.
    /// </remarks>
    public Func<IEnumerable<System.Security.Claims.Claim>, Microsoft.AspNetCore.Http.HttpContext, IReadOnlyDictionary<string, string>> MapAttributes { get; set; } =
        (claims, httpContext) =>
        {
            var attributes = new Dictionary<string, string>();

            var claimList = claims as IList<System.Security.Claims.Claim> ?? claims.ToList();

            AddClaimIfPresent(claimList, "tid", ActorAttributes.TenantId, attributes);
            AddClaimIfPresent(claimList, "preferred_username", ActorAttributes.PreferredUsername, attributes);
            AddClaimIfPresent(claimList, "azp", ActorAttributes.AuthorizedParty, attributes);
            AddClaimIfPresent(claimList, "azpacr", ActorAttributes.AuthorizedPartyAcr, attributes);
            AddClaimIfPresent(claimList, "acrs", ActorAttributes.AuthContextClassReference, attributes);

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            if (ipAddress is not null)
                attributes[ActorAttributes.IpAddress] = ipAddress;

            var hasMfa = claimList
                .Any(c => string.Equals(c.Type, "amr", StringComparison.OrdinalIgnoreCase)
                       && string.Equals(c.Value, "mfa", StringComparison.OrdinalIgnoreCase));
            attributes[ActorAttributes.MfaAuthenticated] = hasMfa ? "true" : "false";

            return attributes;
        };

    private static void AddClaimIfPresent(
        IList<System.Security.Claims.Claim> claims,
        string claimType,
        string attributeKey,
        Dictionary<string, string> attributes)
    {
        var claim = claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase));
        if (claim is not null)
            attributes[attributeKey] = claim.Value;
    }
}