namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>Supplies the checked actor and directly authorized resource to query business logic.</summary>
/// <typeparam name="TQuery">A directly resource-authorized query; static permissions are optional.</typeparam>
/// <typeparam name="TResource">The reference-type resource loaded by the pipeline.</typeparam>
/// <typeparam name="TResponse">A result-compatible response.</typeparam>
public abstract class ActorResourceQueryHandler<TQuery, TResource, TResponse> : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>, IAuthorizeResource<TResource>
    where TResource : class
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public ValueTask<TResponse> Handle(TQuery query, CancellationToken cancellationToken)
        => Handle(query, AuthorizationDispatchContext.GetRequiredActor<TQuery, TResponse>(),
            AuthorizationDispatchContext.GetRequiredResource<TQuery, TResource, TResponse>(), cancellationToken);

    /// <summary>Executes query business logic with the exact checked actor and loaded resource.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="actor">The actor checked by this dispatch.</param>
    /// <param name="resource">The same resource instance loaded and authorized by this dispatch.</param>
    /// <param name="cancellationToken">The dispatch cancellation token.</param>
    /// <returns>The business outcome.</returns>
    protected abstract ValueTask<TResponse> Handle(TQuery query, Actor actor, TResource resource, CancellationToken cancellationToken);
}
