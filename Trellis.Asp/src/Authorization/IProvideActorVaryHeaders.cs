namespace Trellis.Asp.Authorization;

using System.Collections.Generic;
using Trellis.Authorization;

/// <summary>
/// Optional capability that an <see cref="IActorProvider"/> implementation can expose so
/// callers of <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/> can emit the
/// correct <c>Vary</c> headers for cache partitioning by actor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> An intermediate HTTP cache can only safely return a response to
/// a request from actor B if it knows the response would have been the same for B as it was
/// for A. The wire signal for that is the <c>Vary</c> response header naming the request
/// headers the response varies on. Trellis owns the actor abstraction and therefore knows
/// which request headers contribute to actor identity for the registered provider —
/// <c>Authorization</c> for the JWT-bearer-backed <c>ClaimsActorProvider</c> /
/// <c>EntraActorProvider</c>, the configured test header (<c>X-Test-Actor</c> by default)
/// for <c>DevelopmentActorProvider</c>, and the wrapped provider's headers for
/// <c>CachingActorProvider</c>. <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/>
/// emits those headers automatically when this interface is implemented; without the
/// interface the builder method throws, fail-closed, rather than silently emit an
/// incorrect or incomplete <c>Vary</c> header.
/// </para>
/// <para>
/// <b>Implementation contract.</b> The returned collection should name the HTTP request
/// headers (case-insensitive, matching wire-shape conventions like
/// <c>Authorization</c> / <c>Cookie</c> / <c>X-Test-Actor</c>) that contribute to actor
/// resolution. Return an empty collection if and only if the provider's actor identity
/// is derived from request data that cannot be cleanly named by a single HTTP header
/// (e.g. mTLS) — in which case consumers should not be calling <c>VaryForActor</c> for
/// those endpoints, and the right answer is usually <c>Cache-Control: private, no-store</c>.
/// </para>
/// <para>
/// <b>Custom providers.</b> A custom <see cref="IActorProvider"/> implementation that
/// derives actor identity from non-bearer signals (cookies, mTLS, forwarded headers) must
/// implement this interface explicitly to be usable with <c>VaryForActor</c>. The framework
/// does not assume a default — guessing wrong here causes cache poisoning across actors.
/// </para>
/// </remarks>
public interface IProvideActorVaryHeaders
{
    /// <summary>
    /// The HTTP request headers that contribute to actor identity for this provider.
    /// Emitted as <c>Vary</c> response-header entries by
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/>.
    /// </summary>
    IReadOnlyCollection<string> VaryByHeaders { get; }
}