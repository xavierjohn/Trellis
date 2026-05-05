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
    /// Returns the current actor. Implementations must throw
    /// <see cref="InvalidOperationException"/> (or an equivalent) when no authenticated user
    /// exists for the current request — authentication should be handled before the request
    /// reaches the mediator pipeline.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The current authenticated <see cref="Actor"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Recommended exception type when the actor cannot be resolved (e.g. unauthenticated
    /// request, missing claims). Concrete implementations may throw a more specific
    /// subclass — see <c>Trellis.Asp.Authorization</c> providers for examples.
    /// </exception>
    Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default);
}