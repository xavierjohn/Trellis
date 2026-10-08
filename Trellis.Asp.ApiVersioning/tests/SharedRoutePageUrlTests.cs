namespace Trellis.Asp.ApiVersioning.Tests;

using System.Net;
using System.Reflection;
using System.Text.Json;
using global::Asp.Versioning;
using global::Asp.Versioning.Conventions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Asp;
using NamedV1Controller = SharedRoutes.v2026_11_12.LegacyWidgetsController;
using NamedV2Controller = SharedRoutes.v2026_12_01.CurrentWidgetsController;
using V1Controller = SharedRoutes.v2026_11_12.WidgetsController;
using V2Controller = SharedRoutes.v2026_12_01.WidgetsController;

public sealed class SharedRoutePageUrlTests
{
    private const string V1 = "2026-11-12";
    private const string V2 = "2026-12-01";
    private const string RouteName = "Shared_Widgets_List";

    [Theory]
    [InlineData(V1, false)]
    [InlineData(V2, false)]
    [InlineData(V1, true)]
    [InlineData(V2, true)]
    public async Task PageUrl_DifferentControllerNames_SharedRoutesProduceFollowableLinks(
        string version, bool reverseOrder)
    {
        using var host = CreateHost(reverseOrder, differentControllerNames: true);
        using var client = host.GetTestClient();
        host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<RouteNameMetadata>()?.RouteName == RouteName)
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName)
            .Should().OnlyHaveUniqueItems();
        (string Path, bool Pin)[] scenarios =
        [
            ("widgets", false), ("widgets", true),
            ("directional", false), ("directional", true),
            ("source", false)
        ];
        foreach (var (path, pin) in scenarios)
        {
            var expected = pin ? version == V1 ? V2 : V1 : version;
            var query = pin ? $"&pin={expected}" : string.Empty;
            using var response = await client.GetAsync(
                $"/shared-route/{path}?api-version={version}{query}", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var next = json.RootElement.GetProperty("next").GetProperty("href").GetString()!;
            next.Should().Contain($"api-version={expected}");
            await AssertFollowedVersion(client, next, expected);
            if (path == "directional")
            {
                var previous = json.RootElement.GetProperty("previous").GetProperty("href").GetString()!;
                await AssertFollowedVersion(client, previous, expected);
            }
        }
    }

    [Theory]
    [InlineData(V1, false, false)]
    [InlineData(V2, false, false)]
    [InlineData(V1, true, false)]
    [InlineData(V2, true, false)]
    [InlineData(V1, false, true)]
    [InlineData(V2, false, true)]
    [InlineData(V1, true, true)]
    [InlineData(V2, true, true)]
    public async Task PageUrl_SharedNamespaceRoute_PreservesVersionInFollowableLinks(
        string version, bool reverseOrder, bool directional)
    {
        using var host = CreateHost(reverseOrder);
        using var client = host.GetTestClient();
        var path = directional ? "directional" : "widgets";
        using var response = await client.GetAsync(
            $"/shared-route/{path}?api-version={version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var next = json.RootElement.GetProperty("next").GetProperty("href").GetString()!;
        next.Should().Contain($"api-version={version}").And.Contain("limit=2");
        await AssertFollowedVersion(client, next, version);

        if (directional)
        {
            var previous = json.RootElement.GetProperty("previous").GetProperty("href").GetString()!;
            next.Should().Contain("after=next-token").And.NotContain("before=");
            previous.Should().Contain("before=previous-token").And.NotContain("after=");
            previous.Should().Contain($"api-version={version}").And.Contain("limit=2");
            response.Headers.GetValues("Link").Single().Should().Be(
                $"<{next}>; rel=\"next\", <{previous}>; rel=\"prev\"");
            await AssertFollowedVersion(client, previous, version);
        }
    }

    [Theory]
    [InlineData(V1, V2, false, false)]
    [InlineData(V2, V1, false, false)]
    [InlineData(V1, V2, true, false)]
    [InlineData(V2, V1, true, false)]
    [InlineData(V1, V2, false, true)]
    [InlineData(V2, V1, false, true)]
    [InlineData(V1, V2, true, true)]
    [InlineData(V2, V1, true, true)]
    public async Task PageUrl_SharedNamespaceRoute_PinSelectsDestinationVersion(
        string requested, string pinned, bool reverseOrder, bool directional)
    {
        using var host = CreateHost(reverseOrder);
        using var client = host.GetTestClient();
        var path = directional ? "directional" : "widgets";
        using var response = await client.GetAsync(
            $"/shared-route/{path}?api-version={requested}&pin={pinned}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var next = json.RootElement.GetProperty("next").GetProperty("href").GetString()!;
        next.Should().Contain($"api-version={pinned}");
        await AssertFollowedVersion(client, next, pinned);
        if (directional)
        {
            var previous = json.RootElement.GetProperty("previous").GetProperty("href").GetString()!;
            previous.Should().Contain($"api-version={pinned}");
            await AssertFollowedVersion(client, previous, pinned);
        }
    }

    [Theory]
    [InlineData(V1, false)]
    [InlineData(V2, false)]
    [InlineData(V1, true)]
    [InlineData(V2, true)]
    public async Task PageUrl_SharedCrossRoute_SelectsRequestedDestination(string version, bool reverseOrder)
    {
        using var host = CreateHost(reverseOrder);
        using var client = host.GetTestClient();
        using var response = await client.GetAsync(
            $"/shared-route/source?api-version={version}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var next = json.RootElement.GetProperty("next").GetProperty("href").GetString()!;
        next.Should().Contain("/shared-route/widgets?").And.Contain($"api-version={version}");
        await AssertFollowedVersion(client, next, version);
    }

    [Theory]
    [InlineData(V1, false)]
    [InlineData(V2, false)]
    [InlineData(V1, true)]
    [InlineData(V2, true)]
    public async Task PageUrl_SharedNamespaceRoute_HeaderVersionProducesFollowableQueryLink(string version, bool reverseOrder)
    {
        using var host = CreateHost(reverseOrder);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/shared-route/widgets");
        request.Headers.Add("X-Api-Version", version);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var next = json.RootElement.GetProperty("next").GetProperty("href").GetString()!;
        next.Should().Contain($"api-version={version}");
        await AssertFollowedVersion(client, next, version);
    }

    [Theory]
    [InlineData(V1, false, false)]
    [InlineData(V2, false, false)]
    [InlineData(V1, true, false)]
    [InlineData(V2, true, false)]
    [InlineData(V1, false, true)]
    [InlineData(V2, false, true)]
    [InlineData(V1, true, true)]
    [InlineData(V2, true, true)]
    public async Task PageUrl_SharedSegmentRoute_AmbientOrConsumerVersionProducesFollowableLinks(
        string version, bool reverseOrder, bool overrideSegment)
    {
        using var host = CreateHost(reverseOrder);
        using var client = host.GetTestClient();
        var requested = overrideSegment ? version == V1 ? V2 : V1 : version;
        var query = overrideSegment ? $"?destination={version}" : string.Empty;
        using var response = await client.GetAsync(
            $"/shared-route/v{requested}/source{query}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var next = json.RootElement.GetProperty("next").GetProperty("href").GetString()!;
        next.Should().Contain($"/shared-route/v{version}/widgets?").And.NotContain("api-version=");
        await AssertFollowedVersion(client, next, version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_SharedSelfRoute_UsesActiveEndpointSkipRules(bool wrappedEndpoint)
    {
        var unversioned = new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("unit/widgets"), 0,
            new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), "Unversioned target");
        using var services = CreateServices(BuildEndpoint("unit/widgets", V1), unversioned);
        var context = CreateContext(services, V1);
        context.SetEndpoint(wrappedEndpoint
            ? new Endpoint(unversioned.RequestDelegate, unversioned.Metadata, unversioned.DisplayName)
            : unversioned);
        var builder = context.PageUrl(RouteName, (_, _) => new());

        builder(new Cursor("next"), 2).Should().Be("https://example.com/unit/widgets");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_SharedCrossRoute_UsesActionMappingsRatherThanControllerDeclarations(bool pinVersion)
    {
        using var services = CreateServices(
            BuildEndpoint("unit/widgets", V1),
            BuildEndpoint("unit/widgets", V2));
        var context = CreateContext(services, V2);
        var builder = pinVersion
            ? context.PageUrl(RouteName, ApiVersionParser.Default.Parse(V2), (_, _) => new())
            : context.PageUrl(RouteName, (_, _) => new());

        builder(new Cursor("next"), 2).Should().Contain($"/unit/widgets?api-version={V2}");
    }

    [Fact]
    public void PageUrl_SharedCrossRoute_ConfiguredDefaultSelectsDestination()
    {
        using var services = CreateServices(
            BuildEndpoint("unit/widgets", V1),
            BuildEndpoint("unit/widgets", V2));
        var context = CreateContext(services, null);
        var builder = context.PageUrl(RouteName, (_, _) => new());

        builder(new Cursor("next"), 2).Should().Contain($"/unit/widgets?api-version={V2}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_SharedCrossRoute_SuppressedDestinationIsIgnored(bool pinVersion)
    {
        using var services = CreateServices(
            BuildEndpoint("unit/suppressed", V1, suppress: true),
            BuildEndpoint("unit/available", V2));
        var context = CreateContext(services, V2);
        var builder = pinVersion
            ? context.PageUrl(RouteName, ApiVersionParser.Default.Parse(V2), (_, _) => new())
            : context.PageUrl(RouteName, (_, _) => new());

        builder(new Cursor("next"), 2).Should().Contain($"/unit/available?api-version={V2}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_SharedCrossRoute_MultipleMappedDestinationsThrows(bool pinVersion)
    {
        using var services = CreateServices(
            BuildEndpoint("unit/first", V1),
            BuildEndpoint("unit/first", V1));
        var context = CreateContext(services, V1);
        var builder = pinVersion
            ? context.PageUrl(RouteName, ApiVersionParser.Default.Parse(V1), (_, _) => new())
            : context.PageUrl(RouteName, (_, _) => new());
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*ambiguous*");
    }

    [Fact]
    public void PageUrl_SharedName_DifferentTemplatesThrowsRatherThanRenderingAnotherDestination()
    {
        using var services = CreateServices(
            BuildEndpoint("unit/v1", V1),
            BuildEndpoint("unit/v2", V2));
        var context = CreateContext(services, V2);
        var builder = context.PageUrl(RouteName, (_, _) => new());
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*ambiguous*");
    }

    [Fact]
    public void PageUrl_SharedCrossRoute_ConsumerVersionSelectsDestinationWithoutMutation()
    {
        using var services = CreateServices(
            BuildEndpoint("unit/widgets", V1),
            BuildEndpoint("unit/widgets", V2));
        var context = CreateContext(services, V1);
        var values = new RouteValueDictionary { ["api-version"] = V2 };
        var builder = context.PageUrl(RouteName, (_, _) => values);

        builder(new Cursor("next"), 2).Should().Contain($"api-version={V2}");
        values.Should().ContainSingle().Which.Value.Should().Be(V2);
    }

    [Fact]
    public void PageUrl_SharedCrossRoute_NoMappedRequestOrDefaultThrows()
    {
        using var services = CreateServices(
            BuildEndpoint("unit/widgets", V1),
            BuildEndpoint("unit/widgets", V1));
        var context = CreateContext(services, "2027-01-01");
        var builder = context.PageUrl(RouteName, (_, _) => new());
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*ambiguous*");
    }

    private static async Task AssertFollowedVersion(HttpClient client, string href, string expectedVersion)
    {
        using var response = await client.GetAsync(href, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("items")[0].GetProperty("version").GetString().Should().Be(expectedVersion);
    }

    private static IHost CreateHost(bool reverseOrder, bool differentControllerNames = false)
    {
        Type[] controllers = differentControllerNames
            ? [typeof(NamedV1Controller), typeof(NamedV2Controller)]
            : [typeof(V1Controller), typeof(V2Controller)];
        if (reverseOrder)
            Array.Reverse(controllers);
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddTrellisAspWithScalarValidation(options => options.UseVersionedPageUrls());
                    var mvc = services.AddControllers();
                    services.AddApiVersioning(options =>
                    {
                        options.ApiVersionReader = ApiVersionReader.Combine(
                            new QueryStringApiVersionReader("api-version"),
                            new HeaderApiVersionReader("X-Api-Version"),
                            new UrlSegmentApiVersionReader());
                        options.DefaultApiVersion = ApiVersionParser.Default.Parse(V2);
                    }).AddMvc(options => options.Conventions.Add(new VersionByNamespaceConvention()));
                    mvc.ConfigureApplicationPartManager(manager =>
                    {
                        manager.FeatureProviders.Clear();
                        manager.FeatureProviders.Add(new OrderedControllerFeatureProvider(controllers));
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).Start();

        host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<RouteNameMetadata>()?.RouteName == RouteName)
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerTypeInfo.AsType())
            .Should().Equal(controllers);
        return host;
    }

    private static ServiceProvider CreateServices(params Endpoint[] endpoints)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddTrellisAsp(options => options.UseVersionedPageUrls());
        services.Configure<ApiVersioningOptions>(options => options.DefaultApiVersion = ApiVersionParser.Default.Parse(V2));
        services.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(endpoints));
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, string? requested)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");
        context.Features.Set<IApiVersioningFeature>(
            new ApiVersioningFeature(context) { RequestedApiVersion = requested is null ? null : ApiVersionParser.Default.Parse(requested) });
        return context;
    }

    private static RouteEndpoint BuildEndpoint(string pattern, string mappedVersion, bool suppress = false)
    {
        var versions = new[] { ApiVersionParser.Default.Parse(V1), ApiVersionParser.Default.Parse(V2) };
        var controllerModel = new ApiVersionModel(versions, versions, [], [], []);
        var mapped = ApiVersionParser.Default.Parse(mappedVersion);
        var actionModel = new ApiVersionModel([mapped], [mapped], [], [], []);
        List<object> metadata = [
            new RouteNameMetadata(RouteName),
            new ApiVersionMetadata(controllerModel, actionModel)
        ];
        if (suppress)
            metadata.Add(new SuppressLinkGenerationMetadata());
        return new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0,
            new EndpointMetadataCollection(metadata), pattern);
    }

    private sealed class OrderedControllerFeatureProvider(Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers)
                feature.Controllers.Add(controller.GetTypeInfo());
        }
    }
}

[ApiController]
[Route("shared-route")]
public abstract class SharedRoutePagedControllerBase : ControllerBase
{
    protected abstract string Version { get; }

    [HttpGet("widgets", Name = "Shared_Widgets_List")]
    public Microsoft.AspNetCore.Http.IResult List([FromQuery] string? cursor, [FromQuery] string? pin)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        Func<Cursor, int, RouteValueDictionary> values = (c, limit) => new() { ["cursor"] = c.Token, ["limit"] = limit };
        var builder = pin is null
            ? HttpContext.PageUrl("Shared_Widgets_List", values)
            : HttpContext.PageUrl("Shared_Widgets_List", ApiVersionParser.Default.Parse(pin), values);
        return Result.Ok(page).ToHttpResponse(builder, id => new { Id = id, Version });
    }

    [HttpGet("directional", Name = "Shared_Widgets_Directional")]
    public Microsoft.AspNetCore.Http.IResult Directional(
        [FromQuery] string? after, [FromQuery] string? before, [FromQuery] string? pin)
    {
        var page = after is not null || before is not null
            ? new Page<string>([after ?? before!], null, null, 2, 2)
            : new Page<string>(["w1", "w2"], new Cursor("next-token"), new Cursor("previous-token"), 2, 2);
        Func<Cursor, PageDirection, int, RouteValueDictionary> values = (cursor, direction, limit) => new()
        {
            [direction == PageDirection.Next ? "after" : "before"] = cursor.Token,
            ["limit"] = limit
        };
        var builder = pin is null
            ? HttpContext.PageUrl("Shared_Widgets_Directional", values)
            : HttpContext.PageUrl("Shared_Widgets_Directional", ApiVersionParser.Default.Parse(pin), values);
        return Result.Ok(page).ToHttpResponse(builder, id => new { Id = id, Version });
    }

    [HttpGet("source", Name = "Shared_Widgets_Source")]
    public Microsoft.AspNetCore.Http.IResult Source()
    {
        var (page, _) = PagedFixtures.NextPage(null);
        return Result.Ok(page).ToHttpResponse(
            HttpContext.PageUrl("Shared_Widgets_List", (cursor, limit) => new() { ["cursor"] = cursor.Token, ["limit"] = limit }),
            id => new { Id = id, Version });
    }

    [HttpGet("v{version:apiVersion}/widgets", Name = "Shared_Widgets_Segmented")]
    public Microsoft.AspNetCore.Http.IResult Segmented([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        return Result.Ok(page).ToHttpResponse(
            HttpContext.PageUrl("Shared_Widgets_Segmented", (c, limit) => new() { ["cursor"] = c.Token, ["limit"] = limit }),
            id => new { Id = id, Version });
    }

    [HttpGet("v{version:apiVersion}/source", Name = "Shared_Widgets_SegmentedSource")]
    public Microsoft.AspNetCore.Http.IResult SegmentedSource([FromQuery] string? destination)
    {
        var (page, _) = PagedFixtures.NextPage(null);
        return Result.Ok(page).ToHttpResponse(
            HttpContext.PageUrl("Shared_Widgets_Segmented", (cursor, limit) =>
            {
                var values = new RouteValueDictionary { ["cursor"] = cursor.Token, ["limit"] = limit };
                if (destination is not null)
                    values["version"] = destination;
                return values;
            }),
            id => new { Id = id, Version });
    }
}