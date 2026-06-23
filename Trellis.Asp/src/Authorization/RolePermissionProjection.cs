namespace Trellis.Asp.Authorization;

using System.Collections.Frozen;
using System.Linq;
using System.Security.Claims;
using Trellis.Authorization;

/// <summary>
/// Projects coarse role names into the granular permission set an <see cref="Actor"/> carries, using
/// an application-supplied role→permissions map.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Actor"/> has no role concept — permissions must be pre-flattened from all sources
/// (JWT roles, database groups) before the actor is constructed so every check stays O(1). This
/// helper performs that flattening from a map the application owns, replacing the hand-rolled
/// <c>roles.SelectMany(r =&gt; map[r])</c> shape that throws on an unmapped role.
/// </para>
/// <para>
/// Roles absent from the map are skipped rather than throwing, so a token carrying a role the
/// service does not recognize yields the recognized permissions instead of a 500. The result is an
/// ordinal, deduplicated set matching <see cref="Actor"/>'s ordinal permission comparison.
/// </para>
/// <para>
/// The map is captured by reference; supply a stable, effectively-immutable map (it is read
/// concurrently, once per request).
/// </para>
/// </remarks>
public static class RolePermissionProjection
{
    /// <summary>
    /// Expands the given role names into a flattened, ordinal-deduplicated permission set using
    /// <paramref name="rolePermissions"/>. Roles not present in the map are skipped; null or
    /// whitespace role names and permission values are ignored.
    /// </summary>
    /// <param name="roles">The role names to expand (for example, the values of a token's role claims).</param>
    /// <param name="rolePermissions">
    /// The application-supplied map from role name to the permissions that role grants. Role lookups
    /// use the map's own key comparer, so a case-insensitive map matches case-variant role names.
    /// </param>
    /// <returns>The granted permissions, as an ordinal <see cref="IReadOnlySet{T}"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roles"/> or <paramref name="rolePermissions"/> is null.</exception>
    public static IReadOnlySet<string> ExpandRoles(
        IEnumerable<string> roles,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> rolePermissions)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(rolePermissions);
        return BuildPermissionSet(roles, rolePermissions, keepRolesAsPermissions: false);
    }

    /// <summary>
    /// Builds a permission-mapping delegate, compatible with
    /// <see cref="EntraActorOptions.MapPermissions"/>, that reads role claims and expands them via
    /// <paramref name="rolePermissions"/>.
    /// </summary>
    /// <param name="rolePermissions">The application-supplied role→permissions map (see <see cref="ExpandRoles"/>).</param>
    /// <param name="roleClaimType">
    /// The claim type carrying role names. When null (the default), both the short <c>"roles"</c>
    /// claim and its mapped <see cref="ClaimTypes.Role"/> URN are matched (case-insensitively),
    /// mirroring the <see cref="EntraActorOptions"/> default and tolerating
    /// <c>JwtBearerOptions.MapInboundClaims</c> either way. Pass an explicit type to match only that.
    /// </param>
    /// <param name="keepRolesAsPermissions">
    /// When true, each role name that is present in the map is also added to the result (for systems
    /// where a recognized role name doubles as a coarse permission). Roles absent from the map are
    /// skipped entirely — their name is never added. Defaults to false.
    /// </param>
    /// <returns>A delegate that maps a claim sequence to the granted permissions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rolePermissions"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="roleClaimType"/> is non-null but empty or whitespace.</exception>
    public static Func<IEnumerable<Claim>, IReadOnlySet<string>> ForRoleClaims(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> rolePermissions,
        string? roleClaimType = null,
        bool keepRolesAsPermissions = false)
    {
        ArgumentNullException.ThrowIfNull(rolePermissions);
        if (roleClaimType is not null && string.IsNullOrWhiteSpace(roleClaimType))
            throw new ArgumentException("Role claim type must be non-empty when specified.", nameof(roleClaimType));

        return claims =>
        {
            ArgumentNullException.ThrowIfNull(claims);
            var roleValues = claims
                .Where(claim => IsRoleClaim(claim.Type, roleClaimType))
                .Select(claim => claim.Value);
            return BuildPermissionSet(roleValues, rolePermissions, keepRolesAsPermissions);
        };
    }

    private static bool IsRoleClaim(string claimType, string? roleClaimType) =>
        roleClaimType is null
            ? string.Equals(claimType, "roles", StringComparison.OrdinalIgnoreCase)
              || string.Equals(claimType, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase)
            : string.Equals(claimType, roleClaimType, StringComparison.OrdinalIgnoreCase);

    private static FrozenSet<string> BuildPermissionSet(
        IEnumerable<string> roleValues,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> rolePermissions,
        bool keepRolesAsPermissions)
    {
        HashSet<string>? permissions = null;
        foreach (var role in roleValues)
        {
            if (string.IsNullOrWhiteSpace(role))
                continue;

            // Skip-unknown applies to the role name too: an unmapped role contributes nothing,
            // even when keepRolesAsPermissions is set — it must never leak in as a permission.
            if (!rolePermissions.TryGetValue(role, out var granted) || granted is null)
                continue;

            if (keepRolesAsPermissions)
                (permissions ??= new HashSet<string>(StringComparer.Ordinal)).Add(role);

            foreach (var permission in granted)
                if (!string.IsNullOrWhiteSpace(permission))
                    (permissions ??= new HashSet<string>(StringComparer.Ordinal)).Add(permission);
        }

        return permissions is null ? FrozenSet<string>.Empty : permissions.ToFrozenSet(StringComparer.Ordinal);
    }
}
