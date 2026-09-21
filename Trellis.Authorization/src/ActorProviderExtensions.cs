namespace Trellis.Authorization;

/// <summary>
/// Provides required actor access for callers with established actor presence and stable provider resolution.
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
    /// Use only when actor presence is already guaranteed and the provider returns stable identity
    /// and authorization state throughout the operation. This method performs another provider lookup;
    /// it does not retrieve a snapshot captured by an authorization behavior. Authorization alone
    /// does not establish provider stability. When resolution can change, both authorization and
    /// the handler must use the same scoped caching provider, configured before either lookup.
    /// This method does not authenticate, check permissions, enforce stability, or cache the actor.
    /// It calls the provider once per invocation.
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
