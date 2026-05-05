namespace Trellis.Authorization;

/// <summary>
/// Marker interface for commands/queries that require static permissions.
/// Authorization checks verify that the current actor has ALL of the
/// <see cref="RequiredPermissions"/> before calling the handler.
/// </summary>
public interface IAuthorize
{
    /// <summary>
    /// Permissions the actor must have to execute this command/query.
    /// All listed permissions are required (AND logic).
    /// </summary>
    /// <remarks>
    /// Duplicates and order are ignored — the check is set-like under AND-semantics. The type
    /// is <see cref="IReadOnlyList{T}"/> rather than <see cref="IReadOnlySet{T}"/> so consumers
    /// can use natural collection-expression syntax (<c>["orders:delete"]</c>) at the call site.
    /// </remarks>
    IReadOnlyList<string> RequiredPermissions { get; }
}