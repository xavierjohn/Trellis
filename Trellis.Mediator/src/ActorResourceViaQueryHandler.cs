namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>Supplies the checked actor and loaded leaf, not its authorization owners, to query business logic.</summary>
/// <typeparam name="TQuery">An indirectly resource-authorized query; static permissions are optional.</typeparam>
/// <typeparam name="TLeaf">The reference-type leaf identified by the query and loaded by the pipeline.</typeparam>
/// <typeparam name="TOwner">The authorization target type at the end of the registered path.</typeparam>
/// <typeparam name="TResponse">A result-compatible response.</typeparam>
public abstract class ActorResourceViaQueryHandler<TQuery, TLeaf, TOwner, TResponse> : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>, IAuthorizeResourceVia<TOwner>
    where TLeaf : class
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public ValueTask<TResponse> Handle(TQuery query, CancellationToken cancellationToken)
        => Handle(query, AuthorizationDispatchContext.GetRequiredActor<TQuery, TResponse>(),
            AuthorizationDispatchContext.GetRequiredResource<TQuery, TLeaf, TResponse>(), cancellationToken);

    /// <summary>Executes query business logic with the exact checked actor and loaded leaf.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="actor">The actor checked against the owner resources.</param>
    /// <param name="leaf">The same leaf loaded by this dispatch, not its owner resources.</param>
    /// <param name="cancellationToken">The dispatch cancellation token.</param>
    /// <returns>The business outcome.</returns>
    protected abstract ValueTask<TResponse> Handle(TQuery query, Actor actor, TLeaf leaf, CancellationToken cancellationToken);
}
