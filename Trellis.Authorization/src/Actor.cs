namespace Trellis.Authorization;

using System.Collections.Frozen;

/// <summary>
/// Represents the current authenticated user making the request.
/// Contains identity, permissions, forbidden permissions, and contextual attributes
/// used by authorization behaviors.
/// </summary>
/// <remarks>
/// <para>
/// Hydrated during authentication/middleware. Permissions should be pre-flattened from all
/// sources (JWT roles, database groups, organizational hierarchies) before constructing the Actor
/// so that all permission checks remain O(1).
/// </para>
/// <para>
/// Scoped permissions use the <c>"Permission:Scope"</c> convention
/// (e.g., <c>"Document.Edit:Tenant_A"</c>).
/// Add scoped entries to <see cref="Permissions"/> and check with
/// <see cref="HasPermission(string, string)"/>.
/// </para>
/// <para>
/// All permission and attribute lookups use ordinal (case-sensitive) comparison.
/// Ensure consistent casing when hydrating permissions, forbidden permissions, and attributes.
/// </para>
/// </remarks>
public sealed record Actor
{
    private IReadOnlySet<string> _permissions = FrozenSet<string>.Empty;
    private IReadOnlySet<string> _forbiddenPermissions = FrozenSet<string>.Empty;
    private IReadOnlyDictionary<string, string> _attributes = FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// Initializes a new <see cref="Actor"/> and snapshots the supplied authorization state.
    /// </summary>
    /// <param name="id">The unique identifier of the actor (e.g., user ID from JWT sub claim).</param>
    /// <param name="permissions">
    /// The set of permissions granted to the actor.
    /// Implementations such as <see cref="HashSet{T}"/> and <see cref="System.Collections.Frozen.FrozenSet{T}"/>
    /// provide O(1) lookups.
    /// </param>
    /// <param name="forbiddenPermissions">
    /// Permissions that are explicitly denied for this actor.
    /// A permission present in both <paramref name="permissions"/> and <paramref name="forbiddenPermissions"/>
    /// is treated as denied (deny always overrides allow).
    /// </param>
    /// <param name="attributes">
    /// Contextual attributes for attribute-based access control (ABAC).
    /// Stores environmental metadata such as IP address, MFA status, risk score, or VPN status.
    /// Use <see cref="ActorAttributes"/> constants for well-known keys.
    /// </param>
    public Actor(
        string id,
        IReadOnlySet<string> permissions,
        IReadOnlySet<string> forbiddenPermissions,
        IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Permissions = permissions;
        ForbiddenPermissions = forbiddenPermissions;
        Attributes = attributes;
    }

    /// <summary>
    /// The separator used between permission name and scope in scoped permission strings.
    /// </summary>
    public const char PermissionScopeSeparator = ':';

    /// <summary>
    /// The unique identifier of the actor (e.g., user ID from JWT sub claim).
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// The set of permissions granted to the actor.
    /// </summary>
    public IReadOnlySet<string> Permissions
    {
        get => _permissions;
        init => _permissions = SnapshotSet(value);
    }

    /// <summary>
    /// Permissions that are explicitly denied for this actor.
    /// </summary>
    public IReadOnlySet<string> ForbiddenPermissions
    {
        get => _forbiddenPermissions;
        init => _forbiddenPermissions = SnapshotSet(value);
    }

