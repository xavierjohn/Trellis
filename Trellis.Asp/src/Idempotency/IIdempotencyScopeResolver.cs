namespace Trellis.Asp.Idempotency;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Resolves the scope under which an idempotency key is stored, so two clients cannot replay
/// each other's responses by colliding on a key value.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation (<c>DefaultIdempotencyScopeResolver</c>) resolves the actor id
/// from <c>IActorProvider</c> at request time, falling back to anonymous when no provider is
/// registered. Replace with a custom implementation to scope by tenant or any other key. For
/// hosts that require <c>IActorProvider</c> to be registered, swap in
/// <c>ActorIdempotencyScopeResolver</c>.
/// </para>
/// </remarks>
public interface IIdempotencyScopeResolver
{
    /// <summary>
    /// Resolves the scope for the current request. Must never return <c>null</c>; return an
    /// anonymous-sentinel string when no actor identifier is available.
    /// </summary>
    ValueTask<string> ResolveAsync(HttpContext context, CancellationToken cancellationToken);
}