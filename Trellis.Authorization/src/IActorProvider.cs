namespace Trellis.Authorization;

/// <summary>
/// Provides the current authenticated actor for authorization behaviors.
/// Implement in the API/ACL layer, typically extracting from <c>HttpContext.User</c>
/// or resolving permissions from a database. Register as scoped in DI.
/// </summary>
public interface IActorProvider
{
    /// <summary>
    /// Returns the current authenticated actor for the request, or <see cref="Maybe{T}.None"/>
    /// when the request has no usable authenticated actor.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>
    /// <para>
    /// <see cref="Maybe.From{T}"/> wrapping the resolved <see cref="Actor"/> when an
    /// authenticated identity is present and the framework can identify the actor.
    /// </para>
    /// <para>
    /// <see cref="Maybe{T}.None"/> when the request has no usable authenticated actor —
    /// typically the request lacks credentials, the authentication middleware did not produce
    /// an authenticated identity, or the authenticated identity is missing the claim needed
    /// to identify the actor. The Trellis mediator authorization pipeline maps this to
    /// <see cref="Error.AuthenticationRequired"/> (HTTP 401) per RFC 9110 §15.5.2. "No authenticated
    /// actor" is client-error state, not an exception — model it via the return value, not by
    /// throwing.
    /// </para>
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown only for genuine infrastructure or configuration failures — for example, the
    /// provider was invoked outside an HTTP request scope (missing <c>HttpContext</c>), a
    /// user-supplied mapping delegate threw, or an option is misconfigured. The mediator
    /// pipeline does not catch this case; it surfaces as <see cref="Error.Unexpected"/>
    /// (HTTP 500), which is correct because these are bugs rather than authentication state.
    /// Do not throw for "missing actor" — return <see cref="Maybe{T}.None"/> instead.
    /// </exception>
    Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default);
}