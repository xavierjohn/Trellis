namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>Supplies the checked actor and directly authorized resource to command business logic.</summary>
/// <typeparam name="TCommand">A directly resource-authorized command; static permissions are optional.</typeparam>
/// <typeparam name="TResource">The reference-type resource loaded by the pipeline.</typeparam>
/// <typeparam name="TResponse">A result-compatible response.</typeparam>
public abstract class ActorResourceCommandHandler<TCommand, TResource, TResponse> : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>, IAuthorizeResource<TResource>
    where TResource : class
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public ValueTask<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
        => Handle(command, AuthorizationDispatchContext.GetRequiredActor<TCommand, TResponse>(),
            AuthorizationDispatchContext.GetRequiredResource<TCommand, TResource, TResponse>(), cancellationToken);

    /// <summary>Executes command business logic with the exact checked actor and loaded resource.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="actor">The actor checked by this dispatch.</param>
    /// <param name="resource">The same resource instance loaded and authorized by this dispatch.</param>
    /// <param name="cancellationToken">The dispatch cancellation token.</param>
    /// <returns>The business outcome.</returns>
    protected abstract ValueTask<TResponse> Handle(TCommand command, Actor actor, TResource resource, CancellationToken cancellationToken);
}
