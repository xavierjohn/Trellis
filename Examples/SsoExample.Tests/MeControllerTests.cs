namespace SsoExample.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

/// <summary>
/// Integration tests for the SSO sample. Verifies that the Trellis actor
/// pipeline is wired in Development (DevelopmentActorProvider via X-Test-Actor)
/// and that Production correctly rejects unauthenticated requests via the
/// JWT bearer + fallback authorization policy.
/// </summary>
public class MeControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MeControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Dev => _factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));

    [Fact]
    public async Task Get_Me_InDevelopment_WithoutHeader_Returns200_AndDefaultActor()
    {
        using var client = Dev.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/Me", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Ct);
        body.Should().NotBeNull();
        body!["id"]!.GetValue<string>().Should().Be("development");
    }

    [Fact]
    public async Task Get_Me_InDevelopment_WithTestActorHeader_ExposesActorIdToController()
    {
        using var client = Dev.CreateClient();
        var actorJson = """{"Id":"alice","Permissions":["orders:read"],"Attributes":{"tid":"tenant-1"}}""";

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/Me", UriKind.Relative));
        request.Headers.Add("X-Test-Actor", actorJson);
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Ct);
        body.Should().NotBeNull();
        body!["id"]!.GetValue<string>().Should().Be("alice");
    }

    private static readonly string[] s_expectedPermissions = ["orders:read", "orders:create"];

    [Fact]
    public async Task Get_Me_InDevelopment_WithTestActorHeader_ExposesPermissions()
    {
        using var client = Dev.CreateClient();
        var actorJson = """{"Id":"bob","Permissions":["orders:read","orders:create"]}""";

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/Me", UriKind.Relative));
        request.Headers.Add("X-Test-Actor", actorJson);
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Ct);
        body.Should().NotBeNull();
        var permissions = body!["permissions"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .ToHashSet();
        permissions.Should().BeEquivalentTo(s_expectedPermissions);
    }

    [Fact]
    public async Task Get_Me_InProduction_WithoutBearerToken_Returns401_Unauthorized()
    {
        using var prod = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var client = prod.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync(new Uri("/api/Me", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}