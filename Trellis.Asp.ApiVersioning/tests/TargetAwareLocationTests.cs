namespace Trellis.Asp.ApiVersioning.Tests;

using System.Net;
using global::Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Asp;

public sealed class TargetAwareLocationTests
{
    [Theory]
    [InlineData("route", "/target-location/v1/42?api-version=1.0", HttpStatusCode.Created)]
    [InlineData("action", "/target-location/actions/42?api-version=1.0", HttpStatusCode.Created)]
    [InlineData("ambient-action", "/target-location/source/canonical/42?api-version=1.0", HttpStatusCode.Created)]
    [InlineData("controller-value", "/target-location/actions/42?api-version=1.0", HttpStatusCode.Created)]
    [InlineData("area-action", "/target-location/archive/42?api-version=1.0", HttpStatusCode.Created)]
    [InlineData("neutral", "/target-location/neutral/42", HttpStatusCode.Created)]
    [InlineData("unversioned", "/target-location/unversioned/42", HttpStatusCode.Created)]
    [InlineData("transition", "/target-location/v1/42?api-version=1.0", HttpStatusCode.OK)]
    [InlineData("segment", "/target-location/v2.0/42", HttpStatusCode.Created)]
    [InlineData("segment-pin", "/target-location/v1.0/42", HttpStatusCode.Created)]
    [InlineData("mapped", "/target-location/v1.0/only-v1/42", HttpStatusCode.Created)]
    [InlineData("version-first", "/target-location/v1/42?api-version=1.0", HttpStatusCode.Created)]
    [InlineData("repin", "/target-location/v1/42?api-version=1.0", HttpStatusCode.Created)]
    [InlineData("action-after-route", "/target-location/actions/42?api-version=1.0", HttpStatusCode.Created)]
    [InlineData("route-after-action", "/target-location/neutral/42", HttpStatusCode.OK)]
    public async Task WithVersionedRoute_CrossTarget_EmitsFollowableLocation(
        string scenario, string expected, HttpStatusCode status)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        var ct = TestContext.Current.CancellationToken;

        using var response = await client.PostAsync($"/target-location/source/{scenario}?api-version=2.0", null, ct);

        response.StatusCode.Should().Be(status);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should().Be(expected);
        using var followed = await client.GetAsync(response.Headers.Location, ct);
        followed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("invalid-pin", "*not declared*")]
    [InlineData("invalid-segment-pin", "*not declared*")]
    [InlineData("invalid-action-pin", "*not declared*")]
    [InlineData("missing-route", "*no registered endpoint*")]
    [InlineData("missing-action", "*no registered endpoint*")]
    [InlineData("suppressed-route", "*no registered endpoint*")]
    [InlineData("ambiguous-action", "*ambiguous*")]
    public async Task WithVersionedRoute_InvalidDestination_ThrowsInsteadOfEmittingMisleadingLink(
        string scenario, string message)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        var act = () => client.PostAsync(
            $"/target-location/source/{scenario}?api-version=2.0", null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(message);
    }

    [Fact]
    public async Task WithVersionedRoute_NeutralSourceToVersionedTarget_UsesTargetVersion()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        var ct = TestContext.Current.CancellationToken;

        using var response = await client.PostAsync("/target-location/neutral", null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().Be("/target-location/v1/42?api-version=1.0");
        using var followed = await client.GetAsync(response.Headers.Location, ct);
        followed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithVersionedRoute_SharedRouteValues_ClonesBeforeRemovingOrOverridingVersion()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        var ct = TestContext.Current.CancellationToken;

        using var neutral = await client.PostAsync("/target-location/source/shared-neutral?api-version=2.0", null, ct);
        using var versioned = await client.PostAsync("/target-location/source/shared-versioned?api-version=2.0", null, ct);

        neutral.Headers.Location!.OriginalString.Should().Be("/target-location/neutral/42");
        versioned.Headers.Location!.OriginalString.Should().Be("/target-location/v1/42?api-version=1.0");
        TargetLocationSourceController.SharedValues["api-version"].Should().Be("99.0");
    }

    [Theory]
    [InlineData("route", "/application/target-location/v1/42?api-version=1.0")]
    [InlineData("action", "/application/target-location/actions/42?api-version=1.0")]
    public async Task WithVersionedRoute_PathBase_PreservesApplicationPrefix(string scenario, string expected)
    {
        using var host = CreateHost("/application");
        using var client = host.GetTestClient();
        var ct = TestContext.Current.CancellationToken;

        using var response = await client.PostAsync(
            $"/application/target-location/source/{scenario}?api-version=2.0", null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().Be(expected);
        using var followed = await client.GetAsync(response.Headers.Location, ct);
        followed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static IHost CreateHost(string? pathBase = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddTrellisAsp();
                    services.AddControllers().AddApplicationPart(typeof(TargetLocationSourceController).Assembly);
                    services.AddApiVersioning(options =>
                    {
                        options.ApiVersionReader = ApiVersionReader.Combine(
                            new QueryStringApiVersionReader(), new UrlSegmentApiVersionReader());
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    if (pathBase is not null)
                        app.UsePathBase(pathBase);
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapGet("/target-location/unversioned/{id:int}", () => Results.Ok())
                            .WithName("TargetLocation_Unversioned");
                        endpoints.MapGet("/target-location/suppressed/{id:int}", () => Results.Ok())
                            .WithName("TargetLocation_Suppressed")
                            .WithMetadata(new SuppressLinkGenerationMetadata());
                    });
                }))
            .Start();
}

