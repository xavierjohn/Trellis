namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>Supplies the checked actor and loaded leaf, not its authorization owners, to command business logic.</summary>
/// <typeparam name="TCommand">An indirectly resource-authorized command; static permissions are optional.</typeparam>
/// <typeparam name="TLeaf">The reference-type leaf identified by the command and loaded by the pipeline.</typeparam>
/// <typeparam name="TOwner">The authorization target type at the end of the registered path.</typeparam>
/// <typeparam name="TResponse">A result-compatible response.</typeparam>
public abstract class ActorResourceViaCommandHandler<TCommand, TLeaf, TOwner, TResponse> : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>, IAuthorizeResourceVia<TOwner>
    where TLeaf : class
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public ValueTask<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
        => Handle(command, AuthorizationDispatchContext.GetRequiredActor<TCommand, TResponse>(),
            AuthorizationDispatchContext.GetRequiredResource<TCommand, TLeaf, TResponse>(), cancellationToken);

    /// <summary>Executes command business logic with the exact checked actor and loaded leaf.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="actor">The actor checked against the owner resources.</param>
    /// <param name="leaf">The same leaf loaded by this dispatch, not its owner resources.</param>
    /// <param name="cancellationToken">The dispatch cancellation token.</param>
    /// <returns>The business outcome.</returns>
    protected abstract ValueTask<TResponse> Handle(TCommand command, Actor actor, TLeaf leaf, CancellationToken cancellationToken);
}
