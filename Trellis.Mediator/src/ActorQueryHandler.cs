namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>Supplies the dispatch's authorized actor to query business logic.</summary>
/// <typeparam name="TQuery">An authorization-marked query.</typeparam>
/// <typeparam name="TResponse">A result-compatible response.</typeparam>
public abstract class ActorQueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>, IAuthorizationMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public ValueTask<TResponse> Handle(TQuery query, CancellationToken cancellationToken)
        => HandleCore(query, AuthorizationDispatchContext.GetRequiredActor<TQuery, TResponse>(), cancellationToken);

    /// <summary>Runs business logic with a supplied actor, bypassing all pipeline stages.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="actor">The actor supplied by an isolated business test.</param>
    /// <param name="cancellationToken">The cancellation token to forward.</param>
    /// <returns>The business outcome without authentication, authorization, validation, or commit.</returns>
    public ValueTask<TResponse> Handle(TQuery query, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return HandleCore(query, actor, cancellationToken);
    }

    /// <summary>Executes query business logic using the supplied authorized actor.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="actor">The actor checked by this dispatch's authorization stages.</param>
    /// <param name="cancellationToken">The dispatch cancellation token.</param>
    /// <returns>The business outcome.</returns>
    protected abstract ValueTask<TResponse> HandleCore(TQuery query, Actor actor, CancellationToken cancellationToken);
}
