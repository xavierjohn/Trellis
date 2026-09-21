namespace Trellis.Authorization;

/// <summary>
/// Provides actor access for callers that have already established an actor-presence invariant.
/// </summary>
public static class ActorProviderExtensions
{
    /// <summary>
    /// Resolves the current actor, throwing if the caller's actor-presence invariant is broken.
    /// </summary>
    /// <param name="actorProvider">The provider used to resolve the actor.</param>
    /// <param name="cancellationToken">Token forwarded to the provider.</param>
    /// <returns>The same actor instance returned by the provider.</returns>
    /// <remarks>
    /// Use only when actor presence is already guaranteed, such as inside a handler reached
    /// through a correctly registered authorization behavior. This method does not authenticate,
    /// check permissions, or cache the actor. It calls the provider once per invocation.
    /// For ordinary unauthenticated requests, use <see cref="IActorProvider.GetCurrentActorAsync"/>
    /// and handle absence as <see cref="Error.AuthenticationRequired"/> instead.
    /// Provider exceptions and cancellation propagate unchanged.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="actorProvider"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The provider returned no actor.</exception>
    public static async Task<Actor> RequireActorAsync(
        this IActorProvider actorProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);

        var actor = await actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        return actor.GetValueOrThrow(
            "RequireActorAsync requires an actor. Ensure actor presence has already been established.");
    }
}
