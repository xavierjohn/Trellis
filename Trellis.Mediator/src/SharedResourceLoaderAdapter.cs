namespace Trellis.Mediator;

using Trellis.Authorization;

/// <summary>
/// Internal adapter that bridges <see cref="IIdentifyResource{TResource, TId}"/> commands
/// to a <see cref="SharedResourceLoaderById{TResource, TId}"/>, implementing
/// <see cref="IResourceLoader{TMessage, TResource}"/> for DI resolution.
/// </summary>
internal sealed class SharedResourceLoaderAdapter<TMessage, TResource, TId>(
    SharedResourceLoaderById<TResource, TId> inner)
    : IResourceLoader<TMessage, TResource>
    where TMessage : IIdentifyResource<TResource, TId>
{
    /// <inheritdoc />
    public Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)
        => inner.GetByIdAsync(message.GetResourceId(), cancellationToken);
}
