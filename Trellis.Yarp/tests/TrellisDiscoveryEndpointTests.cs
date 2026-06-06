namespace Trellis.Yarp.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using global::Microsoft.IdentityModel.Tokens;

/// <summary>
/// Tests for <see cref="TrellisDiscoveryEndpointRouteBuilderExtensions"/>. Asserts the
/// OIDC discovery document shape, the JWKS rotation-ring composition, symmetric-key
/// defense-in-depth, and the "URLs come from options not request context" invariant.
/// </summary>
public sealed class TrellisDiscoveryEndpointTests
{
    [Fact]
    public void BuildDiscoveryDocument_UsesIssuerFromOptions()
    {
        var options = NewValidOptions(issuer: "https://my-gateway.test");

        var doc = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildDiscoveryDocument(
            options,
            TrellisDiscoveryEndpointRouteBuilderExtensions.DefaultJwksPath);

        doc.Issuer.Should().Be("https://my-gateway.test");
    }

    [Fact]
    public void BuildDiscoveryDocument_JwksUriIsAbsoluteAndCombinedFromPublicBaseUrl()
    {
        var options = NewValidOptions(publicBaseUrl: new Uri("https://gw.example.com/"));

        var doc = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildDiscoveryDocument(
            options,
            "/.well-known/jwks.json");

        doc.JwksUri.Should().Be("https://gw.example.com/.well-known/jwks.json");
    }

    [Fact]
    public void BuildDiscoveryDocument_SigningAlgsIsOnlyActiveAlgorithm()
    {
        // PR review feedback: discovery doc must advertise the algorithm actually in use,
        // not a hard-coded RS*/ES* list. Downstream consumers using ValidAlgorithms = ["RS256"]
        // (Recipe 33) need the published list to match what the gateway mints.
        var options = NewValidOptions();

        var doc = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildDiscoveryDocument(
            options,
            "/.well-known/jwks.json");

        doc.IdTokenSigningAlgValuesSupported.Should().Equal([SecurityAlgorithms.RsaSha256]);
    }

    [Fact]
    public void BuildJwks_SingleActiveKey_OneEntry()
    {
        var options = NewValidOptions();

        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(options);

        var keys = jwks["keys"]!.AsArray();
        keys.Should().HaveCount(1);
        keys[0]!["kid"]!.GetValue<string>().Should().Be("active-1");
        keys[0]!["kty"]!.GetValue<string>().Should().Be("RSA");
        keys[0]!["use"]!.GetValue<string>().Should().Be("sig");
        keys[0]!["alg"]!.GetValue<string>().Should().Be(SecurityAlgorithms.RsaSha256);
    }

    [Fact]
    public void BuildJwks_RotationRing_AllKeysAppearActiveFirst()
    {
        var previous1 = NewRsaKey("prev-1");
        var previous2 = NewRsaKey("prev-2");
        var options = NewValidOptions(previousSigningKeys: [previous1, previous2]);

        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(options);

        var keys = jwks["keys"]!.AsArray();
        keys.Should().HaveCount(3);
        keys[0]!["kid"]!.GetValue<string>().Should().Be("active-1");
        keys[1]!["kid"]!.GetValue<string>().Should().Be("prev-1");
        keys[2]!["kid"]!.GetValue<string>().Should().Be("prev-2");
    }

    [Fact]
    public void BuildJwks_RsaKey_PublicFieldsPresentNoPrivateFields()
    {
        var options = NewValidOptions();

        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(options);

        var key = jwks["keys"]!.AsArray()[0]!.AsObject();
        key.Should().ContainKey("n", "RSA modulus must be published");
        key.Should().ContainKey("e", "RSA public exponent must be published");
        // Private RSA components MUST NEVER appear in JWKS.
        key.Should().NotContainKey("d");
        key.Should().NotContainKey("p");
        key.Should().NotContainKey("q");
        key.Should().NotContainKey("dp");
        key.Should().NotContainKey("dq");
        key.Should().NotContainKey("qi");
    }

    [Fact]
    public void BuildJwks_SymmetricKeyInRotationRing_Skipped()
    {
        // Defense in depth: TrellisActorForwardingOptionsValidator already rejects symmetric
        // keys at startup. If a future refactor loosens that, the JWKS builder must still
        // refuse to publish them (publishing symmetric material would leak the signing secret).
        var symmetric = new SymmetricSecurityKey(new byte[64]) { KeyId = "sym-prev" };
        var options = NewValidOptions(previousSigningKeys: [symmetric]);

        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(options);

        var keys = jwks["keys"]!.AsArray();
        keys.Should().HaveCount(1, "only the active asymmetric key should be published; the symmetric previous key must be silently dropped (defense in depth)");
        keys.Select(k => k!["kid"]!.GetValue<string>()).Should().NotContain("sym-prev");
    }

