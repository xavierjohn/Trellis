namespace Trellis.Authorization;

/// <summary>
/// Convenience base class for resource loaders that extract a typed ID from the message
/// and load via a repository's GetByIdAsync method.
/// </summary>
/// <typeparam name="TMessage">The command or query type.</typeparam>
/// <typeparam name="TResource">The aggregate or entity type to load.</typeparam>
/// <typeparam name="TId">The identifier type (e.g., <c>OrderId</c>, <c>Guid</c>).</typeparam>
/// <remarks>
/// <para>
/// Covers the most common resource-loading pattern: extract an ID from the message,
/// call a repository method. Implement <see cref="IResourceLoader{TMessage, TResource}"/>
/// directly for complex cases (composite keys, projections, etc.).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class CancelOrderResourceLoader
///     : ResourceLoaderById&lt;CancelOrderCommand, Order, OrderId&gt;
/// {
///     private readonly IOrderRepository _repo;
///     public CancelOrderResourceLoader(IOrderRepository repo) =&gt; _repo = repo;
///     protected override OrderId GetId(CancelOrderCommand message) =&gt; message.OrderId;
///     protected override Task&lt;Result&lt;Order&gt;&gt; GetByIdAsync(OrderId id, CancellationToken ct)
///         =&gt; _repo.GetByIdAsync(id, ct);
/// }
/// </code>
/// </example>
public abstract class ResourceLoaderById<TMessage, TResource, TId>
    : IResourceLoader<TMessage, TResource>
{
    /// <summary>
    /// Extracts the resource ID from the message.
    /// </summary>
    /// <param name="message">The command or query containing the resource identifier.</param>
    /// <returns>The typed resource identifier.</returns>
    protected abstract TId GetId(TMessage message);

    /// <summary>
    /// Loads the resource by ID.
    /// Return <c>Result.Failure</c> with a <see cref="NotFoundError"/> if the resource does not exist.
    /// </summary>
    /// <param name="id">The resource identifier extracted by <see cref="GetId"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A success result containing the loaded resource, or a failure result if not found.
    /// </returns>
    protected abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken);

    /// <inheritdoc />
    public Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)
        => GetByIdAsync(GetId(message), cancellationToken);
}
