namespace Trellis.Asp.Tests.Idempotency;

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Moq;
using Trellis.Asp.Idempotency;
using Trellis.Authorization;
using Trellis.Primitives;
using Trellis.Testing;

/// <summary>
/// Pins the contract of the default <see cref="IIdempotencyScopeResolver"/> implementations:
/// <see cref="AnonymousIdempotencyScopeResolver"/> returns the anonymous sentinel for every
/// request; <see cref="ActorIdempotencyScopeResolver"/> returns the resolved actor id when one
/// is present and falls back to the anonymous sentinel when <see cref="IActorProvider"/> has no
/// authenticated actor.
/// </summary>
public sealed class IdempotencyScopeResolverTests
{
    [Fact]
    public async Task Anonymous_resolver_returns_anonymous_sentinel()
    {
        var resolver = new AnonymousIdempotencyScopeResolver();

        var scope = await resolver.ResolveAsync(new DefaultHttpContext(), CancellationToken.None);

        scope.Should().Be("anonymous");
    }

    [Fact]
    public async Task Actor_resolver_returns_actor_id_when_present()
    {
        var actor = new Actor(
            id: ActorId.TryCreate("user-42").Unwrap(),
            permissions: System.Collections.Frozen.FrozenSet<string>.Empty,
            forbiddenPermissions: System.Collections.Frozen.FrozenSet<string>.Empty,
            attributes: System.Collections.Frozen.FrozenDictionary<string, string>.Empty);
        var provider = new Mock<IActorProvider>();
        provider.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe.From(actor));
        var resolver = new ActorIdempotencyScopeResolver(provider.Object);

        var scope = await resolver.ResolveAsync(new DefaultHttpContext(), CancellationToken.None);

        scope.Should().Be("user-42");
    }

    [Fact]
    public async Task Actor_resolver_returns_anonymous_sentinel_when_no_actor()
    {
        var provider = new Mock<IActorProvider>();
        provider.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<Actor>.None);
        var resolver = new ActorIdempotencyScopeResolver(provider.Object);

        var scope = await resolver.ResolveAsync(new DefaultHttpContext(), CancellationToken.None);

        scope.Should().Be("anonymous");
    }
}