namespace Trellis.Asp.ApiVersioning.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using global::Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Pins the per-request resolution behaviour of
/// <see cref="HttpResponseOptionsBuilderApiVersioningExtensions.WithVersionedRoute{T}(HttpResponseOptionsBuilder{T})"/>.
/// Each test stands up a minimal ASP.NET Core host with a specific versioning configuration,
/// POSTs to a Created-returning controller action, and asserts the resulting <c>Location</c>
/// header carries (or correctly omits) the <c>api-version</c> route value.
/// </summary>
public sealed class WithVersionedRouteTests
{
    public const string ApiVersionV1 = "2026-11-12";
    public const string ApiVersionV2 = "2026-12-01";

    private static IHost CreateSingleVersionHost()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(SingleVersionController).Assembly);
                    s.AddApiVersioning(o =>
                    {
                        o.ReportApiVersions = true;
                        o.ApiVersionReader = new QueryStringApiVersionReader("api-version");
                        // Lets the no-client-version test reach the controller; the resolver
                        // then falls back to the single declared version.
                        o.AssumeDefaultVersionWhenUnspecified = true;
                        o.DefaultApiVersion = new ApiVersion(new DateOnly(2026, 11, 12));
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static IHost CreateMultiVersionHost(ApiVersion? defaultVersion = null)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(MultiVersionController).Assembly);
                    s.AddApiVersioning(o =>
                    {
                        o.ApiVersionReader = new QueryStringApiVersionReader("api-version");
                        if (defaultVersion is not null)
                        {
                            o.DefaultApiVersion = defaultVersion;
                            o.AssumeDefaultVersionWhenUnspecified = true;
                        }
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static IHost CreateUrlSegmentHost()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(SegmentVersionController).Assembly);
                    s.AddApiVersioning(o => o.ApiVersionReader = new UrlSegmentApiVersionReader()).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static IHost CreateUnversionedHost()
    {
        // No services.AddApiVersioning(...) call. The UnversionedCustomerController carries no
        // [ApiVersion] attribute, so the registered endpoint will have no ApiVersionMetadata —
        // the canonical "host doesn't use API versioning" shape. Scoped to a single controller
        // because other test controllers (SegmentVersionController) carry the `:apiVersion`
        // route constraint, which is only registered when AddApiVersioning() runs.
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers()
                        .ConfigureApplicationPartManager(apm =>
                        {
                            apm.FeatureProviders.Clear();
                            apm.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(UnversionedCustomerController)));
                        });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private sealed class SingleControllerFeatureProvider : Microsoft.AspNetCore.Mvc.Controllers.ControllerFeatureProvider
    {
        private readonly System.Collections.Generic.HashSet<Type> _allowed;
        public SingleControllerFeatureProvider(params Type[] allowed) => _allowed = [.. allowed];
        protected override bool IsController(System.Reflection.TypeInfo typeInfo) => _allowed.Contains(typeInfo.AsType());
    }

    [Fact]
    public async Task Single_declared_version_with_client_request_echoes_version_in_Location()
    {
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/customers?api-version={ApiVersionV1}", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().Contain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task Single_declared_version_with_no_client_request_falls_back_to_declared()
    {
        // Single declared version is unambiguous — the resolver should fall back to it
        // even when the client didn't specify a version.
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        // No api-version query param.
        var resp = await client.PostAsync("/customers", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().Contain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task Multi_version_with_client_request_echoes_requested_version()
    {
        using var host = CreateMultiVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/orders?api-version={ApiVersionV2}", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().Contain($"api-version={ApiVersionV2}");
    }

    [Fact]
    public async Task Multi_version_with_no_client_request_uses_default_when_set()
    {
        using var host = CreateMultiVersionHost(defaultVersion: new ApiVersion(new DateOnly(2026, 11, 12)));
        using var client = host.GetTestClient();

        var resp = await client.PostAsync("/orders", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().Contain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task Explicit_version_overload_pins_regardless_of_client_request()
    {
        using var host = CreateMultiVersionHost();
        using var client = host.GetTestClient();

        // Client requests v2 but the controller pinned the Location to v1 via the explicit overload.
        var resp = await client.PostAsync($"/orders/pinned?api-version={ApiVersionV2}", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().Contain($"api-version={ApiVersionV1}");
        resp.Headers.Location.OriginalString.Should().NotContain($"api-version={ApiVersionV2}");
    }

    [Fact]
    public async Task Single_id_convenience_overload_injects_id_and_version()
    {
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/customers/single-id?api-version={ApiVersionV1}", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        // /customers/{id} with id from idSelector + api-version from the resolver.
        resp.Headers.Location!.OriginalString.Should().Contain("/customers/42");
        resp.Headers.Location.OriginalString.Should().Contain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task ApiVersionNeutral_controller_omits_api_version_from_Location()
    {
        // [ApiVersionNeutral] endpoints must not carry a version in their Location header
        // (they're version-exempt by design — adding ?api-version=... would mislead clients).
        // The resolver short-circuits to null, ApplyRouteValueResolvers writes nothing, and
        // the resulting Location is the bare URI.
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/neutral?api-version={ApiVersionV1}", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().NotContain("api-version");
    }

    [Fact]
    public async Task Explicit_version_overload_on_ApiVersionNeutral_controller_omits_api_version_from_Location()
    {
        // Regression: the explicit-version overload `WithVersionedRoute(ApiVersion)` must honor
        // the same skip rules as the parameterless overload. A pinned version on a
        // [ApiVersionNeutral] endpoint would emit a Location with a version the target endpoint
        // doesn't accept — exactly the bug the type-level skip rules promise won't happen.
        // The action pins ApiVersionV2; the request supplies ApiVersionV1. Either value showing
        // up in the Location indicates the skip rule was bypassed.
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/neutral/pinned?api-version={ApiVersionV1}", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().NotContain("api-version");
        resp.Headers.Location.OriginalString.Should().NotContain(ApiVersionV2);
    }

    [Fact]
    public async Task UrlSegment_versioned_route_omits_api_version_query_from_Location()
    {
        // URL-segment versioning embeds the version in the route template (`v{version:apiVersion}`).
        // The resolver detects the `:apiVersion` constraint and skips injecting api-version into the
        // route-values dictionary — the segment is filled by ambient routing, and a query-string
        // copy would create a redundant/conflicting parameter on the Location URI.
        using var host = CreateUrlSegmentHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/v{ApiVersionV1}/segments", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        // The version is in the path, not the query. The Location should contain the segment but
        // NOT a `?api-version=...` query parameter from the resolver.
        resp.Headers.Location!.OriginalString.Should().Contain($"/v{ApiVersionV1}/segments/");
        resp.Headers.Location.OriginalString.Should().NotContain("?api-version=");
        resp.Headers.Location.OriginalString.Should().NotContain("&api-version=");
    }

    [Fact]
    public async Task Explicit_version_overload_on_url_segment_versioned_route_omits_api_version_query_from_Location()
    {
        // Regression: the explicit-version overload must honor the URL-segment skip rule too.
        // The route template `v{version:apiVersion}/segments/...` already carries the version
        // in its path; a pinned `?api-version=...` query parameter on the Location would create
        // a redundant/conflicting parameter that contradicts the path. The action pins
        // ApiVersionV2 while the request comes in under the ApiVersionV1 segment — either
        // value appearing as a query parameter indicates the skip rule was bypassed.
        using var host = CreateUrlSegmentHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/v{ApiVersionV1}/segments/pinned", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().Contain($"/v{ApiVersionV1}/segments/");
        resp.Headers.Location.OriginalString.Should().NotContain("?api-version=");
        resp.Headers.Location.OriginalString.Should().NotContain("&api-version=");
        resp.Headers.Location.OriginalString.Should().NotContain($"={ApiVersionV2}");
    }

    [Fact]
    public async Task PerRequest_WithVersionedRoute_without_AddApiVersioning_omits_api_version_from_Location()
    {
        // The host never calls services.AddApiVersioning(...) and the controller carries no
        // [ApiVersion] attribute, so the endpoint has no ApiVersionMetadata at all. The
        // per-request WithVersionedRoute() resolver must NOT throw the multi-version
        // unresolvable error (whose advice — configure DefaultApiVersion or use the explicit
        // overload — does not apply when versioning is not in use) and must NOT echo the
        // default-constructed ApiVersioningOptions.DefaultApiVersion (which IOptions returns
        // even without AddApiVersioning, value 1.0). It must emit a clean Location header
        // with no api-version query parameter.
        using var host = CreateUnversionedHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync("/unversioned/customers", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().Contain("/unversioned/customers/");
        resp.Headers.Location.OriginalString.Should().NotContain("api-version");
    }

    [Fact]
    public async Task Explicit_WithVersionedRoute_without_AddApiVersioning_skips_pin_silently()
    {
        // Same unversioned-host setup as above, but the controller chains
        // WithVersionedRoute(ApiVersion) — the EXPLICIT overload. With no ApiVersionMetadata
        // on the target the pinned version has no declared-version set to validate against
        // and emitting it as a query parameter on the Location would be a stale URL artefact
        // (no API-versioning middleware is installed to consume it). The pin must be silently
        // suppressed — same precedent as [ApiVersionNeutral] — and a clean Location emitted.
        using var host = CreateUnversionedHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync("/unversioned/customers/pinned", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location!.OriginalString.Should().Contain("/unversioned/customers/");
        resp.Headers.Location.OriginalString.Should().NotContain("api-version");
    }

    [Fact]
    public async Task WithLocation_after_CreatedAtRoute_clears_created_intent_and_returns_200_OK()
    {
        // Regression for a latent bug: WithLocation overwrites the location configuration but
        // must also reset the Created flag the prior CreatedAtRoute call set. Otherwise the
        // chain CreatedAtRoute(...).WithLocation(...) emits a stale 201 even though
        // WithLocation is the state-transition primitive (200 OK + Location).
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/customers/transition?api-version={ApiVersionV1}", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.Location!.OriginalString.Should().Contain("/customers/42");
        resp.Headers.Location.OriginalString.Should().Contain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task WithLocation_emits_versioned_Location_with_status_200_OK_not_201_Created()
    {
        // WithLocation is the state-transition primitive: it adds a Location header to point
        // at the resource being mutated, but keeps the response's natural status code (200 OK
        // here, since the handler returns Result.Ok). Chaining .WithVersionedRoute() must
        // inject the api-version into the generated Location just like with CreatedAtRoute.
        using var host = CreateMultiVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync($"/orders/99/return?api-version={ApiVersionV2}", JsonContent("{}"), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.Location!.OriginalString.Should().Contain("/orders/99");
        resp.Headers.Location.OriginalString.Should().Contain($"api-version={ApiVersionV2}");
    }

    private static StringContent JsonContent(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");
}

#region Test fixtures

[ApiController]
[ApiVersion(WithVersionedRouteTests.ApiVersionV1)]
[Route("customers")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class SingleVersionController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Customers_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedCustomer> Post() =>
        Result.Ok(new CreatedCustomer(42))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Customers_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedCustomer>();

    [HttpPost("single-id")]
    public ActionResult<CreatedCustomer> PostSingleId() =>
        Result.Ok(new CreatedCustomer(42))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute("Customers_GetById", c => (object)c.Id)
                .WithVersionedRoute())
            .AsActionResult<CreatedCustomer>();

    // Regression: a chain of `.CreatedAtRoute(...).WithLocation(...)` must produce a 200 OK,
    // not a stale 201 from the prior CreatedAtRoute call. WithLocation takes ownership of the
    // location configuration and clears the "this is a Created response" intent.
    [HttpPost("transition")]
    public ActionResult<CreatedCustomer> PostTransition() =>
        Result.Ok(new CreatedCustomer(42))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute("Customers_GetById", c => new RouteValueDictionary { ["id"] = c.Id })
                .WithLocation("Customers_GetById", c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedCustomer>();
}

[ApiController]
[ApiVersion(WithVersionedRouteTests.ApiVersionV1)]
[ApiVersion(WithVersionedRouteTests.ApiVersionV2)]
[Route("orders")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class MultiVersionController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Orders_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedOrder> Post() =>
        Result.Ok(new CreatedOrder(99))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Orders_GetById",
                    o => new RouteValueDictionary { ["id"] = o.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedOrder>();

    [HttpPost("pinned")]
    public ActionResult<CreatedOrder> PostPinned() =>
        Result.Ok(new CreatedOrder(99))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Orders_GetById",
                    o => new RouteValueDictionary { ["id"] = o.Id })
                .WithVersionedRoute(new ApiVersion(new DateOnly(2026, 11, 12))))
            .AsActionResult<CreatedOrder>();

    [HttpPost("{id:int}/return")]
    public ActionResult<CreatedOrder> ReturnOrder(int id) =>
        Result.Ok(new CreatedOrder(id))
            .ToHttpResponse(opts => opts
                .WithLocation(
                    "Orders_GetById",
                    o => new RouteValueDictionary { ["id"] = o.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedOrder>();
}

public sealed record CreatedCustomer(int Id);

public sealed record CreatedOrder(int Id);

[ApiController]
[ApiVersionNeutral]
[Route("neutral")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class NeutralController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Neutral_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedCustomer> Post() =>
        Result.Ok(new CreatedCustomer(7))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Neutral_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedCustomer>();

    [HttpPost("pinned")]
    public ActionResult<CreatedCustomer> PostPinned() =>
        Result.Ok(new CreatedCustomer(7))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Neutral_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1))))
            .AsActionResult<CreatedCustomer>();
}

[ApiController]
[ApiVersion(WithVersionedRouteTests.ApiVersionV1)]
[Route("v{version:apiVersion}/segments")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class SegmentVersionController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Segments_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedCustomer> Post() =>
        Result.Ok(new CreatedCustomer(13))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Segments_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedCustomer>();

    [HttpPost("pinned")]
    public ActionResult<CreatedCustomer> PostPinned() =>
        Result.Ok(new CreatedCustomer(13))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Segments_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1))))
            .AsActionResult<CreatedCustomer>();
}

[ApiController]
[Route("unversioned/customers")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class UnversionedCustomerController : ControllerBase
{
    // No [ApiVersion] attribute and no AddApiVersioning() on the host → the registered
    // endpoint has no ApiVersionMetadata at all. Exercises the unversioned-host path
    // shared by ResolveApiVersion (per-request WithVersionedRoute()) and the explicit
    // WithVersionedRoute(ApiVersion) overload — both of which must skip api-version
    // injection silently in this configuration.

    [HttpGet("{id:int}", Name = "Unversioned_Customers_GetById")]
    public IActionResult Get(int id) => Ok(new { id });

    [HttpPost]
    public ActionResult<CreatedCustomer> Post() =>
        Result.Ok(new CreatedCustomer(101))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Unversioned_Customers_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute())
            .AsActionResult<CreatedCustomer>();

    [HttpPost("pinned")]
    public ActionResult<CreatedCustomer> PostPinned() =>
        Result.Ok(new CreatedCustomer(101))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Unversioned_Customers_GetById",
                    c => new RouteValueDictionary { ["id"] = c.Id })
                .WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1))))
            .AsActionResult<CreatedCustomer>();
}

#endregion