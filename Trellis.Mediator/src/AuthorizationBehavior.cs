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
    private readonly IAsyncActorProvider? _asyncActorProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="actorProvider">Provides the current authenticated actor.</param>
    /// <param name="asyncActorProvider">
    /// Optional async actor provider. When registered, takes precedence over <paramref name="actorProvider"/>.
    /// </param>
    public AuthorizationBehavior(IActorProvider actorProvider, IAsyncActorProvider? asyncActorProvider = null)
    {
        _actorProvider = actorProvider;
        _asyncActorProvider = asyncActorProvider;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken).ConfigureAwait(false);

        if (!actor.HasAllPermissions(message.RequiredPermissions))
        {
            var missing = message.RequiredPermissions
                .Where(p => !actor.HasPermission(p));

            var error = Error.Forbidden(
                $"Missing required permissions: {string.Join(", ", missing)}");

            return TResponse.CreateFailure(error);
        }

        return await next(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Actor> GetActorAsync(CancellationToken cancellationToken)
    {
        if (_asyncActorProvider is not null)
        {
            var actor = await _asyncActorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
            return actor ?? throw new InvalidOperationException("No authenticated actor available. Ensure an IAsyncActorProvider is configured and the user is authenticated.");
        }

        var syncActor = _actorProvider.GetCurrentActor();
        if (syncActor is null)
            throw new InvalidOperationException("No authenticated actor available. Ensure an IActorProvider is configured and the user is authenticated.");
        return syncActor;
    }
}