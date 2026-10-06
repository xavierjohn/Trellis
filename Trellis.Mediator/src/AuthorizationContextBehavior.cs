namespace Trellis.Mediator;

using global::Mediator;
using Trellis.Authorization;

/// <summary>
/// Establishes an isolated authorization snapshot and lifetime for each Mediator dispatch.
/// </summary>
/// <typeparam name="TMessage">An authorization-marked Mediator message.</typeparam>
/// <typeparam name="TResponse">A result-compatible response.</typeparam>
/// <remarks>
/// Register before static and resource authorization. Standard Trellis and resource
/// registration helpers install this infrastructure automatically. Actor resolution
/// remains in the authorization stages; this behavior does not authenticate eagerly.
/// </remarks>
public sealed class AuthorizationContextBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorizationMessage, global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        using var scope = AuthorizationDispatchContext.Begin<TMessage, TResponse>(message);
        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}
