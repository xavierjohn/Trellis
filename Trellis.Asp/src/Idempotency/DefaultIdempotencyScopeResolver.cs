namespace Trellis.Asp.Idempotency;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

/// <summary>
/// Default <see cref="IIdempotencyScopeResolver"/> registered by
/// <see cref="IdempotencyServiceCollectionExtensions.AddTrellisIdempotency"/>. Resolves
/// <see cref="IActorProvider"/> from the request services at request time and uses the
/// current actor's id as the scope, falling back to
/// <see cref="AnonymousIdempotencyScopeResolver.AnonymousScope"/> when no provider is
/// registered or the actor cannot be determined.
/// </summary>
/// <remarks>
/// <para>
/// Resolving the actor provider lazily per request lets the idempotency package compose with
/// any actor-provider registration that happens before the application starts, without
/// requiring callers to add the scope resolver themselves. The default scope is
/// <em>per-actor</em> so the same key value sent by two different authenticated clients
/// cannot collide in the store.
/// </para>
/// </remarks>
public sealed class DefaultIdempotencyScopeResolver : IIdempotencyScopeResolver
{
    /// <inheritdoc/>
    public async ValueTask<string> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var provider = context.RequestServices.GetService<IActorProvider>();
        if (provider is not null)
        {
            var actor = await provider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
            if (actor.TryGetValue(out var resolved))
                return resolved.Id.Value;
        }

        return AnonymousIdempotencyScopeResolver.AnonymousScope;
    }
}