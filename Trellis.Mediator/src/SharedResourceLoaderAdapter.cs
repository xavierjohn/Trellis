namespace Trellis.Mediator;

using Trellis.Authorization;

/// <summary>
/// Internal adapter that bridges <see cref="IIdentifyResource{TResource, TId}"/> commands
/// to a <see cref="SharedResourceLoaderById{TResource, TId}"/>, implementing
/// <see cref="IResourceLoader{TMessage, TResource}"/> for DI resolution.
/// </summary>
internal sealed class SharedResourceLoaderAdapter<TMessage, TResource, TId>
    : IResourceLoader<TMessage, TResource>
    where TMessage : IIdentifyResource<TResource, TId>
{
    private readonly SharedResourceLoaderById<TResource, TId> _inner;

    public SharedResourceLoaderAdapter(SharedResourceLoaderById<TResource, TId> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _inner.GetByIdAsync(message.GetResourceId(), cancellationToken);
    }
}