namespace Trellis.Asp.Authorization.Tests;

using Microsoft.AspNetCore.Http;
using Trellis.Authorization;
using Trellis.Testing;

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

        result1.Unwrap().Should().BeSameAs(result2.Unwrap());
        callCount.Should().Be(1, "inner provider should only be called once");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ReturnsActorFromInnerProvider()
    {
        var actor = Actor.Create("user-1", new HashSet<string>(["Write"]));
        var inner = new CountingActorProvider(actor, () => { });
        var caching = new CachingActorProvider(inner, s_nullAccessor);

        var result = (await caching.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        result.Id.Value.Should().Be("user-1");
        result.HasPermission("Write").Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequireActorAsync_InnerActorChanges_PreservesPreviouslyResolvedSnapshot(bool sameIdentity)
    {
        var original = Actor.Create("user-1", new HashSet<string>(["Read"]));
        var changed = Actor.Create(sameIdentity ? "user-1" : "user-2", new HashSet<string>(["Write"]));
        var inner = new TestActorProvider(original);
        var caching = new CachingActorProvider(inner, s_nullAccessor);
        var token = TestContext.Current.CancellationToken;
        var previouslyResolved = (await caching.GetCurrentActorAsync(token)).Unwrap();
        previouslyResolved.HasPermission("Read").Should().BeTrue();

        using var changedScope = inner.WithActor(changed);
        (await inner.GetCurrentActorAsync(token)).Unwrap().Should().BeSameAs(changed);

        var result = await caching.RequireActorAsync(token);

        result.Should().BeSameAs(previouslyResolved);
        result.HasPermission("Read").Should().BeTrue();
        result.HasPermission("Write").Should().BeFalse();
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

    #region Failure caching

    [Fact]
    public async Task GetCurrentActorAsync_InnerThrowsSynchronously_FailureIsCachedAndReplayed()
    {
        // The XML doc on CachingActorProvider promises that failures from the inner provider
        // are cached for the request scope so subsequent behaviors in the pipeline don't
        // re-run expensive prerequisites (e.g. DB lookups) that already failed. Synchronous
        // throws (typical of providers that validate prerequisites before returning a Task)
        // must be cached the same way as faulted Tasks.
        var callCount = 0;
        var inner = new ThrowingSyncProvider(() =>
        {
            callCount++;
            throw new InvalidOperationException("boom");
        });
        var caching = new CachingActorProvider(inner, s_nullAccessor);

        var act1 = async () => await caching.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        var act2 = async () => await caching.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act1.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await act2.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        callCount.Should().Be(1,
            "synchronous failures from the inner provider must be cached for the request scope, "
            + "matching the contract documented on CachingActorProvider");
    }

    #endregion

    #region Helpers

    private sealed class CountingActorProvider(Actor actor, Action onCall) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            onCall();
            return Task.FromResult(Maybe.From(actor));
        }
    }

    private sealed class TokenCapturingProvider(Actor actor, Action<CancellationToken> onCall) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            onCall(cancellationToken);
            return Task.FromResult(Maybe.From(actor));
        }
    }

    private sealed class ThrowingSyncProvider(Action onCall) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            onCall();
            return Task.FromResult(Maybe<Actor>.None); // unreachable — onCall throws
        }
    }

    #endregion
}