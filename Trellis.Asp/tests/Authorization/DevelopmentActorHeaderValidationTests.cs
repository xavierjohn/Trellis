namespace Trellis.Asp.Authorization.Tests;

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

public class DevelopmentActorHeaderValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\u00A0")]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"Id":null}""")]
    [InlineData("""{"Id":123}""")]
    [InlineData("""{"Id":""}""")]
    [InlineData("""{"Id":" "}""")]
    [InlineData("""{"Id":"\uD800"}""")]
    [InlineData("""{"Id":"user","Permissions":"read"}""")]
    [InlineData("""{"Id":"user","Permissions":null}""")]
    [InlineData("""{"Id":"user","Permissions":[123]}""")]
    [InlineData("""{"Id":"user","Permissions":[null]}""")]
    [InlineData("""{"Id":"user","Permissions":["\uD800"]}""")]
    [InlineData("""{"Id":"user","ForbiddenPermissions":{}}""")]
    [InlineData("""{"Id":"user","ForbiddenPermissions":null}""")]
    [InlineData("""{"Id":"user","ForbiddenPermissions":[true]}""")]
    [InlineData("""{"Id":"user","Attributes":[]}""")]
    [InlineData("""{"Id":"user","Attributes":null}""")]
    [InlineData("""{"Id":"user","Attributes":{"tid":123}}""")]
    [InlineData("""{"Id":"user","Attributes":{"tid":null}}""")]
    [InlineData("""{"Id":"user","Attributes":{"tid":"\uD800"}}""")]
    [InlineData("""{"Id":"user","Extra":"\uD800"}""")]
    [InlineData("""{"Id":"user","Extra":{"payload":["\uD800"]}}""")]
    [InlineData("""{"Id":"user","Extra":[{"\uD800":null}]}""")]
    [InlineData("""{"Id":"user","Id":"other"}""")]
    [InlineData("""{"Id":"user","id":"other"}""")]
    [InlineData("""{"Id":"user","Attributes":{"tid":"one","tid":"two"}}""")]
    public async Task UseDevelopmentActorHeaderValidation_Strict_InvalidSuppliedHeader_ReturnsBadRequestBeforeActorResolution(
        string header)
    {
        var headerObserved = false;
        var actorResolutionReached = false;
        await using var app = await CreateAppAsync(
            observeRequest: context => headerObserved = context.Request.Headers.ContainsKey("X-Test-Actor"),
            onResolveActor: () => actorResolutionReached = true);

        var response = await SendAsync(app, "/actor", header);

        headerObserved.Should().BeTrue("the server must actually receive the supplied header, including an empty value");
        actorResolutionReached.Should().BeFalse();
        response.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.Response.ContentType.Should().StartWith("application/problem+json");
        using var problem = await JsonDocument.ParseAsync(
            response.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var root = problem.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("code").GetString().Should().Be("development-actor.header.invalid");
        root.GetProperty("kind").GetString().Should().Be("bad-request");
        root.GetProperty("instance").GetString().Should().Be("/actor");
        root.GetProperty("errors").GetProperty("X-Test-Actor").GetArrayLength().Should().Be(1);
        root.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("", """{"Id":"user"}""")]
    [InlineData("""{"Id":"user"}""", """{"Id":"other"}""")]
    public async Task UseDevelopmentActorHeaderValidation_Strict_MultipleHeaderValues_RejectsRatherThanSelectingFirst(
        string first, string second)
    {
        var observedCount = 0;
        var actorResolutionReached = false;
        await using var app = await CreateAppAsync(
            observeRequest: context => observedCount = context.Request.Headers["X-Test-Actor"].Count,
            onResolveActor: () => actorResolutionReached = true);

        var response = await SendAsync(app, "/actor", first, second);

        observedCount.Should().Be(2);
        response.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        actorResolutionReached.Should().BeFalse();
    }

    [Theory]
    [InlineData("/actor", false)]
    [InlineData("/identity", false)]
    [InlineData("/actor", true)]
    [InlineData("/identity", true)]
    public async Task UseDevelopmentActorHeaderValidation_Strict_MissingHeader_PreservesConfiguredFallback(
        string path, bool caching)
    {
        await using var app = await CreateAppAsync(caching: caching);

        var response = await SendAsync(app, path);

        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var reader = new StreamReader(response.Response.Body);
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be("fallback");
    }

    [Theory]
    [InlineData("/actor", false)]
    [InlineData("/identity", false)]
    [InlineData("/actor", true)]
    [InlineData("/identity", true)]
    public async Task UseDevelopmentActorHeaderValidation_Strict_ValidHeader_PreservesCanonicalIdentity(
        string path, bool caching)
    {
        await using var app = await CreateAppAsync(caching: caching);
        const string header = """
            {"iD":" user ","pErMiSsIoNs":["read"],"ForbiddenPermissions":["write"],"Attributes":{"tid":"tenant"},"extra":null}
            """;

        var response = await SendAsync(app, path, header);

        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var reader = new StreamReader(response.Response.Body);
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be("user");
    }

    [Theory]
    [InlineData("", "fallback")]
    [InlineData("""{"Id":"legacy","Permissions":123,"Attributes":[]}""", "legacy")]
    public async Task UseDevelopmentActorHeaderValidation_Disabled_EmptyOrLegacyShape_PreservesExistingProviderBehavior(
        string header, string expectedId)
    {
        await using var app = await CreateAppAsync(strict: false);

        var response = await SendAsync(app, "/actor", header);

        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var reader = new StreamReader(response.Response.Body);
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be(expectedId);
    }

    [Fact]
    public async Task UseDevelopmentActorHeaderValidation_Strict_LenientProvider_StillRejectsInvalidSuppliedHeader()
    {
        await using var app = await CreateAppAsync(throwOnMalformed: false);

        var response = await SendAsync(app, "/actor", "not-json");

        response.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("UAT")]
    public async Task UseDevelopmentActorHeaderValidation_Strict_NonDevelopment_ThrowsBeforeServingRequests(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        await using var app = builder.Build();

        Action act = () => app.UseDevelopmentActorHeaderValidation();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Development*");
    }

    [Theory]
    [InlineData("""{"Id":"minimal"}""")]
    [InlineData("""{"Id":"minimal","Permissions":[],"ForbiddenPermissions":[],"Attributes":{}}""")]
    [InlineData("""{"Id":"minimal","Attributes":{"tid":"","TID":"separate"}}""")]
    [InlineData("""{"Id":"minimal","Extra":{"\uD83D\uDE00":[null,{"text":"\uD83D\uDE00"}]}}""")]
    public async Task UseDevelopmentActorHeaderValidation_Strict_MinimalOrEmptyCollections_AcceptsHeader(string header)
    {
        await using var app = await CreateAppAsync();

        var response = await SendAsync(app, "/actor", header);

        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var reader = new StreamReader(response.Response.Body);
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be("minimal");
    }

    [Fact]
    public async Task UseDevelopmentActorHeaderValidation_Strict_XmlAccept_PreservesJsonAndProblemCustomization()
    {
        var customizations = 0;
        await using var app = await CreateAppAsync(
            observeRequest: context => context.Request.PathBase = "/dev app",
            customizeProblem: context =>
            {
                customizations++;
                context.ProblemDetails.Extensions["application"] = "customized";
            });
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/actor?attempt=1");
        request.Headers.Accept.ParseAdd("application/xml");
        request.Headers.TryAddWithoutValidation(
            "X-Test-Actor", """{"Id":"private-value","Permissions":[false]}""").Should().BeTrue();

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain("private-value");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("application").GetString().Should().Be("customized");
        problem.RootElement.GetProperty("instance").GetString().Should().Be("/dev%20app/actor");
        customizations.Should().Be(1);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("en", null, "en")]
    [InlineData("en", "fr", "fr")]
    public async Task UseDevelopmentActorHeaderValidation_Strict_ProblemContentLanguage_PreservesConfiguredOrExistingHeader(
        string? configuredLanguage, string? existingLanguage, string? expectedLanguage)
    {
        await using var app = await CreateAppAsync(
            configureServices: services => services.AddTrellisAsp(o => o.ProblemContentLanguage = configuredLanguage),
            observeRequest: context =>
            {
                if (existingLanguage is not null)
                    context.Response.Headers.ContentLanguage = existingLanguage;
            });

        var response = await SendAsync(app, "/actor", "not-json");

        response.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.Response.Headers.ContentLanguage.ToString().Should().Be(expectedLanguage ?? string.Empty);
        response.Response.Headers.Vary.ToString().Should().NotContain("Accept-Language");
    }

    private static async Task<WebApplication> CreateAppAsync(
        bool strict = true,
        bool throwOnMalformed = true,
        bool caching = false,
        Action<HttpContext>? observeRequest = null,
        Action? onResolveActor = null,
        Action<ProblemDetailsContext>? customizeProblem = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddDevelopmentActorProvider(options =>
        {
            options.DefaultActorId = "fallback";
            options.DefaultPermissions = new HashSet<string> { "default:permission" };
            options.ThrowOnMalformedHeader = throwOnMalformed;
        });
        if (caching)
            builder.Services.AddCachingActorProvider<DevelopmentActorProvider>();
        builder.Services.AddTrellisProblemDetails();
        if (customizeProblem is not null)
            builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = customizeProblem);
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            observeRequest?.Invoke(context);
            await next(context);
        });
        if (strict)
            app.UseDevelopmentActorHeaderValidation();
        app.MapGet("/actor", async (IActorProvider provider, CancellationToken token) =>
        {
            onResolveActor?.Invoke();
            var actor = (await provider.GetCurrentActorAsync(token)).GetValueOrThrow();
            return Results.Text(actor.Id.Value);
        });
        app.MapGet("/identity", async (IActorIdentityProvider provider, CancellationToken token) =>
        {
            onResolveActor?.Invoke();
            var id = (await provider.GetCurrentActorIdAsync(token)).GetValueOrThrow();
            return Results.Text(id.Value);
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static Task<HttpContext> SendAsync(WebApplication app, string path, params string[] headerValues) =>
        app.GetTestServer().SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = path;
            if (headerValues.Length > 0)
                context.Request.Headers["X-Test-Actor"] = new StringValues(headerValues);
        }, TestContext.Current.CancellationToken);
}