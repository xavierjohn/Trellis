namespace Trellis.Asp.Authorization.Tests;

using Trellis.Authorization;

/// <summary>
/// Tests for <see cref="CachingActorProvider"/> — the caching decorator for actor providers.
/// </summary>
public class CachingActorProviderTests
{
    #region Caching behavior

    [Fact]
    public async Task GetCurrentActorAsync_CachesResultAcrossCalls()
    {
        var callCount = 0;
        var actor = Actor.Create("user-1", new HashSet<string>(["Read"]));
        var inner = new CountingActorProvider(actor, () => callCount++);
        var caching = new CachingActorProvider(inner);

        var result1 = await caching.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        var result2 = await caching.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result1.Should().BeSameAs(result2);
        callCount.Should().Be(1, "inner provider should only be called once");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ReturnsActorFromInnerProvider()
    {
        var actor = Actor.Create("user-1", new HashSet<string>(["Write"]));
        var inner = new CountingActorProvider(actor, () => { });
        var caching = new CachingActorProvider(inner);

        var result = await caching.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.Id.Should().Be("user-1");
        result.HasPermission("Write").Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentActorAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;
        var actor = Actor.Create("user-1", new HashSet<string>());
        var inner = new TokenCapturingProvider(actor, t => capturedToken = t);
        var caching = new CachingActorProvider(inner);

        await caching.GetCurrentActorAsync(cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    #endregion

    #region Helpers

    private sealed class CountingActorProvider(Actor actor, Action onCall) : IActorProvider
    {
        public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            onCall();
            return Task.FromResult(actor);
        }
    }

    private sealed class TokenCapturingProvider(Actor actor, Action<CancellationToken> onCall) : IActorProvider
    {
        public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            onCall(cancellationToken);
            return Task.FromResult(actor);
        }
    }

    #endregion
}
