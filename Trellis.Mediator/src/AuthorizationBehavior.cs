namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>
/// Pipeline behavior that checks the current actor has all permissions
/// declared in <see cref="IAuthorize.RequiredPermissions"/>.
/// Short-circuits with <see cref="Error.Forbidden"/> if any permission is missing.
/// </summary>
/// <typeparam name="TMessage">The message type, constrained to <see cref="IAuthorize"/>.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.</typeparam>
public sealed class AuthorizationBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorize, global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    private readonly IActorProvider _actorProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="actorProvider">The provider used to resolve the current actor.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actorProvider"/> is null.</exception>
    public AuthorizationBehavior(IActorProvider actorProvider)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        _actorProvider = actorProvider;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var actor = await ActorResolution.TryResolveAsync<TMessage, TResponse>(_actorProvider, cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return TResponse.CreateFailure(ActorResolution.AuthenticationRequired());

        if (!actor.HasAllPermissions(message.RequiredPermissions))
        {
            var error = new Error.Forbidden("authorization.insufficient.permissions") { Detail = "Insufficient permissions." };

            return TResponse.CreateFailure(error);
        }

        AuthorizationDispatchContext.MarkStaticAuthorized<TMessage, TResponse>();
        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}