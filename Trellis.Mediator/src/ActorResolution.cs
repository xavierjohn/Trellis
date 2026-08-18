namespace Trellis.Mediator;

using Trellis.Authorization;

/// <summary>
/// Internal helper shared by the three authorization pipeline behaviors
/// (<see cref="AuthorizationBehavior{TMessage,TResponse}"/>,
/// <see cref="ResourceAuthorizationBehavior{TMessage,TResource,TResponse}"/>,
/// <see cref="ResourceAuthorizationViaBehavior{TMessage,TLeaf,TOwner,TResponse}"/>).
/// Centralises the "resolve current actor or short-circuit with HTTP 401" idiom so the three
/// behaviors cannot drift on the 401 vs 500 distinction.
/// </summary>
internal static class ActorResolution
{
    /// <summary>
    /// Resolves the current actor via <paramref name="actorProvider"/>. Returns the actor on
    /// success; returns <see langword="null"/> when no authenticated actor is available, in
    /// which case the caller should short-circuit the pipeline with
    /// <see cref="Error.AuthenticationRequired"/> (HTTP 401).
    /// </summary>
    /// <remarks>
    /// <para>
    /// "No authenticated actor" is modelled as <see cref="Maybe{T}.None"/> on the
    /// <see cref="IActorProvider"/> contract — client-error state, not an exception. Provider
    /// implementations that legitimately cannot operate (no <c>HttpContext</c>, mapping
    /// delegate threw, etc.) still throw <see cref="System.InvalidOperationException"/>, which
    /// propagates here unhandled and is caught later by
    /// <see cref="ExceptionBehavior{TMessage,TResponse}"/> as <see cref="Error.Unexpected"/>
    /// (HTTP 500). That preserves the bug-vs-auth-state distinction.
    /// </para>
    /// </remarks>
    public static async ValueTask<Actor?> TryResolveAsync(
        IActorProvider actorProvider,
        CancellationToken cancellationToken)
    {
        var maybeActor = await actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        return maybeActor.TryGetValue(out var actor) ? actor : null;
    }

    /// <summary>
    /// Canonical <see cref="Error.AuthenticationRequired"/> instance for the "no authenticated actor"
    /// short-circuit. The ASP.NET boundary synthesizes <c>WWW-Authenticate</c> from the
    /// configured authentication handler when it writes a 401 response, so the mediator layer
    /// does not need to know the scheme name or challenge parameters.
    /// </summary>
    public static Error.AuthenticationRequired AuthenticationRequired() =>
        new() { Detail = "Authentication required." };
}