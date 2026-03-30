namespace Trellis.Asp.Authorization.Tests;

using Microsoft.AspNetCore.Http;
using Trellis.Authorization;

/// <summary>
/// Tests for <see cref="CachingActorProvider"/> — the caching decorator for actor providers.
/// </summary>
public class CachingActorProviderTests
{
    private static readonly IHttpContextAccessor s_nullAccessor = new HttpContextAccessor();

    #region Caching behavior

    [Fact]
    public async Task GetCurrentActorAsync_CachesResultAcrossCalls()
    {
        var callCount = 0;
        var actor = Actor.Create("user-1", new HashSet<string>(["Read"]));
        var inner = new CountingActorProvider(actor, () => callCount++);
        var caching = new CachingActorProvider(inner, s_nullAccessor);

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
        var caching = new CachingActorProvider(inner, s_nullAccessor);

        var result = await caching.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.Id.Should().Be("user-1");
        result.HasPermission("Write").Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentActorAsync_InnerReceivesRequestAbortedToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;
        var actor = Actor.Create("user-1", new HashSet<string>());
        var inner = new TokenCapturingProvider(actor, t => capturedToken = t);

        // Simulate HttpContext with a RequestAborted token
        var httpContext = new DefaultHttpContext();
        httpContext.RequestAborted = cts.Token;
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var caching = new CachingActorProvider(inner, accessor);

#pragma warning disable xUnit1051 // Intentionally omitting token to test default behavior
        await caching.GetCurrentActorAsync();
#pragma warning restore xUnit1051

        // Inner provider receives HttpContext.RequestAborted, not CancellationToken.None
        capturedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task GetCurrentActorAsync_NoHttpContext_InnerReceivesNone()
    {
        CancellationToken capturedToken = new CancellationTokenSource().Token; // non-default
        var actor = Actor.Create("user-1", new HashSet<string>());
        var inner = new TokenCapturingProvider(actor, t => capturedToken = t);
        var caching = new CachingActorProvider(inner, s_nullAccessor);

#pragma warning disable xUnit1051 // Intentionally omitting token to test default behavior
        await caching.GetCurrentActorAsync();
#pragma warning restore xUnit1051

        capturedToken.Should().Be(CancellationToken.None);
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