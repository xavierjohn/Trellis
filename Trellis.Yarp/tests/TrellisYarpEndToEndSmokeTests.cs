namespace Trellis.Yarp.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using global::Microsoft.IdentityModel.JsonWebTokens;
using global::Microsoft.IdentityModel.Tokens;
using global::Yarp.ReverseProxy.Configuration;
using global::Yarp.ReverseProxy.Forwarder;

/// <summary>
/// End-to-end smoke tests exercising the actual YARP runtime pipeline. Spins up two
/// in-process TestServers (gateway + destination) wired through a custom
/// <see cref="IForwarderHttpClientFactory"/> so YARP's forwarder routes its outbound
/// HTTP request through the destination TestServer's handler. Verifies that:
/// (1) the Trellis actor-forwarding transform is invoked by YARP's transform pipeline,
/// (2) the minted JWT actually arrives at the destination as the upstream <c>Authorization</c>
/// header, and (3) the per-cluster audience selection produces the expected <c>aud</c>.
/// </summary>
/// <remarks>
/// These tests close the gap left by the per-class unit tests: those construct YARP types
/// directly and invoke the transform delegate manually, which proves the SHAPE of the code
/// against the YARP type system but does NOT prove that YARP actually invokes the transform
/// at runtime or that the resulting <see cref="System.Net.Http.HttpRequestMessage"/> headers
/// reach the destination on the wire. The integration tests below pump real HTTP requests
/// through <see cref="IHttpForwarder"/>'s real machinery.
/// </remarks>
public sealed class TrellisYarpEndToEndSmokeTests
{
    [Fact]
    public async Task GatewayRoutesRequest_MintedAuthorizationArrivesAtDestination()
    {
        var capturedAuth = new RequestCapture();
        using var destination = await StartDestinationAsync(capturedAuth);

        var actor = new Actor(
            id: "user-42",
            permissions: new HashSet<string>(StringComparer.Ordinal) { "incidents:read", "incidents:write" },
            forbiddenPermissions: new HashSet<string>(StringComparer.Ordinal) { "incidents:delete" },
            attributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["tid"] = "tenant-7" });

        using var gateway = await StartGatewayAsync(
            destinationBaseAddress: destination.GetTestServer().BaseAddress.ToString(),
            destinationHandler: destination.GetTestServer().CreateHandler(),
            actor: actor);

        using var client = gateway.GetTestServer().CreateClient();

        var response = await client.GetAsync("/anything", TestContext.Current.CancellationToken);

        var authMessage = capturedAuth.AuthorizationHeader ?? "(missing)";
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the gateway should successfully forward to the destination; destination saw method={capturedAuth.Method} path={capturedAuth.Path} auth={authMessage}");
        capturedAuth.AuthorizationHeader.Should().NotBeNull(
            "the YARP runtime MUST invoke TrellisActorForwardingRequestTransform.ApplyAsync, which sets ProxyRequest.Headers.Authorization, which YARP MUST then serialize into the actual outbound HTTP request that the destination receives");
        capturedAuth.AuthorizationHeader.Should().StartWith("Bearer ");

