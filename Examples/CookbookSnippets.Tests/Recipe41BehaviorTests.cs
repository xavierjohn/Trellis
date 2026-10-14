namespace CookbookSnippets.Tests;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CookbookSnippets.Recipe41;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.Testing;

public class Recipe41BehaviorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public async Task CanonicalActorId_SharedNormalizer_ProviderAndSynchronousLimiterAgree(string claim)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claim, " user ")], "test")),
        };
        var provider = new SharedClaimsActorProvider(new HttpContextAccessor { HttpContext = context });

        await provider.ShouldHaveConsistentActorIdentityAsync(provider, Token);
        ActorRateLimitingSample.ClaimsPartitionKey(context).Should().Be("actor:sample:user");
        (await provider.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("sample:user");
    }

    [Theory]
    [InlineData("192.0.2.10", "::ffff:192.0.2.10", true)]
    [InlineData("2001:db8:1234:5678::1", "2001:db8:1234:5678::ffff", true)]
    [InlineData("2001:db8:1234:5678::1", "2001:db8:1234:5679::1", false)]
    public void ClaimsPartitionKey_AnonymousNetwork_NormalizesMappedAddressesAndExampleIpv6Prefix(
        string first, string second, bool same)
    {
        var a = new DefaultHttpContext();
        var b = new DefaultHttpContext();
        a.Connection.RemoteIpAddress = IPAddress.Parse(first);
        b.Connection.RemoteIpAddress = IPAddress.Parse(second);

        (ActorRateLimitingSample.ClaimsPartitionKey(a) == ActorRateLimitingSample.ClaimsPartitionKey(b))
            .Should().Be(same);
    }

    [Fact]
    public void ClaimsPartitionKey_MissingAddress_UsesSharedUnknownBucket() =>
        ActorRateLimitingSample.ClaimsPartitionKey(new DefaultHttpContext()).Should().Be("network:unknown");

    [Fact]
    public void PrefetchedPartitionKey_MissingBridge_FailsInsteadOfTreatingMissingSnapshotAsAnonymous()
    {
        var act = () => ActorRateLimitingSample.PrefetchedPartitionKey(new DefaultHttpContext());
        act.Should().Throw<InvalidOperationException>().WithMessage("*prefetch did not run*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RateLimiter_SameActorAcrossRequests_RejectsBurstWithoutFullActorLookup(bool prefetch)
    {
        await using var host = await CreateAsync(prefetch);
        using var first = await SendAsync(host.Client, HttpMethod.Get, "/limited", "user");
        using var second = await SendAsync(host.Client, HttpMethod.Get, "/limited", "user");
        using var rejected = await SendAsync(host.Client, HttpMethod.Get, "/limited", "user");
        using var otherActor = await SendAsync(host.Client, HttpMethod.Get, "/limited", "other");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await rejected.Content.ReadAsStringAsync(Token)).Should().Contain("rate-limit.exceeded")
            .And.NotContain("actor:sample:user");
        otherActor.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Counter.ActorCalls.Should().Be(0);
        host.Counter.IdentityCalls.Should().Be(prefetch ? 4 : 0);
    }

    [Fact]
    public async Task IdentityPrefetch_ReadyRoute_DoesNotResolveIdentityOrLoadActor()
    {
        await using var host = await CreateAsync(prefetch: true);
        host.Counter.ThrowOnIdentity = true;
        using var response = await SendAsync(host.Client, HttpMethod.Get, "/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Counter.IdentityCalls.Should().Be(0);
        host.Counter.ActorCalls.Should().Be(0);
    }

    [Fact]
    public async Task IdentityPrefetch_NoneSnapshot_UsesNetworkBucketRatherThanReportingMissingBridge()
    {
        await using var host = await CreateAsync(prefetch: true);
        using var first = await SendAsync(host.Client, HttpMethod.Get, "/limited");
        using var second = await SendAsync(host.Client, HttpMethod.Get, "/limited");
        using var rejected = await SendAsync(host.Client, HttpMethod.Get, "/limited");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        host.Counter.ActorCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MiddlewareOrder_AuthorizationDenial_ConsumesPermitsOnlyWhenLimiterRunsFirst(bool authorizationFirst)
    {
        await using var host = await CreateAsync(prefetch: true, authorizationFirst);
        using var first = await SendAsync(host.Client, HttpMethod.Post, "/payments");
        using var second = await SendAsync(host.Client, HttpMethod.Post, "/payments");
        using var third = await SendAsync(host.Client, HttpMethod.Post, "/payments");

        first.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        third.StatusCode.Should().Be(authorizationFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests);
        host.Counter.ActorCalls.Should().Be(0);
        host.Counter.IdentityCalls.Should().Be(authorizationFirst ? 0 : 3);
    }

    [Fact]
    public async Task MiddlewareOrder_IdempotentReplay_ConsumesPermitAndStillScopesThroughFullActor()
    {
        await using var host = await CreateAsync(prefetch: true);
        using var first = await SendAsync(host.Client, HttpMethod.Post, "/payments", "user", "\"operation\"");
        using var replay = await SendAsync(host.Client, HttpMethod.Post, "/payments", "user", "\"operation\"");
        using var rejected = await SendAsync(host.Client, HttpMethod.Post, "/payments", "user", "\"operation\"");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync(Token)).Should().Be(await first.Content.ReadAsStringAsync(Token));
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        host.Counter.ActorCalls.Should().Be(2);
        host.Counter.IdentityCalls.Should().Be(3);
    }

    private static async Task<TestApplication> CreateAsync(bool prefetch, bool authorizationFirst = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", static _ => { });
        ActorRateLimitingSample.ConfigureServices(builder.Services, prefetch);
        var counter = new LookupCounter();
        builder.Services.AddSingleton(counter);
        builder.Services.AddCachingActorProvider<CountingProvider>();
        var app = builder.Build();
        ActorRateLimitingSample.ConfigurePipeline(app, prefetch, authorizationFirst);
        await app.StartAsync(Token);
        return new TestApplication(app, app.GetTestClient(), counter);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string? subject = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (subject is not null)
            request.Headers.Add("X-Subject", subject);
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request, Token);
    }

    private sealed record TestApplication(WebApplication App, HttpClient Client, LookupCounter Counter) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }

    private sealed class LookupCounter
    {
        private int _identityCalls;
        private int _actorCalls;
        public int IdentityCalls => Volatile.Read(ref _identityCalls);
        public int ActorCalls => Volatile.Read(ref _actorCalls);
        public bool ThrowOnIdentity { get; set; }
        public void ResolveIdentity() => Interlocked.Increment(ref _identityCalls);
        public void ResolveActor() => Interlocked.Increment(ref _actorCalls);
    }

    private sealed class CountingProvider(IHttpContextAccessor accessor, LookupCounter counter)
        : IActorProvider, IActorIdentityProvider
    {
        public async ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default)
        {
            counter.ResolveIdentity();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (counter.ThrowOnIdentity)
                throw new InvalidOperationException("Identity resolution was not expected.");
            return SharedClaimsActorProvider.CanonicalActorId(accessor.HttpContext!.User);
        }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            counter.ResolveActor();
            return Task.FromResult(SharedClaimsActorProvider.CanonicalActorId(accessor.HttpContext!.User)
                .Map(id => Actor.Create(id, new HashSet<string>())));
        }
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers["X-Subject"].FirstOrDefault();
            return Task.FromResult(subject is null
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], Scheme.Name)), Scheme.Name)));
        }
    }
}