    /// <summary>
    /// Contextual attributes for attribute-based access control (ABAC).
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = SnapshotDictionary(value);
    }

    /// <summary>
    /// Creates an <see cref="Actor"/> with no forbidden permissions and no ABAC attributes.
    /// Convenience factory for the common case where only identity and permissions are needed.
    /// </summary>
    /// <param name="id">The unique identifier of the actor.</param>
    /// <param name="permissions">The set of permissions granted to the actor.</param>
    /// <returns>A new <see cref="Actor"/> instance.</returns>
    public static Actor Create(string id, IReadOnlySet<string> permissions) =>
        new(id, permissions, FrozenSet<string>.Empty, FrozenDictionary<string, string>.Empty);

    /// <summary>
    /// Returns true if this actor has the specified permission and it is not forbidden.
    /// If the permission exists in both <see cref="Permissions"/> and <see cref="ForbiddenPermissions"/>,
    /// deny wins and this returns false.
    /// </summary>
    /// <param name="permission">The permission to check (case-sensitive, ordinal comparison).</param>
    /// <returns>True if the permission is granted and not explicitly denied; otherwise false.</returns>
    public bool HasPermission(string permission) =>
        !ForbiddenPermissions.Contains(permission) && Permissions.Contains(permission);

    /// <summary>
    /// Returns true if this actor has the specified permission within the given scope
    /// and it is not forbidden. Uses the <c>"Permission:Scope"</c> convention with
    /// <see cref="PermissionScopeSeparator"/>.
    /// </summary>
    /// <param name="permission">The base permission (e.g., <c>"Document.Edit"</c>).</param>
    /// <param name="scope">The scope qualifier (e.g., <c>"Tenant_A"</c> or a resource ID). Case-sensitive.</param>
    /// <returns>True if the scoped permission is granted and not explicitly denied; otherwise false.</returns>
    public bool HasPermission(string permission, string scope) =>
        HasPermission($"{permission}{PermissionScopeSeparator}{scope}");

    /// <summary>
    /// Returns true if this actor has ALL of the specified permissions.
    /// Each permission is checked against <see cref="ForbiddenPermissions"/> (deny-aware).
    /// </summary>
    /// <param name="permissions">The permissions to check.</param>
    /// <returns>True if the actor has every specified permission and none are forbidden; otherwise false.</returns>
    public bool HasAllPermissions(IEnumerable<string> permissions) =>
        permissions.All(HasPermission);

    /// <summary>
    /// Returns true if this actor has ANY of the specified permissions.
    /// Each permission is checked against <see cref="ForbiddenPermissions"/> (deny-aware).
    /// </summary>
    /// <param name="permissions">The permissions to check.</param>
    /// <returns>True if the actor has at least one non-forbidden specified permission; otherwise false.</returns>
    public bool HasAnyPermission(IEnumerable<string> permissions) =>
        permissions.Any(HasPermission);

    /// <summary>
    /// Returns true if this actor is the owner of the specified resource.
    /// Compares the actor's <see cref="Id"/> against the resource owner ID using ordinal comparison.
    /// </summary>
    /// <param name="resourceOwnerId">The identifier of the resource owner (e.g., creator ID).</param>
    /// <returns>True if the actor's ID matches the resource owner ID; otherwise false.</returns>
    public bool IsOwner(string resourceOwnerId) =>
        string.Equals(Id, resourceOwnerId, StringComparison.Ordinal);

    /// <summary>Returns true if this actor has the specified attribute.</summary>
    /// <param name="key">The attribute key. Use <see cref="ActorAttributes"/> constants for well-known keys.</param>
    /// <returns>True if the attribute exists; otherwise false.</returns>
    public bool HasAttribute(string key) => Attributes.ContainsKey(key);

    /// <summary>
    /// Returns the value of the specified attribute, or <c>null</c> if the attribute does not exist.
    /// </summary>
    /// <param name="key">The attribute key. Use <see cref="ActorAttributes"/> constants for well-known keys.</param>
    /// <returns>The attribute value if found; otherwise <c>null</c>.</returns>
    public string? GetAttribute(string key) =>
        Attributes.TryGetValue(key, out var value) ? value : null;

    private static FrozenSet<string> SnapshotSet(IReadOnlySet<string> values) =>
        values.Count == 0
            ? FrozenSet<string>.Empty
            : values.ToFrozenSet(StringComparer.Ordinal);

    private static FrozenDictionary<string, string> SnapshotDictionary(IReadOnlyDictionary<string, string> values) =>
        values.Count == 0
            ? FrozenDictionary<string, string>.Empty
            : values.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}