        var compactJws = capturedAuth.AuthorizationHeader!.Substring("Bearer ".Length);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(compactJws);
        jwt.Issuer.Should().Be("https://gateway.internal");
        jwt.Audiences.Should().Equal(["incidents"]);
        jwt.Subject.Should().Be("user-42");
        jwt.Kid.Should().Be("active-1");
        jwt.Claims.Where(c => c.Type == TrellisInternalJwtClaimNames.Permissions)
            .Select(c => c.Value).Should().BeEquivalentTo(["incidents:read", "incidents:write"]);
        jwt.Claims.Where(c => c.Type == TrellisInternalJwtClaimNames.ForbiddenPermissions)
            .Select(c => c.Value).Should().BeEquivalentTo(["incidents:delete"]);
        jwt.Claims.Single(c => c.Type == TrellisInternalJwtClaimNames.PermissionsCount).Value.Should().Be("2");
        jwt.Claims.Single(c => c.Type == TrellisInternalJwtClaimNames.ForbiddenPermissionsCount).Value.Should().Be("1");
        jwt.Claims.Single(c => c.Type == TrellisInternalJwtClaimNames.ContractVersion).Value.Should().Be("1");
        jwt.Claims.Single(c => c.Type == "tid").Value.Should().Be("tenant-7");
    }

    [Fact]
    public async Task GatewayRoutesRequest_NoAuthenticatedActor_DestinationReceivesNoAuthorizationHeader()
    {
        var capturedAuth = new RequestCapture();
        using var destination = await StartDestinationAsync(capturedAuth);

        using var gateway = await StartGatewayAsync(
            destinationBaseAddress: destination.GetTestServer().BaseAddress.ToString(),
            destinationHandler: destination.GetTestServer().CreateHandler(),
            actor: null);

        using var client = gateway.GetTestServer().CreateClient();
        // Even with a pre-existing upstream Authorization header on the inbound request, the
        // no-actor path must clear it before the request reaches the destination.
        client.DefaultRequestHeaders.Add("Authorization", "Bearer upstream-leaked-token");

        var response = await client.GetAsync("/anything", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedAuth.AuthorizationHeader.Should().BeNullOrEmpty(
            "the no-actor fail-closed posture (added in P4 round-1 security review) requires the upstream Authorization header to be CLEARED before YARP forwards the request — otherwise the external bearer token would reach the downstream service, creating a header-authority confusion vector");
    }

    // === Fixtures ===

    private sealed class RequestCapture
    {
        public string? AuthorizationHeader { get; set; }
        public string? Method { get; set; }
        public string? Path { get; set; }
    }

    private static async Task<IHost> StartDestinationAsync(RequestCapture capture)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(s => s.AddRouting());
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapGet("/{**catch-all}", context =>
                    {
                        capture.AuthorizationHeader = context.Request.Headers.TryGetValue("Authorization", out var values)
                            ? values.ToString()
                            : null;   // distinguish "missing" from "present but empty"; the test's "(missing)" diagnostic depends on the null
                        capture.Method = context.Request.Method;
                        capture.Path = context.Request.Path.ToString();
                        context.Response.StatusCode = 200;
                        return Task.CompletedTask;
                    }));
                });
            });
        return await builder.StartAsync();
    }

    private static async Task<IHost> StartGatewayAsync(
        string destinationBaseAddress,
        HttpMessageHandler destinationHandler,
        Actor? actor)
    {
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = "incidents",
            Match = new RouteMatch { Path = "/{**catch-all}" },
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "incidents",
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
            {
                ["d1"] = new DestinationConfig { Address = destinationBaseAddress },
            },
        };

        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddSingleton<IActorProvider>(new StubActorProvider(actor));

                    s.AddReverseProxy()
                        .LoadFromMemory([route], [cluster])
                        .AddTrellisActorForwarding(o =>
                        {
                            o.Issuer = "https://gateway.internal";
                            o.SigningCredentials = new SigningCredentials(
                                new RsaSecurityKey(RSA.Create(2048)) { KeyId = "active-1" },
                                SecurityAlgorithms.RsaSha256);
                            o.PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute);
                        });

                    // Redirect YARP's forwarder to the destination TestServer's handler so
                    // we do not need an actual network listener.
                    s.AddSingleton<IForwarderHttpClientFactory>(
                        new TestServerForwarderHttpClientFactory(destinationHandler));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapReverseProxy());
                });
            });
        return await builder.StartAsync();
    }

    private sealed class StubActorProvider(Actor? actor) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(actor is null ? Maybe<Actor>.None : Maybe<Actor>.From(actor));
    }

    /// <summary>
    /// Routes YARP's outbound HTTP requests through the in-memory destination TestServer
    /// instead of the real network. This is the canonical pattern for YARP integration
    /// tests that exercise the full pipeline without binding sockets.
    /// </summary>
    private sealed class TestServerForwarderHttpClientFactory(HttpMessageHandler handler)
        : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
            => new(handler, disposeHandler: false);
    }
}
