namespace Trellis.Testing;

using FluentAssertions.Execution;
using Trellis.Authorization;

/// <summary>
/// Assertions for the agreement between full actor and identity-only provider contracts.
/// </summary>
public static class ActorIdentityAssertionsExtensions
{
    /// <summary>
    /// Resolves identity and actor sequentially and asserts that a resolved actor has the
    /// same canonical identity. Actor absence is valid even when identity is available.
    /// </summary>
    /// <param name="actorProvider">The full actor provider, typically resolved from the same DI scope.</param>
    /// <param name="identityProvider">The identity-only provider for that actor provider.</param>
    /// <param name="cancellationToken">Token passed to both sequential resolutions.</param>
    /// <returns>A task that completes when agreement has been asserted.</returns>
    /// <remarks>Provider exceptions and cancellation propagate unchanged.</remarks>
    public static async Task ShouldHaveConsistentActorIdentityAsync(
        this IActorProvider actorProvider,
        IActorIdentityProvider identityProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(identityProvider);
        cancellationToken.ThrowIfCancellationRequested();

        var identity = await identityProvider.GetCurrentActorIdAsync(cancellationToken).ConfigureAwait(false);
        var actor = await actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (!actor.TryGetValue(out var resolved))
            return;

        Execute.Assertion
            .ForCondition(identity.TryGetValue(out var id) && resolved.Id == id)
            .FailWith(
                "Expected actor provider {0} and identity provider {1} to agree, " +
                "but full actor resolved {2} and identity-only resolved {3}.",
                actorProvider.GetType().FullName, identityProvider.GetType().FullName, resolved.Id,
                identity.Match(static value => value.Value, static () => "None"));
    }
}