    [Fact]
    public void BuildJwks_NoPreviousKeys_OnlyActiveKey()
    {
        var options = NewValidOptions();

        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(options);

        jwks["keys"]!.AsArray().Should().HaveCount(1);
    }

    [Theory]
    [InlineData(SecurityAlgorithms.RsaSha256)]
    [InlineData(SecurityAlgorithms.RsaSha384)]
    [InlineData(SecurityAlgorithms.RsaSha512)]
    public void BuildJwks_AlgFieldMirrorsActiveSigningCredentialsAlgorithm(string algorithm)
    {
        // PR review feedback (round 3): the JWKS `alg` hint must match
        // options.SigningCredentials.Algorithm exactly — not be derived from the key TYPE
        // (which previously defaulted to RS256/ES256 regardless of whether the consumer
        // configured RS384/RS512/etc). Misalignment between JWKS alg and discovery doc
        // alg would mislead metadata consumers and break ValidAlgorithms enforcement.
        var rsa = new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "active-1" };
        var previous = new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "prev-1" };
        var options = new TrellisActorForwardingOptions
        {
            Issuer = "https://gateway.internal",
            SigningCredentials = new SigningCredentials(rsa, algorithm),
            PreviousSigningKeys = [previous],
            PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute),
        };

        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(options);

        var keys = jwks["keys"]!.AsArray();
        keys.Should().HaveCount(2);
        keys[0]!["alg"]!.GetValue<string>().Should().Be(algorithm,
            "the active key's JWKS alg field MUST match the configured signing algorithm exactly");
        keys[1]!["alg"]!.GetValue<string>().Should().Be(algorithm,
            "every key in the rotation ring inherits the active signing algorithm; v1 assumes rotation stays within a single algorithm family");
    }

    [Fact]
    public void BuildJwks_UnsupportedSecurityKeyTypeInRotationRing_FailsClosedSilently()
    {
        // PR review feedback (round 2): the AppendKey defense-in-depth path must NOT throw
        // when JsonWebKeyConverter doesn't know about the key type. A throw here turns the
        // entire JWKS endpoint into a 500, which cascades to "every downstream service
        // can't validate any token." Silent-skip is the correct fail-closed behavior for
        // this key only.
        //
        // We construct an unsupported SecurityKey by hand. X509SecurityKey is rejected by
        // the validator but ConvertFromSecurityKey for it returns a JWK without the n/e/x/y
        // fields the builder emits — so it would produce an unusable JWK entry rather than
        // throw. The reliable "throws NotSupportedException" path is a JsonWebKey wrapper.
        var jwkInput = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.RSA,
            N = "abc",
            E = "AQAB",
            KeyId = "jwk-input-1",
        };
        var options = NewValidOptions(previousSigningKeys: [jwkInput]);

        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(options);

        var keys = jwks["keys"]!.AsArray();
        keys.Should().HaveCount(1,
            "the unsupported JsonWebKey input must be silently skipped — throwing would 500 the entire JWKS endpoint");
        keys.Select(k => k!["kid"]!.GetValue<string>()).Should().NotContain("jwk-input-1");
    }

    [Fact]
    public void BuildJwks_NullEntryInRotationRing_FailsClosedSilently()
    {
        // PR review feedback (round 4): startup validation rejects null entries in
        // PreviousSigningKeys, but a runtime mutation of IOptionsMonitor (or a future
        // refactor that loosens validation) could still produce one. Without a null guard,
        // JsonWebKeyConverter.ConvertFromSecurityKey(null) throws NullReferenceException
        // and 500s the JWKS endpoint, contradicting the method's own defense-in-depth
        // rationale. Silent-skip keeps the rest of the ring published.
        var options = NewValidOptions(previousSigningKeys: [null!]);

        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(options);

        var keys = jwks["keys"]!.AsArray();
        keys.Should().HaveCount(1, "only the active key should be published; the null previous entry must be silently dropped");
    }

    [Fact]
    public async Task MapTrellisDiscoveryEndpoint_ReturnedConventionBuilder_AppliesToBothEndpoints()
    {
        // PR review feedback (round 2): the returned IEndpointConventionBuilder MUST apply
        // chained conventions (.WithTags, .RequireHost, caching metadata, etc.) to BOTH the
        // OIDC and JWKS endpoints. Previously it returned only the OIDC endpoint's builder
        // and any chained call would silently configure only one of the two routes.
        //
        // Asserting via metadata count: tag the endpoints with a sentinel marker via the
        // returned composite builder, then enumerate the EndpointDataSource and count how
        // many endpoints carry the marker. If the builder fans out correctly, both
        // discovery endpoints have the marker. If only one (the old behavior), only one.
        var marker = "trellis-yarp-composite-builder-test-marker";
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddSingleton(Options.Create(NewValidOptions()));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e =>
                        e.MapTrellisDiscoveryEndpoint().WithMetadata(marker));
                });
            });
        using var host = await builder.StartAsync(TestContext.Current.CancellationToken);

        var dataSource = host.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>();
        var endpointsWithMarker = dataSource.Endpoints
            .Where(ep => ep.Metadata.GetMetadata<string>() == marker)
            .ToList();

        endpointsWithMarker.Should().HaveCount(2,
            "chained conventions on the returned builder must reach BOTH the OIDC AND the JWKS endpoint registrations");
    }

    [Fact]
    public async Task MapTrellisDiscoveryEndpoint_OidcEndpoint_Returns200WithCorrectShape()
    {
        using var app = BuildTestApp();
        using var client = app.GetTestServer().CreateClient();

        var response = await client.GetAsync(TrellisDiscoveryEndpointRouteBuilderExtensions.DefaultOidcDiscoveryPath, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        doc.Should().NotBeNull();
        doc!["issuer"]!.GetValue<string>().Should().Be("https://gateway.internal");
        doc["jwks_uri"]!.GetValue<string>().Should().Be("https://gateway.internal/.well-known/jwks.json");
    }

    [Fact]
    public async Task MapTrellisDiscoveryEndpoint_JwksEndpoint_Returns200WithKeys()
    {
        using var app = BuildTestApp();
        using var client = app.GetTestServer().CreateClient();

        var response = await client.GetAsync(TrellisDiscoveryEndpointRouteBuilderExtensions.DefaultJwksPath, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        doc.Should().NotBeNull();
        doc!["keys"]!.AsArray().Should().HaveCount(1);
        doc["keys"]!.AsArray()[0]!["kid"]!.GetValue<string>().Should().Be("active-1");
    }

    [Fact]
    public async Task MapTrellisDiscoveryEndpoint_CustomPaths_AreRespected()
    {
        using var app = BuildTestApp(
            oidcPath: "/.trellis/oidc",
            jwksPath: "/.trellis/jwks");
        using var client = app.GetTestServer().CreateClient();

        var oidcResponse = await client.GetAsync("/.trellis/oidc", TestContext.Current.CancellationToken);
        var jwksResponse = await client.GetAsync("/.trellis/jwks", TestContext.Current.CancellationToken);

        oidcResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        jwksResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await oidcResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        doc!["jwks_uri"]!.GetValue<string>().Should().Be("https://gateway.internal/.trellis/jwks");
    }

    [Fact]
    public async Task MapTrellisDiscoveryEndpoint_JwksUri_IgnoresHttpRequestHostHeader()
    {
        // Even though the request comes through TestServer with a host header, the
        // jwks_uri MUST be built from options.PublicBaseUrl. A spoofed Host header
        // (typical reverse-proxy attack vector) MUST NOT influence the published URL.
        using var app = BuildTestApp();
        using var client = app.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Host = "attacker.example";

        var response = await client.GetAsync(TrellisDiscoveryEndpointRouteBuilderExtensions.DefaultOidcDiscoveryPath, TestContext.Current.CancellationToken);
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        doc!["jwks_uri"]!.GetValue<string>().Should().StartWith("https://gateway.internal/",
            "jwks_uri must come from PublicBaseUrl, not the request's spoofable Host header");
        doc["issuer"]!.GetValue<string>().Should().Be("https://gateway.internal");
    }

    [Fact]
    public void MapTrellisDiscoveryEndpoint_NullEndpoints_Throws()
    {
        IEndpointRouteBuilder? endpoints = null;
        var act = () => endpoints!.MapTrellisDiscoveryEndpoint();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDiscoveryDocument_NullOptions_Throws()
    {
        var act = () => TrellisDiscoveryEndpointRouteBuilderExtensions.BuildDiscoveryDocument(null!, "/.well-known/jwks.json");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDiscoveryDocument_EmptyJwksPath_Throws()
    {
        var options = NewValidOptions();
        var act = () => TrellisDiscoveryEndpointRouteBuilderExtensions.BuildDiscoveryDocument(options, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildJwks_NullOptions_Throws()
    {
        var act = () => TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // === Fixtures ===

    private static TrellisActorForwardingOptions NewValidOptions(
        string? issuer = null,
        Uri? publicBaseUrl = null,
        IReadOnlyList<SecurityKey>? previousSigningKeys = null)
        => new()
        {
            Issuer = issuer ?? "https://gateway.internal",
            SigningCredentials = new SigningCredentials(NewRsaKey("active-1"), SecurityAlgorithms.RsaSha256),
            PreviousSigningKeys = previousSigningKeys ?? [],
            PublicBaseUrl = publicBaseUrl ?? new Uri("https://gateway.internal", UriKind.Absolute),
        };

    private static RsaSecurityKey NewRsaKey(string kid)
        => new(RSA.Create(2048)) { KeyId = kid };

    private static IHost BuildTestApp(string? oidcPath = null, string? jwksPath = null)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(Options.Create(NewValidOptions()));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapTrellisDiscoveryEndpoint(oidcPath, jwksPath));
                });
            });
        var host = builder.Start();
        return host;
    }
}
