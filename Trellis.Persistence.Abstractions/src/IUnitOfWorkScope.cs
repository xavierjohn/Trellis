namespace Trellis;

/// <summary>
/// Identifies whether a unit-of-work scope owns the actual persistence boundary.
/// </summary>
public interface IUnitOfWorkScope : IDisposable
{
    /// <summary>
    /// Gets whether this is the outermost scope. Only its successful commit persists changes;
    /// successful commits in nested scopes are deferred and must not release post-commit work.
    /// </summary>
    bool IsOwner { get; }
}
