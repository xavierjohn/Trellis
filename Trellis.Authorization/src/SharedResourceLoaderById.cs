namespace Trellis.Authorization;

/// <summary>
/// Shared resource loader that loads a resource by ID. Register one per resource type
/// instead of one <see cref="ResourceLoaderById{TMessage, TResource, TId}"/> per command.
/// </summary>
/// <typeparam name="TResource">The aggregate or entity type to load.</typeparam>
/// <typeparam name="TId">The identifier type (e.g., <c>OrderId</c>).</typeparam>
/// <remarks>
/// <para>
/// When a command implements both <see cref="IAuthorizeResource{TResource}"/> and
/// <see cref="IIdentifyResource{TResource, TId}"/>, the pipeline automatically bridges
/// to this shared loader — no per-command loader class needed.
/// </para>
/// <para>
/// Explicit <see cref="IResourceLoader{TMessage, TResource}"/> registrations always
/// take priority over the shared loader.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // ONE class serves ALL commands that authorize against Order
/// public sealed class OrderResourceLoader(IOrderRepository repo)
///     : SharedResourceLoaderById&lt;Order, OrderId&gt;
/// {
///     public override Task&lt;Result&lt;Order&gt;&gt; GetByIdAsync(OrderId id, CancellationToken ct)
///         =&gt; repo.GetByIdAsync(id, ct);
/// }
/// </code>
/// </example>
public abstract class SharedResourceLoaderById<TResource, TId>
{
    /// <summary>
    /// Loads the resource by ID.
    /// Return <c>Result.Fail</c> with a <see cref="Error.NotFound"/> if the resource does not exist.
    /// </summary>
    /// <param name="id">The resource identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A success result containing the loaded resource, or a failure result if not found.
    /// </returns>
    public abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken);
}