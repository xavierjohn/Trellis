namespace Trellis.Authorization;

/// <summary>
/// Declares that this message carries a typed resource identifier that can be
/// extracted for resource-based authorization. Implement alongside
/// <see cref="IAuthorizeResource{TResource}"/> to use a
/// <see cref="SharedResourceLoaderById{TResource, TId}"/> instead of a
/// per-command <see cref="ResourceLoaderById{TMessage, TResource, TId}"/>.
/// </summary>
/// <typeparam name="TResource">The type of the resource to authorize against.</typeparam>
/// <typeparam name="TId">The identifier type (e.g., <c>OrderId</c>).</typeparam>
/// <example>
/// <code>
/// public record CancelOrderCommand(OrderId OrderId)
///     : ICommand&lt;Result&gt;, IAuthorizeResource&lt;Order&gt;, IIdentifyResource&lt;Order, OrderId&gt;
/// {
///     public OrderId GetResourceId() =&gt; OrderId;
///     public IResult Authorize(Actor actor, Order order) =&gt; ...;
/// }
/// </code>
/// </example>
public interface IIdentifyResource<TResource, out TId>
{
    /// <summary>
    /// Extracts the resource identifier from this message.
    /// </summary>
    /// <returns>The typed resource identifier.</returns>
    TId GetResourceId();
}