[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("target-location/source")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC test fixture actions.")]
public sealed class TargetLocationSourceController : ControllerBase
{
    internal static RouteValueDictionary SharedValues { get; } = new() { ["id"] = 42, ["api-version"] = "99.0" };

    [HttpGet("canonical/{id:int}")]
    [MapToApiVersion("1.0")]
    public IActionResult Canonical(int id) => Ok(new { id });

    [HttpPost("{scenario}")]
    [MapToApiVersion("2.0")]
    public Microsoft.AspNetCore.Http.IResult Post(string scenario) =>
        Result.Ok(42).ToHttpResponse(options =>
        {
            switch (scenario)
            {
                case "action":
                    options.CreatedAtAction("Get", id => new RouteValueDictionary { ["id"] = id }, "TargetLocationAction")
                        .WithVersionedRoute();
                    break;
                case "ambient-action":
                    options.CreatedAtAction(nameof(Canonical), id => new RouteValueDictionary { ["id"] = id })
                        .WithVersionedRoute();
                    break;
                case "controller-value":
                    options.CreatedAtAction("Get", id => new RouteValueDictionary
                    {
                        ["id"] = id,
                        ["controller"] = "TargetLocationAction"
                    }).WithVersionedRoute();
                    break;
                case "area-action":
                    options.CreatedAtAction("Get", id => new RouteValueDictionary
                    {
                        ["id"] = id,
                        ["area"] = "Archive"
                    }, "TargetLocationArea").WithVersionedRoute();
                    break;
                case "missing-action":
                    options.CreatedAtAction("Missing", id => new RouteValueDictionary { ["id"] = id }, "TargetLocationAction")
                        .WithVersionedRoute();
                    break;
                case "invalid-action-pin":
                    options.CreatedAtAction(nameof(Canonical), id => new RouteValueDictionary { ["id"] = id })
                        .WithVersionedRoute(new ApiVersion(2, 0));
                    break;
                case "ambiguous-action":
                    options.CreatedAtAction("Get", id => new RouteValueDictionary { ["id"] = id }, "TargetLocationAmbiguous")
                        .WithVersionedRoute();
                    break;
                case "transition":
                    options.CreatedAtRoute("TargetLocation_V1", id => id)
                        .WithLocation("TargetLocation_V1", id => id).WithVersionedRoute();
                    break;
                case "action-after-route":
                    options.CreatedAtRoute("TargetLocation_Neutral", id => id)
                        .CreatedAtAction("Get", id => new RouteValueDictionary { ["id"] = id }, "TargetLocationAction")
                        .WithVersionedRoute();
                    break;
                case "route-after-action":
                    options.CreatedAtAction("Get", id => new RouteValueDictionary { ["id"] = id }, "TargetLocationAction")
                        .WithLocation("TargetLocation_Neutral", id => id).WithVersionedRoute();
                    break;
                case "version-first":
                    options.WithVersionedRoute().CreatedAtRoute("TargetLocation_V1", id => id);
                    return;
                case "repin":
                    options.CreatedAtRoute("TargetLocation_V1", id => id)
                        .WithVersionedRoute(new ApiVersion(99, 0))
                        .WithVersionedRoute(new ApiVersion(1, 0));
                    return;
                case "segment-pin":
                    options.CreatedAtRoute("TargetLocation_Segment", id => id)
                        .WithVersionedRoute(new ApiVersion(1, 0));
                    return;
                case "invalid-pin":
                    options.CreatedAtRoute("TargetLocation_V1", id => id)
                        .WithVersionedRoute(new ApiVersion(2, 0));
                    return;
                case "invalid-segment-pin":
                    options.CreatedAtRoute("TargetLocation_Segment", id => id)
                        .WithVersionedRoute(new ApiVersion(3, 0));
                    return;
                case "shared-neutral":
                    options.CreatedAtRoute("TargetLocation_Neutral", _ => SharedValues).WithVersionedRoute();
                    break;
                case "shared-versioned":
                    options.CreatedAtRoute("TargetLocation_V1", _ => SharedValues).WithVersionedRoute();
                    break;
                default:
                    var target = scenario switch
                    {
                        "route" => "TargetLocation_V1",
                        "neutral" => "TargetLocation_Neutral",
                        "unversioned" => "TargetLocation_Unversioned",
                        "segment" => "TargetLocation_Segment",
                        "mapped" => "TargetLocation_Mapped",
                        "missing-route" => "TargetLocation_Missing",
                        "suppressed-route" => "TargetLocation_Suppressed",
                        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
                    };
                    options.CreatedAtRoute(target, id => id).WithVersionedRoute();
                    break;
            }
        });
}

[ApiController]
[ApiVersion("1.0")]
[Route("target-location/v1")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC test fixture actions.")]
public sealed class TargetLocationV1Controller : ControllerBase
{
    [HttpGet("{id:int}", Name = "TargetLocation_V1")]
    public IActionResult Get(int id) => Ok(new { id });
}

[ApiController]
[ApiVersion("1.0")]
[Route("target-location/actions")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC test fixture actions.")]
public sealed class TargetLocationActionController : ControllerBase
{
    [HttpGet("{id:int}")]
    public IActionResult Get(int id) => Ok(new { id });
}

[ApiController]
[ApiVersion("1.0")]
[Area("Archive")]
[Route("target-location/archive")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC test fixture actions.")]
public sealed class TargetLocationAreaController : ControllerBase
{
    [HttpGet("{id:int}")]
    public IActionResult Get(int id) => Ok(new { id });
}

[ApiController]
[ApiVersion("1.0")]
[Route("target-location/ambiguous")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC test fixture actions.")]
public sealed class TargetLocationAmbiguousController : ControllerBase
{
    [HttpGet("a/{id:int}")]
    [HttpGet("b/{id:int}")]
    public IActionResult Get(int id) => Ok(new { id });
}

[ApiController]
[ApiVersionNeutral]
[Route("target-location/neutral")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC test fixture actions.")]
public sealed class TargetLocationNeutralController : ControllerBase
{
    [HttpGet("{id:int}", Name = "TargetLocation_Neutral")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public Microsoft.AspNetCore.Http.IResult Post() =>
        Result.Ok(42).ToHttpResponse(options =>
            options.CreatedAtRoute("TargetLocation_V1", id => id).WithVersionedRoute());
}

[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("target-location/v{release:apiVersion}")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC test fixture actions.")]
public sealed class TargetLocationSegmentController : ControllerBase
{
    [HttpGet("{id:int}", Name = "TargetLocation_Segment")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpGet("only-v1/{id:int}", Name = "TargetLocation_Mapped")]
    [MapToApiVersion("1.0")]
    public IActionResult GetV1(int id) => Ok(new { id });
}
