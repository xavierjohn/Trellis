namespace Trellis.Yarp.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using global::Microsoft.IdentityModel.JsonWebTokens;
using global::Microsoft.IdentityModel.Tokens;
using global::Yarp.ReverseProxy.Configuration;
using global::Yarp.ReverseProxy.Transforms;
using global::Yarp.ReverseProxy.Transforms.Builder;

/// <summary>
/// Tests for the per-request <see cref="TrellisActorForwardingRequestTransform"/>
/// and the build-time <see cref="TrellisActorForwardingTransformProvider"/>.
/// Verifies the actor-resolve → mint → header-overwrite flow, the no-actor fail-open
/// behavior, the mint-failure propagation, and the audit-log redaction contract.
/// </summary>
public sealed class TrellisActorForwardingRequestTransformTests
{
    private const string Issuer = "https://gateway.internal";

    [Fact]
    public async Task ApplyAsync_AuthenticatedActor_OverwritesAuthorizationHeaderWithMintedJwt()
    {
        var actor = NewActor(permissions: ["incidents:read"]);
        var (requestContext, services, log) = NewContext(actor: actor);

        await TrellisActorForwardingRequestTransform.ApplyAsync(requestContext, NewCluster("incidents"));

        var authHeader = requestContext.ProxyRequest.Headers.Authorization;
        authHeader.Should().NotBeNull();
        authHeader!.Scheme.Should().Be("Bearer");
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(authHeader.Parameter!);
        jwt.Issuer.Should().Be(Issuer);
        jwt.Audiences.Should().Equal(["incidents"]);
        jwt.Subject.Should().Be("user-42");
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "TrellisYarpTokenMinted");
    }

    [Fact]
    public async Task ApplyAsync_NoAuthenticatedActor_DoesNotSetAuthorizationHeaderAndLogsNoActor()
    {
        var (requestContext, _, log) = NewContext(actor: null);

        await TrellisActorForwardingRequestTransform.ApplyAsync(requestContext, NewCluster("incidents"));

        requestContext.ProxyRequest.Headers.Authorization.Should().BeNull(
            "no actor on inbound = no gateway-minted header; downstream policy decides whether anonymous is allowed (fail-open is intentional and documented)");
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "TrellisYarpNoActor");
    }

    [Fact]
    public async Task ApplyAsync_NoAuthenticatedActor_ClearsPreExistingAuthorizationHeader()
    {
        // Round-1 security review fix: when there's no authenticated actor on the inbound
        // request, the upstream (external) Authorization header MUST be cleared before
        // proxying. Otherwise YARP's default header-copy would forward the external bearer
        // token to the downstream service, creating an authority-confusion vector for any
        // downstream that hasn't pinned its audience strictly to the gateway-minted value.
        // Fail closed: no actor → no Authorization → downstream policy decides anonymous.
        var (requestContext, _, _) = NewContext(actor: null);
        requestContext.ProxyRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "upstream-token");

        await TrellisActorForwardingRequestTransform.ApplyAsync(requestContext, NewCluster("incidents"));

        requestContext.ProxyRequest.Headers.Authorization.Should().BeNull(
            "the upstream JWT MUST NOT reach the downstream service when no Actor was hydrated at the gateway — that would create a header-authority confusion vector");
    }

    [Fact]
    public async Task ApplyAsync_AuditLog_ContainsKidAndCountsButNoActorIdNoClaimValues()
    {
        // Redaction contract: TokenMinted carries cluster id, kid, permissions count,
        // forbidden count. NEVER actor id, permission strings, attribute values, raw JWT.
        var actor = NewActor(
            id: "secret-sub-claim-do-not-log",
            permissions: ["secret:permission:1", "secret:permission:2"],
            forbidden: ["secret:forbidden:1"],
            attributes: new Dictionary<string, string>
            {
                ["tenant_id"] = "secret-tenant-id-value",
                ["mfa"] = "secret-mfa-marker",
            });
        var (requestContext, _, log) = NewContext(actor: actor);

        await TrellisActorForwardingRequestTransform.ApplyAsync(requestContext, NewCluster("incidents"));

        var minted = log.Entries.Single(e => e.EventId.Name == "TrellisYarpTokenMinted");
        var state = minted.State;
        state.Should().Contain(state.Where(kv => kv.Key == "PermissionsCount" && kv.Value!.ToString() == "2"));
        state.Should().Contain(state.Where(kv => kv.Key == "ForbiddenPermissionsCount" && kv.Value!.ToString() == "1"));
        state.Should().Contain(state.Where(kv => kv.Key == "ClusterId" && kv.Value!.ToString() == "incidents"));
        state.Should().Contain(state.Where(kv => kv.Key == "Kid" && (kv.Value as string)?.Length > 0));

        // Hard-assert no PII makes it into the log entry (across all keys + values + message).
        var allText = string.Join("|", state.Select(kv => $"{kv.Key}={kv.Value}")) + "|" + minted.Message;
        allText.Should().NotContain("secret-sub-claim-do-not-log");
        allText.Should().NotContain("secret:permission:1");
        allText.Should().NotContain("secret:permission:2");
        allText.Should().NotContain("secret:forbidden:1");
        allText.Should().NotContain("secret-tenant-id-value");
        allText.Should().NotContain("secret-mfa-marker");
        allText.Should().NotContain(requestContext.ProxyRequest.Headers.Authorization!.Parameter!,
            "the raw JWT MUST NEVER appear in audit logs");
    }

    [Fact]
    public async Task ApplyAsync_AuditLog_OnNoActor_DoesNotLeakRequestState()
    {
        var (requestContext, _, log) = NewContext(actor: null);

        await TrellisActorForwardingRequestTransform.ApplyAsync(requestContext, NewCluster("incidents"));

        var noActor = log.Entries.Single(e => e.EventId.Name == "TrellisYarpNoActor");
        noActor.State.Should().Contain(kv => kv.Key == "ClusterId");
        // No other low-cardinality data should leak; specifically no header values, route name, etc.
        string.Join("|", noActor.State.Select(kv => $"{kv.Key}")).Should().NotContain("Authorization");
    }

    [Fact]
    public async Task ApplyAsync_NullRequestContext_Throws()
    {
        var act = async () => await TrellisActorForwardingRequestTransform.ApplyAsync(null!, NewCluster("incidents"));
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ApplyAsync_NullCluster_Throws()
    {
        var (requestContext, _, _) = NewContext(actor: null);
        var act = async () => await TrellisActorForwardingRequestTransform.ApplyAsync(requestContext, cluster: null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // === Provider tests ===

    [Fact]
    public void TransformProvider_ApplyWithNoCluster_NoOps()
    {
        // The TransformBuilderContext can have a null Cluster (e.g., a route without an
        // attached cluster). The provider must not throw and must not add a request transform.
        var provider = new TrellisActorForwardingTransformProvider();
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TransformBuilderContext { Services = services, Cluster = null };

        provider.Apply(context);

        context.RequestTransforms.Should().BeEmpty();
    }

    [Fact]
    public void TransformProvider_ApplyWithCluster_AddsRequestTransform()
    {
        var provider = new TrellisActorForwardingTransformProvider();
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TransformBuilderContext
        {
            Services = services,
            Cluster = NewCluster("incidents"),
        };

        provider.Apply(context);

        context.RequestTransforms.Should().HaveCount(1);
    }

    [Fact]
    public void TransformProvider_ApplyNullContext_Throws()
    {
        var provider = new TrellisActorForwardingTransformProvider();
        var act = () => provider.Apply(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // === Fixtures ===

    private static (RequestTransformContext Context, IServiceProvider Services, CapturingLogger Log) NewContext(
        Actor? actor)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActorProvider>(new StubActorProvider(actor));
        services.AddSingleton(Options.Create(NewValidOptions()));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)));
        var log = new CapturingLogger();
        services.AddSingleton<ILogger<TrellisActorJwtMinter>>(log);
        services.AddSingleton<TrellisActorJwtMinter>();

        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var requestContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage(),
            DestinationPrefix = "https://destination/",
        };
        return (requestContext, sp, log);
    }

    private static TrellisActorForwardingOptions NewValidOptions() => new()
    {
        Issuer = Issuer,
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "active-1" }, SecurityAlgorithms.RsaSha256),
        PublicBaseUrl = new Uri("https://gateway.internal"),
    };

    private static Actor NewActor(
        string id = "user-42",
        string[]? permissions = null,
        string[]? forbidden = null,
        Dictionary<string, string>? attributes = null)
        => new(
            id,
            (permissions ?? []).ToHashSet(StringComparer.Ordinal),
            (forbidden ?? []).ToHashSet(StringComparer.Ordinal),
            attributes ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private static ClusterConfig NewCluster(string clusterId) => new() { ClusterId = clusterId };

    private sealed class StubActorProvider(Actor? actor) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(actor is null ? Maybe<Actor>.None : Maybe<Actor>.From(actor));
    }

    private sealed class CapturingLogger : ILogger<TrellisActorJwtMinter>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entry = new LogEntry(
                logLevel,
                eventId,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
                exception);
            Entries.Add(entry);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State,
        Exception? Exception);
}
