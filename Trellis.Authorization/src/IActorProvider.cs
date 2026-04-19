namespace Trellis.Authorization;

/// <summary>
/// Provides the current authenticated actor for authorization behaviors.
/// Implement in the API/ACL layer, typically extracting from HttpContext.User
/// or resolving permissions from a database.
/// Register as scoped in DI.
/// </summary>
public interface IActorProvider
{
    /// <summary>
    /// Returns the current actor. Throws if no authenticated user exists
    /// (authentication should be handled before the request reaches the mediator pipeline).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The current authenticated <see cref="Actor"/>.</returns>
    Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default);
}