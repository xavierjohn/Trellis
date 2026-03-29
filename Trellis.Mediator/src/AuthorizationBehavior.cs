namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>
/// Pipeline behavior that checks the current actor has all permissions
/// declared in <see cref="IAuthorize.RequiredPermissions"/>.
/// Short-circuits with <see cref="Error.Forbidden(string, string?)"/> if any permission is missing.
/// </summary>
/// <typeparam name="TMessage">The message type, constrained to <see cref="IAuthorize"/>.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.</typeparam>
public sealed class AuthorizationBehavior<TMessage, TResponse>(IActorProvider actorProvider)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorize, global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var actor = await actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);

        if (actor is null)
            throw new InvalidOperationException("No authenticated actor available. Ensure an IActorProvider is configured and the user is authenticated.");

        if (!actor.HasAllPermissions(message.RequiredPermissions))
        {
            var error = Error.Forbidden("Insufficient permissions.");

            return TResponse.CreateFailure(error);
        }

        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}