namespace Trellis.Asp.Idempotency;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Trellis.Authorization;

/// <summary>
/// <see cref="IIdempotencyScopeResolver"/> that derives the scope from the current actor's id as
/// reported by <see cref="IActorProvider"/>. Returns
/// <see cref="AnonymousIdempotencyScopeResolver.AnonymousScope"/> when no actor is available.
/// </summary>
/// <remarks>
/// <para>
/// Register this resolver in hosts where authenticated clients send idempotency keys so that
/// the same key value used by two different actors cannot collide in the store and let one
/// client replay the other's response.
/// </para>
/// </remarks>
public sealed class ActorIdempotencyScopeResolver : IIdempotencyScopeResolver
{
    private readonly IActorProvider _actorProvider;

    /// <summary>
    /// Creates a new <see cref="ActorIdempotencyScopeResolver"/>.
    /// </summary>
    /// <param name="actorProvider">The configured actor provider.</param>
    public ActorIdempotencyScopeResolver(IActorProvider actorProvider)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        _actorProvider = actorProvider;
    }

    /// <inheritdoc/>
    public async ValueTask<string> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var actor = await _actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor.TryGetValue(out var resolved))
            return resolved.Id.Value;

        return AnonymousIdempotencyScopeResolver.AnonymousScope;
    }
}