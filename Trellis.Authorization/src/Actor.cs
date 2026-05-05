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
/// <para>
/// <b>Equality is identity-based.</b> <see cref="Actor"/> is conceptually an entity — its
/// <see cref="Id"/> is a stable, externally-meaningful principal identifier (e.g. JWT
/// <c>sub</c> claim) and the other properties are point-in-time state about that principal
/// (granted permissions and ABAC attributes change over time). Two <see cref="Actor"/>
/// instances with the same <see cref="Id"/> are equal even when their <see cref="Permissions"/>
/// or <see cref="Attributes"/> differ; two with different <see cref="Id"/>s are never equal.
/// This deliberately mirrors the framework's domain-layer
/// <c>Trellis.Entity&lt;TId&gt;</c> semantics without taking on the full <see cref="IAggregate"/>
/// surface (Actor is an authorization-layer principal, not a domain aggregate root).
/// </para>
/// </remarks>
public sealed class Actor : IEquatable<Actor>
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
    /// provide O(1) lookups. Scoped permissions must use the <see cref="PermissionScopeSeparator"/>
    /// convention (e.g. <c>"Document.Edit:Tenant_A"</c>) so they round-trip correctly through
    /// <see cref="HasPermission(string, string)"/>.
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="permissions"/>, <paramref name="forbiddenPermissions"/>, or
    /// <paramref name="attributes"/> is null.
    /// </exception>
    public Actor(
        string id,
        IReadOnlySet<string> permissions,
        IReadOnlySet<string> forbiddenPermissions,
        IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(forbiddenPermissions);
        ArgumentNullException.ThrowIfNull(attributes);
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
    /// The set of permissions granted to the actor. Scoped permissions use the
    /// <see cref="PermissionScopeSeparator"/> convention (e.g. <c>"Document.Edit:Tenant_A"</c>)
    /// — the format <see cref="HasPermission(string, string)"/> reconstructs at lookup time.
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions"/> is null.</exception>
    public static Actor Create(string id, IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return new(id, permissions, FrozenSet<string>.Empty, FrozenDictionary<string, string>.Empty);
    }

    /// <summary>
    /// Returns true if this actor has the specified permission and it is not forbidden.
    /// If the permission exists in both <see cref="Permissions"/> and <see cref="ForbiddenPermissions"/>,
    /// deny wins and this returns false.
    /// </summary>
    /// <param name="permission">The permission to check (case-sensitive, ordinal comparison).</param>
    /// <returns>True if the permission is granted and not explicitly denied; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permission"/> is null.</exception>
    public bool HasPermission(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return !ForbiddenPermissions.Contains(permission) && Permissions.Contains(permission);
    }

    /// <summary>
    /// Returns true if this actor has the specified permission within the given scope
    /// and it is not forbidden. Uses the <c>"Permission:Scope"</c> convention with
    /// <see cref="PermissionScopeSeparator"/>.
    /// </summary>
    /// <param name="permission">The base permission (e.g., <c>"Document.Edit"</c>).</param>
    /// <param name="scope">The scope qualifier (e.g., <c>"Tenant_A"</c> or a resource ID). Case-sensitive.</param>
    /// <returns>True if the scoped permission is granted and not explicitly denied; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permission"/> or <paramref name="scope"/> is null.</exception>
    public bool HasPermission(string permission, string scope)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(scope);
        return HasPermission($"{permission}{PermissionScopeSeparator}{scope}");
    }

    /// <summary>
    /// Returns true if this actor has ALL of the specified permissions.
    /// Each permission is checked against <see cref="ForbiddenPermissions"/> (deny-aware).
    /// </summary>
    /// <param name="permissions">The permissions to check.</param>
    /// <returns>True if the actor has every specified permission and none are forbidden; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions"/> is null.</exception>
    public bool HasAllPermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.All(HasPermission);
    }

    /// <summary>
    /// Returns true if this actor has ANY of the specified permissions.
    /// Each permission is checked against <see cref="ForbiddenPermissions"/> (deny-aware).
    /// </summary>
    /// <param name="permissions">The permissions to check.</param>
    /// <returns>True if the actor has at least one non-forbidden specified permission; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions"/> is null.</exception>
    public bool HasAnyPermission(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.Any(HasPermission);
    }

    /// <summary>
    /// Returns true if this actor is the owner of the specified resource.
    /// Compares the actor's <see cref="Id"/> against the resource owner ID using ordinal comparison.
    /// </summary>
    /// <param name="resourceOwnerId">The identifier of the resource owner (e.g., creator ID).</param>
    /// <returns>True if the actor's ID matches the resource owner ID; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resourceOwnerId"/> is null.</exception>
    public bool IsOwner(string resourceOwnerId)
    {
        ArgumentNullException.ThrowIfNull(resourceOwnerId);
        return string.Equals(Id, resourceOwnerId, StringComparison.Ordinal);
    }

    /// <summary>Returns true if this actor has the specified attribute.</summary>
    /// <param name="key">The attribute key. Use <see cref="ActorAttributes"/> constants for well-known keys.</param>
    /// <returns>True if the attribute exists; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    public bool HasAttribute(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Attributes.ContainsKey(key);
    }

    /// <summary>
    /// Returns the value of the specified attribute, or <c>null</c> if the attribute does not exist.
    /// </summary>
    /// <param name="key">The attribute key. Use <see cref="ActorAttributes"/> constants for well-known keys.</param>
    /// <returns>The attribute value if found; otherwise <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    public string? GetAttribute(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Attributes.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Determines whether the specified <see cref="Actor"/> represents the same principal.
    /// </summary>
    /// <param name="other">The actor to compare against.</param>
    /// <returns>
    /// <see langword="true"/> when both actors share the same <see cref="Id"/> (ordinal
    /// comparison); otherwise <see langword="false"/>. The other properties
    /// (<see cref="Permissions"/>, <see cref="ForbiddenPermissions"/>, <see cref="Attributes"/>)
    /// are state about the principal, not part of identity, and are intentionally excluded
    /// from the equality comparison.
    /// </returns>
    /// <remarks>
    /// Identity-based equality mirrors the domain-layer <c>Trellis.Entity&lt;TId&gt;</c>
    /// pattern without inheriting the full <see cref="IAggregate"/> contract. Two
    /// <see cref="Actor"/>s with the same <see cref="Id"/> represent the same principal even
    /// when one carries a freshly-rotated permission set or a different request-bound IP
    /// address — both are point-in-time snapshots of the same actor.
    /// </remarks>
    public bool Equals(Actor? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return string.Equals(Id, other.Id, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Actor);

    /// <inheritdoc />
    /// <remarks>
    /// The hash is derived from <see cref="Id"/> only, matching the identity-based equality
    /// contract on <see cref="Equals(Actor)"/>.
    /// </remarks>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

    /// <summary>
    /// Determines whether two actors represent the same principal (identity-based comparison).
    /// </summary>
    /// <param name="left">The first actor to compare.</param>
    /// <param name="right">The second actor to compare.</param>
    /// <returns>
    /// <see langword="true"/> when both operands are <see langword="null"/>, or when both
    /// share the same <see cref="Id"/>; otherwise <see langword="false"/>.
    /// </returns>
    public static bool operator ==(Actor? left, Actor? right)
    {
        if (left is null)
            return right is null;
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two actors represent different principals.
    /// </summary>
    /// <param name="left">The first actor to compare.</param>
    /// <param name="right">The second actor to compare.</param>
    /// <returns><see langword="true"/> when the actors have different <see cref="Id"/>s or exactly one is <see langword="null"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Actor? left, Actor? right) => !(left == right);

    private static FrozenSet<string> SnapshotSet(IReadOnlySet<string> values) =>
        values.Count == 0
            ? FrozenSet<string>.Empty
            : values.ToFrozenSet(StringComparer.Ordinal);

    private static FrozenDictionary<string, string> SnapshotDictionary(IReadOnlyDictionary<string, string> values) =>
        values.Count == 0
            ? FrozenDictionary<string, string>.Empty
            : values.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}