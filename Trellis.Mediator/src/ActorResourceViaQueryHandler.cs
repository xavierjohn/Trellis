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
        => HandleCore(query, AuthorizationDispatchContext.GetRequiredActor<TQuery, TResponse>(),
            AuthorizationDispatchContext.GetRequiredResource<TQuery, TLeaf, TResponse>(), cancellationToken);

    /// <summary>Runs business logic with supplied actor/leaf arguments, bypassing all pipeline stages.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="actor">The actor supplied by an isolated business test.</param>
    /// <param name="leaf">The leaf supplied by that test, not an authorization owner.</param>
    /// <param name="cancellationToken">The cancellation token to forward.</param>
    /// <returns>The business outcome without authentication, authorization, validation, or commit.</returns>
    public ValueTask<TResponse> Handle(TQuery query, Actor actor, TLeaf leaf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(leaf);
        return HandleCore(query, actor, leaf, cancellationToken);
    }

    /// <summary>Executes query business logic with the exact checked actor and loaded leaf.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="actor">The actor checked against the owner resources.</param>
    /// <param name="leaf">The same leaf loaded by this dispatch, not its owner resources.</param>
    /// <param name="cancellationToken">The dispatch cancellation token.</param>
    /// <returns>The business outcome.</returns>
    protected abstract ValueTask<TResponse> HandleCore(TQuery query, Actor actor, TLeaf leaf, CancellationToken cancellationToken);
}
