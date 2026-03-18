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
public sealed class AuthorizationBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorize, global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    private readonly IActorProvider _actorProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="actorProvider">Provides the current authenticated actor.</param>
    public AuthorizationBehavior(IActorProvider actorProvider)
        => _actorProvider = actorProvider;

    /// <inheritdoc />
    public ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var actor = _actorProvider.GetCurrentActor();
        if (actor is null)
            throw new InvalidOperationException("No authenticated actor available. Ensure an IActorProvider is configured and the user is authenticated.");

        if (!actor.HasAllPermissions(message.RequiredPermissions))
        {
            var missing = message.RequiredPermissions
                .Where(p => !actor.HasPermission(p));

            var error = Error.Forbidden(
                $"Missing required permissions: {string.Join(", ", missing)}");

            return new ValueTask<TResponse>(TResponse.CreateFailure(error));
        }

        return next(message, cancellationToken);
    }
}