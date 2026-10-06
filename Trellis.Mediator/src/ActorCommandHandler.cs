namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>Supplies the dispatch's authorized actor to command business logic.</summary>
/// <typeparam name="TCommand">An authorization-marked command.</typeparam>
/// <typeparam name="TResponse">A result-compatible response.</typeparam>
public abstract class ActorCommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>, IAuthorizationMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public ValueTask<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
        => HandleCore(command, AuthorizationDispatchContext.GetRequiredActor<TCommand, TResponse>(), cancellationToken);

    /// <summary>Runs business logic with a supplied actor, bypassing all pipeline stages.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="actor">The actor supplied by an isolated business test.</param>
    /// <param name="cancellationToken">The cancellation token to forward.</param>
    /// <returns>The business outcome without authentication, authorization, validation, or commit.</returns>
    public ValueTask<TResponse> Handle(TCommand command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return HandleCore(command, actor, cancellationToken);
    }

    /// <summary>Executes command business logic using the supplied authorized actor.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="actor">The actor checked by this dispatch's authorization stages.</param>
    /// <param name="cancellationToken">The dispatch cancellation token.</param>
    /// <returns>The business outcome.</returns>
    protected abstract ValueTask<TResponse> HandleCore(TCommand command, Actor actor, CancellationToken cancellationToken);
}
