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
    IReadOnlyList<string> RequiredPermissions { get; }
}