namespace Trellis.Authorization;

/// <summary>
/// Provides the current actor asynchronously. Use when permission resolution
/// requires async operations such as database lookups or external service calls.
/// </summary>
public interface IAsyncActorProvider
{
    /// <summary>
    /// Gets the current actor with resolved permissions.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The current <see cref="Actor"/> with resolved permissions.</returns>
    Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default);
}
