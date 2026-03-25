namespace Trellis.Authorization;

/// <summary>
/// Loads the resource required for resource-based authorization.
/// Registered in DI as scoped (typically depends on DbContext via a repository).
/// Resolved per-request by the pipeline behavior via <see cref="IServiceProvider"/>.
/// </summary>
/// <typeparam name="TMessage">The command or query type that triggers the resource load.</typeparam>
/// <typeparam name="TResource">The type of resource to load.</typeparam>
/// <remarks>
/// <para>
/// Implement this interface directly when the loading logic is complex
/// (e.g., composite keys, joins, or projections). For the common case of
/// extracting an ID and calling a repository, use <see cref="ResourceLoaderById{TMessage, TResource, TId}"/>.
/// </para>
/// </remarks>
public interface IResourceLoader<in TMessage, TResource>
{
    /// <summary>
    /// Loads the resource identified by the message.
    /// </summary>
    /// <param name="message">The command or query containing the resource identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A success result containing the loaded resource, or a failure result
    /// (typically <see cref="Error.NotFound(string, string?)"/>) if the resource does not exist.
    /// </returns>
    Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken);
}