namespace Trellis.Asp.Authorization.Tests;

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="DevelopmentActorProvider"/> — the development/testing actor provider
/// that reads the <c>X-Test-Actor</c> header with a production environment guard.
/// </summary>
public class DevelopmentActorProviderTests
{
    private static DevelopmentActorProvider CreateProvider(
        HttpContext? httpContext = null,
        bool isProduction = false,
        DevelopmentActorOptions? options = null,
        ILogger<DevelopmentActorProvider>? logger = null,
        string? environmentName = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var envName = environmentName ?? (isProduction ? Environments.Production : Environments.Development);
        var env = new StubHostEnvironment(envName);

        var opts = Options.Create(options ?? new DevelopmentActorOptions());
        logger ??= NullLogger<DevelopmentActorProvider>.Instance;

        return new DevelopmentActorProvider(accessor, env, opts, logger);
    }

    private static DefaultHttpContext CreateHttpContextWithHeader(string headerValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[DevelopmentActorProvider.HeaderName] = headerValue;
        return context;
    }

    private static string CreateActorJson(
        string id,
        string[]? permissions = null,
        string[]? forbiddenPermissions = null,
        Dictionary<string, string>? attributes = null)
    {
        var json = new JsonObject
        {
            ["Id"] = id,
            ["Permissions"] = new JsonArray((permissions ?? []).Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            ["ForbiddenPermissions"] = new JsonArray((forbiddenPermissions ?? []).Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            ["Attributes"] = new JsonObject((attributes ?? []).Select(kvp => new KeyValuePair<string, JsonNode?>(kvp.Key, JsonValue.Create(kvp.Value))).ToList())
        };
        return json.ToJsonString();
    }

    #region Environment guard — Production

    [Fact]
    public async Task GetCurrentActor_Production_WithTestActorHeader_ThrowsInvalidOperationException()
    {
        var header = CreateActorJson("attacker", ["admin:all"]);
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context, isProduction: true);

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not allowed outside Development*");
    }

    [Fact]
    public async Task GetCurrentActor_Production_WithoutHeader_ThrowsInvalidOperationException()
    {
        var context = new DefaultHttpContext();
        var provider = CreateProvider(context, isProduction: true);

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not allowed outside Development*");
    }

    [Fact]
    public async Task GetCurrentActor_Staging_WithoutHeader_ThrowsInvalidOperationException()
    {
        var context = new DefaultHttpContext();
        var provider = CreateProvider(context, environmentName: "Staging");

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not allowed outside Development*");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("UAT")]
    [InlineData("PreProd")]
    public async Task GetCurrentActor_NonDevelopmentEnvironment_WithHeader_ThrowsInvalidOperationException(string environment)
    {
        var header = CreateActorJson("attacker", ["admin:all"]);
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context, environmentName: environment);

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not allowed outside Development*");
    }

    #endregion

    #region Development — Valid header parsing

    [Fact]
    public async Task GetCurrentActor_Development_ValidHeader_ReturnsActorWithIdAndPermissions()
    {
        var header = CreateActorJson("user-1", ["orders:create", "orders:read"]);
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-1");
        actor.Permissions.Should().BeEquivalentTo(["orders:create", "orders:read"]);
    }

    [Fact]
    public async Task GetCurrentActor_Development_ValidHeader_ParsesForbiddenPermissions()
    {
        var header = CreateActorJson("user-1", ["orders:create"], ["orders:delete"]);
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.ForbiddenPermissions.Should().Contain("orders:delete");
        actor.HasPermission("orders:delete").Should().BeFalse("deny overrides allow");
    }

    [Fact]
    public async Task GetCurrentActor_Development_ValidHeader_ParsesAttributes()
    {
        var attrs = new Dictionary<string, string> { ["tid"] = "tenant-1", ["region"] = "us-west" };
        var header = CreateActorJson("user-1", attributes: attrs);
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.GetAttribute("tid").Should().Be("tenant-1");
        actor.GetAttribute("region").Should().Be("us-west");
    }

    [Fact]
    public async Task GetCurrentActor_Development_HeaderWithEmptyPermissions_ReturnsActorWithNoPermissions()
    {
        var header = CreateActorJson("user-1");
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-1");
        actor.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentActor_Development_MinimalHeader_OnlyIdRequired()
    {
        var header = """{"Id":"minimal-user"}""";
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("minimal-user");
        actor.Permissions.Should().BeEmpty();
        actor.ForbiddenPermissions.Should().BeEmpty();
        actor.Attributes.Should().BeEmpty();
    }

    #endregion

    #region Development — No header (fallback to default actor)

    [Fact]
    public async Task GetCurrentActor_Development_NoHeader_ReturnsDefaultActor()
    {
        var context = new DefaultHttpContext();
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("development");
        actor.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentActor_Development_NoHeader_UsesConfiguredDefaults()
    {
        var context = new DefaultHttpContext();
        var options = new DevelopmentActorOptions
        {
            DefaultActorId = "admin",
            DefaultPermissions = new HashSet<string>(["orders:create", "orders:read"])
        };
        var provider = CreateProvider(context, options: options);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("admin");
        actor.Permissions.Should().BeEquivalentTo(["orders:create", "orders:read"]);
    }

    [Fact]
    public async Task GetCurrentActor_NoHttpContext_ReturnsDefaultActor()
    {
        var provider = CreateProvider(httpContext: null);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("development");
    }

    [Fact]
    public async Task GetCurrentActor_EmptyHeader_ReturnsDefaultActor()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[DevelopmentActorProvider.HeaderName] = "";
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("development");
    }

    #endregion

    #region Development — Malformed header

    [Fact]
    public async Task GetCurrentActor_Development_MalformedJson_ByDefault_Throws()
    {
        // Security: a MALFORMED header is a developer error, distinct from an ABSENT header
        // (which intentionally yields the default actor). By default the provider now rejects it
        // rather than silently falling back to the configured default actor — which would be a
        // silent privilege elevation when DefaultPermissions is non-empty.
        var context = CreateHttpContextWithHeader("not valid json {{{");
        var provider = CreateProvider(context);

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Malformed*");
    }

    [Fact]
    public async Task GetCurrentActor_Development_MalformedJson_FallsBackToDefault()
    {
        // Opt-out: with ThrowOnMalformedHeader=false the legacy lenient behavior is preserved.
        var context = CreateHttpContextWithHeader("not valid json {{{");
        var provider = CreateProvider(context, options: new DevelopmentActorOptions { ThrowOnMalformedHeader = false });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("development");
    }

    [Fact]
    public async Task GetCurrentActor_Development_MissingId_FallsBackToDefault()
    {
        var context = CreateHttpContextWithHeader("""{"Permissions":["orders:read"]}""");
        var provider = CreateProvider(context, options: new DevelopmentActorOptions { ThrowOnMalformedHeader = false });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("development");
    }

    [Fact]
    public async Task GetCurrentActor_Development_EmptyId_FallsBackToDefault()
    {
        var context = CreateHttpContextWithHeader("""{"Id":"","Permissions":["orders:read"]}""");
        var provider = CreateProvider(context, options: new DevelopmentActorOptions { ThrowOnMalformedHeader = false });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("development");
    }

    [Fact]
    public async Task GetCurrentActor_Development_WhitespaceOnlyId_FallsBackToDefault()
    {
        // Whitespace-only Id is treated as a malformed header (same as missing/empty Id).
        // Without this guard, ActorId.TryCreate would reject the value and Actor.Create
        // would throw ArgumentException instead of producing the configured default actor.
        var context = CreateHttpContextWithHeader("""{"Id":"   ","Permissions":["orders:read"]}""");
        var provider = CreateProvider(context, options: new DevelopmentActorOptions { ThrowOnMalformedHeader = false });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("development");
    }

    [Fact]
    public async Task GetCurrentActor_Development_MalformedJson_ThrowOnMalformedEnabled_Throws()
    {
        var context = CreateHttpContextWithHeader("not valid json");
        var options = new DevelopmentActorOptions { ThrowOnMalformedHeader = true };
        var provider = CreateProvider(context, options: options);

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Malformed*");
    }

    [Fact]
    public async Task GetCurrentActor_Development_MissingId_ThrowOnMalformedEnabled_Throws()
    {
        var context = CreateHttpContextWithHeader("""{"Permissions":["orders:read"]}""");
        var options = new DevelopmentActorOptions { ThrowOnMalformedHeader = true };
        var provider = CreateProvider(context, options: options);

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Malformed*Id*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetCurrentActor_Development_EmptyOrWhitespaceId_ThrowOnMalformedEnabled_Throws(string idValue)
    {
        // Parallel to MissingId: when ThrowOnMalformedHeader=true, an empty or whitespace-only
        // Id is malformed-header input and must throw rather than silently fall back to the
        // default actor.
        var context = CreateHttpContextWithHeader($$"""{"Id":"{{idValue}}","Permissions":["orders:read"]}""");
        var options = new DevelopmentActorOptions { ThrowOnMalformedHeader = true };
        var provider = CreateProvider(context, options: options);

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Malformed*Id*");
    }

    [Fact]
    public async Task GetCurrentActor_Development_NonStringPermissionValues_FallsBackToDefault()
    {
        var context = CreateHttpContextWithHeader("""{"Id":"user-1","Permissions":[123,true]}""");
        var provider = CreateProvider(context, options: new DevelopmentActorOptions { ThrowOnMalformedHeader = false });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("development");
    }

    #endregion

    #region Round-trip with CreateClientWithActor JSON schema

    [Fact]
    public async Task GetCurrentActor_RoundTrip_FullActorJson_AllPropertiesSurvive()
    {
        var header = CreateActorJson(
            "round-trip-user",
            permissions: ["orders:create", "orders:read", "products:manage-stock"],
            forbiddenPermissions: ["admin:delete"],
            attributes: new Dictionary<string, string>
            {
                ["tid"] = "tenant-abc",
                ["preferred_username"] = "alice@contoso.com"
            });
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("round-trip-user");
        actor.Permissions.Should().BeEquivalentTo(["orders:create", "orders:read", "products:manage-stock"]);
        actor.ForbiddenPermissions.Should().Contain("admin:delete");
        actor.GetAttribute("tid").Should().Be("tenant-abc");
        actor.GetAttribute("preferred_username").Should().Be("alice@contoso.com");
    }

    [Fact]
    public async Task GetCurrentActor_CaseInsensitivePropertyNames_ParsesCorrectly()
    {
        // The header may come from hand-written JSON with different casing
        var header = """{"id":"case-user","permissions":["orders:read"],"forbiddenPermissions":[],"attributes":{}}""";
        var context = CreateHttpContextWithHeader(header);
        var provider = CreateProvider(context);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("case-user");
        actor.Permissions.Should().Contain("orders:read");
    }

    #endregion

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TestApp";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}