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
        => Handle(command, AuthorizationDispatchContext.GetRequiredActor<TCommand, TResponse>(), cancellationToken);

    /// <summary>Executes command business logic using the supplied authorized actor.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="actor">The actor checked by this dispatch's authorization stages.</param>
    /// <param name="cancellationToken">The dispatch cancellation token.</param>
    /// <returns>The business outcome.</returns>
    protected abstract ValueTask<TResponse> Handle(TCommand command, Actor actor, CancellationToken cancellationToken);
}
