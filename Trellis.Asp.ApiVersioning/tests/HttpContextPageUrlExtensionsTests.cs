namespace Trellis.Asp.ApiVersioning.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using global::Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis;
using Trellis.Asp;
using HttpResult = Microsoft.AspNetCore.Http.IResult;

/// <summary>
/// Pins the per-request behaviour of
/// <see cref="HttpContextPageUrlExtensions.PageUrl(HttpContext, string, Func{Cursor, int, RouteValueDictionary})"/>
/// and the explicit-version overload. Each test stands up a minimal ASP.NET Core host with a
/// specific versioning configuration, hits a paginated controller action that consumes
/// <c>HttpContext.PageUrl(...)</c> via the <c>nextUrlBuilder</c> parameter of
/// <c>ToHttpResponseAsync</c>, and asserts the resulting <c>Link</c> header carries (or
/// correctly omits) the <c>api-version</c> route value.
/// </summary>
public sealed class HttpContextPageUrlExtensionsTests
{
    private const string ApiVersionV1 = "2026-11-12";
    private const string ApiVersionV2 = "2026-12-01";

    #region Null / argument guards (do not need a host)

    [Fact]
    public void PageUrl_null_httpContext_throws()
    {
        HttpContext? context = null;
        Action act = () => context!.PageUrl("X", (_, _) => new RouteValueDictionary());
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("httpContext");
    }

    [Fact]
    public void PageUrl_null_routeName_throws()
    {
        var context = new DefaultHttpContext();
        Action act = () => context.PageUrl(null!, (_, _) => new RouteValueDictionary());
        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("routeName");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PageUrl_blank_routeName_throws(string routeName)
    {
        var context = new DefaultHttpContext();
        Action act = () => context.PageUrl(routeName, (_, _) => new RouteValueDictionary());
        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("routeName");
    }

    [Fact]
    public void PageUrl_null_routeValues_throws()
    {
        var context = new DefaultHttpContext();
        Action act = () => context.PageUrl("X", null!);
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("routeValues");
    }

    [Fact]
    public void PageUrl_explicit_null_version_throws()
    {
        var context = new DefaultHttpContext();
        Action act = () => context.PageUrl("X", (ApiVersion)null!, (_, _) => new RouteValueDictionary());
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("version");
    }

    #endregion

    #region Implicit version — query/header versioning

    [Fact]
    public async Task Per_request_RequestedApiVersion_is_echoed_into_next_url()
    {
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync($"/widgets?api-version={ApiVersionV1}", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page.Should().NotBeNull();
        page!.Next.Should().NotBeNull();
        page.Next!.Href.Should().Contain($"api-version={ApiVersionV1}");
        page.Next.Href.Should().Contain("cursor=cur-2");
        page.Next.Href.Should().Contain("limit=2");

        AssertLinkHeader(resp, page.Next.Href);
    }

    [Fact]
    public async Task Single_declared_version_with_no_client_request_falls_back_to_declared()
    {
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/widgets", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task Multi_version_with_default_falls_back_to_DefaultApiVersion()
    {
        using var host = CreateMultiVersionHost(defaultVersion: new ApiVersion(new DateOnly(2026, 11, 12)));
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/multi/widgets", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task Multi_version_with_no_default_and_no_client_version_returns_400_from_versioning_middleware()
    {
        // Asp.Versioning's request pipeline intercepts before the controller can be selected:
        // multi-version endpoint + no DefaultApiVersion + no client api-version = 400 from
        // ErrorObjectVersioningPolicy. PageUrl never gets a chance to run. The test pins this
        // behaviour so a future Asp.Versioning change that lets such a request through (and
        // therefore reaches PageUrl's fail-loud path) is caught and the corresponding PageUrl
        // throw-vs-pick decision can be re-evaluated.
        using var host = CreateMultiVersionHost(defaultVersion: null);
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/multi/widgets", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Implicit version — neutral & url-segment (skipped)

    [Fact]
    public async Task Version_neutral_endpoint_skips_api_version_injection()
    {
        using var host = CreateNeutralHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/neutral/widgets", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().NotContain("api-version=");
    }

    [Fact]
    public async Task UrlSegment_routed_target_skips_query_injection_and_walks_via_ambient_segment()
    {
        // Consumer-supplied dict has only cursor/limit. The version segment is filled from
        // ambient route data; no `?api-version=` query string is appended.
        using var host = CreateUrlSegmentHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync($"/v{ApiVersionV1}/segments/widgets", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().NotContain("api-version=");
        page.Next.Href.Should().Contain($"/v{ApiVersionV1}/segments/widgets");
        page.Next.Href.Should().Contain("cursor=cur-2");
    }

    #endregion

    #region Explicit version

    [Fact]
    public async Task Explicit_version_overrides_per_request_version()
    {
        using var host = CreateMultiVersionHost(defaultVersion: null);
        using var client = host.GetTestClient();

        // Client says V1, controller pins V2 explicitly → emitted URL must carry V2.
        var resp = await client.GetAsync($"/multi/widgets/pinned?api-version={ApiVersionV1}", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain($"api-version={ApiVersionV2}");
        page.Next.Href.Should().NotContain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task Explicit_version_still_skipped_on_neutral_endpoint()
    {
        using var host = CreateNeutralHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/neutral/widgets/pinned", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().NotContain("api-version=");
    }

    [Fact]
    public async Task Explicit_version_on_url_segment_target_throws_with_guidance()
    {
        // The skip rules of WithVersionedRoute deliberately suppress api-version injection on
        // URL-segment targets to avoid duplicating the segment as a query parameter. Carrying
        // that into PageUrl's explicit overload would silently use the AMBIENT segment value
        // (current request's version) and ignore the pinned version — directly contradicting
        // the caller's request. Fail loudly with actionable guidance instead.
        using var host = CreateUrlSegmentHost();
        using var client = host.GetTestClient();

        var ct = TestContext.Current.CancellationToken;
        Func<Task> act = () => client.GetAsync($"/v{ApiVersionV1}/segments/widgets/explicit-pin", ct);
        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message
            .Should().Contain("URL-segment")
            .And.Contain("per-request overload")
            .And.Contain("PageUrl(routeName, routeValues)");
    }

    [Fact]
    public async Task Explicit_pin_of_version_not_declared_by_target_throws_with_guidance()
    {
        // Bug-regression: the explicit-pin overload used to inject the pinned version without
        // validating it against the target endpoint's declared versions. Pinning v2 on the
        // v1-only "Widgets_List" target would generate a URL the target rejects when followed
        // (e.g., 400 from the versioning middleware). The per-request overload performs this
        // validation via ResolveApiVersion's TargetDeclaresVersion check; the explicit overload
        // must too. Surface the misconfiguration at emit time rather than letting clients chase
        // a broken Location header.
        using var host = CreateMultiVersionHost(defaultVersion: new ApiVersion(new DateOnly(2026, 11, 12)));
        using var client = host.GetTestClient();

        var ct = TestContext.Current.CancellationToken;
        Func<Task> act = () => client.GetAsync($"/multi/widgets/pin-v2-on-v1-only?api-version={ApiVersionV1}", ct);
        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message
            .Should().Contain("not declared")
            .And.Contain($"api-version='{ApiVersionV2}'")
            .And.Contain("Widgets_List");
    }

    [Fact]
    public async Task Explicit_pin_overrides_consumer_supplied_api_version()
    {
        // Bug-regression: the explicit-pin overload used to honor a consumer-supplied
        // api-version in the callback dictionary, silently bypassing both the pin AND
        // its declared-version validation. The pinned overload must always win — callers
        // who need per-call version selection should use the per-request overload, which
        // documents consumer-supplied api-version as an explicit escape hatch. Pinning
        // here is V2; the callback inserts V1; the emitted URL must carry V2.
        using var host = CreateMultiVersionHost(defaultVersion: null);
        using var client = host.GetTestClient();

        var resp = await client.GetAsync($"/multi/widgets/pin-overrides-consumer-supplied?api-version={ApiVersionV1}", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain($"api-version={ApiVersionV2}");
        page.Next.Href.Should().NotContain($"api-version={ApiVersionV1}");
    }

    #endregion

    #region Edge cases

    [Fact]
    public async Task Consumer_supplied_api_version_is_preserved_not_overwritten()
    {
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        // Endpoint's PageUrl callback supplies api-version=2099-01-01; per-request resolver
        // would otherwise inject ApiVersionV1. Consumer wins.
        var resp = await client.GetAsync($"/widgets/consumer-supplied?api-version={ApiVersionV1}", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain("api-version=2099-01-01");
        page.Next.Href.Should().NotContain($"api-version={ApiVersionV1}");
    }

    [Fact]
    public async Task Unknown_route_name_throws_invalid_operation()
    {
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        // TestServer rethrows unhandled controller exceptions on the client side because no
        // ExceptionHandler middleware is installed. We assert the exception type and message
        // directly rather than relying on a 500-response shape.
        var ct = TestContext.Current.CancellationToken;
        Func<Task> act = () => client.GetAsync($"/widgets/unknown-route?api-version={ApiVersionV1}", ct);
        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.WithMessage("*Widgets_RouteDoesNotExist*");
    }

    [Fact]
    public async Task Cursor_token_with_special_characters_is_url_encoded()
    {
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync($"/widgets/funky-cursor?api-version={ApiVersionV1}", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);

        // Raw token is "a+b/c d=e&f"; URL-encoded portion must escape +, /, space, =, &.
        page!.Next!.Href.Should().NotContain("cursor=a+b/c d=e&f");
        page.Next.Href.Should().Contain("cursor=");
        page.Next.Href.Should().Contain("%2B"); // +
        page.Next.Href.Should().Contain("%2F"); // /
        page.Next.Href.Should().Contain("%20"); // space
        page.Next.Href.Should().Contain("%3D"); // =
        page.Next.Href.Should().Contain("%26"); // &

        // Round-trip: server can decode the cursor verbatim from the next URL.
        var next = new Uri(page.Next.Href);
        var followResp = await client.GetAsync(next.PathAndQuery, TestContext.Current.CancellationToken);
        followResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var followBody = await followResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        followBody.Should().Contain("a+b/c d=e&f");
    }

    [Fact]
    public async Task PathBase_is_preserved_in_emitted_next_url()
    {
        using var host = CreatePathBaseHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync($"/myapp/widgets?api-version={ApiVersionV1}", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain("/myapp/widgets");
    }

    [Fact]
    public async Task Consumer_dict_is_not_observably_mutated_across_invocations()
    {
        // Verifies the helper clones before injecting api-version, so a consumer that returns
        // the same dict instance from a closure doesn't see the api-version key accumulate.
        using var host = CreateSingleVersionHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync($"/widgets/shared-dict?api-version={ApiVersionV1}", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain("api-version=");

        // The endpoint asserts internally that the dict instance returned by the callback has
        // NO `api-version` key after the URL-building step. If mutation leaked, the endpoint
        // would have returned 500.
    }

    #endregion

    #region Cross-route

    [Fact]
    public async Task Cross_route_target_does_not_echo_unsupported_requested_version()
    {
        // Current endpoint declares v1 AND v2; client requests v2.
        // PageUrl targets a v1-only sibling route. The requested v2 must NOT leak into the
        // emitted URL — the target declares only v1, so falling back to the single declared
        // version is the unambiguous safe choice. Echoing v2 would produce a URL the target
        // route rejects (a delayed 400 the next time the client follows the next link).
        using var host = CreateMixedHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync(
            $"/multi/widgets/cross-route-to-v1-only?api-version={ApiVersionV2}",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain($"api-version={ApiVersionV1}");
        page.Next.Href.Should().NotContain($"api-version={ApiVersionV2}");
    }

    [Fact]
    public async Task UrlSegment_cross_route_target_without_declared_request_version_throws_with_guidance()
    {
        // Bug-regression: when the cross-route target is URL-segment-versioned (template
        // contains `{version:apiVersion}`), ResolveApiVersion returns null (the skip path
        // that lets same-route URL-segment requests reuse the ambient segment correctly).
        // PageUrl then calls LinkGenerator without setting the segment route value, and
        // LinkGenerator fills it from ambient route data — emitting the CURRENT request's
        // version on a sibling route that may not declare it. v2 source → v1-only segment
        // target would emit `/v{V2}/segments/widgets`, a URL the target rejects when the
        // client follows it. Surface the mismatch at emit time so the caller corrects it.
        using var host = CreateUrlSegmentHost();
        using var client = host.GetTestClient();

        var ct = TestContext.Current.CancellationToken;
        Func<Task> act = () => client.GetAsync($"/v{ApiVersionV2}/segments/multi/cross-route-to-v1-only", ct);
        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message
            .Should().Contain("URL-segment")
            .And.Contain($"api-version='{ApiVersionV2}'")
            .And.Contain("Segment_Widgets_List")
            .And.Contain("ambient");
    }

    [Fact]
    public async Task UrlSegment_cross_route_with_explicit_segment_value_in_callback_is_emitted_unmodified()
    {
        // Documented escape hatch from the UrlSegment cross-route throw: the consumer can
        // bypass the ambient-version validation by supplying the segment route value
        // explicitly in the routeValues callback. PageUrl must honour that value (whatever
        // it is) and not throw. The target endpoint's segment is filled from the consumer-
        // supplied value rather than ambient route data, so a v2 request can cleanly link
        // to a v1-only segment target by setting `["version"] = "<declared>"` in the dict.
        using var host = CreateUrlSegmentHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync(
            $"/v{ApiVersionV2}/segments/multi/cross-route-with-explicit-segment",
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain($"/v{ApiVersionV1}/segments/widgets");
        page.Next.Href.Should().NotContain($"/v{ApiVersionV2}/segments/widgets");
    }

    [Fact]
    public async Task UrlSegment_cross_route_query_escape_hatch_does_not_bypass_segment_validation()
    {
        // Round-3 regression: a callback that sets ["api-version"] (the query-string
        // escape-hatch key) but NOT the URL-segment key ("version") must still trigger
        // URL-segment cross-route validation. The earlier fix gated the validator behind
        // the api-version-injection block, so this combination silently dropped through
        // to LinkGenerator with the segment filled from ambient (v2) — leaking the
        // original cross-route bug. PageUrl must validate independently of the query key.
        using var host = CreateUrlSegmentHost();
        using var client = host.GetTestClient();

        var ct = TestContext.Current.CancellationToken;
        Func<Task> act = () => client.GetAsync(
            $"/v{ApiVersionV2}/segments/multi/cross-route-with-api-version-query-only", ct);
        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message
            .Should().Contain("URL-segment")
            .And.Contain($"api-version='{ApiVersionV2}'")
            .And.Contain("Segment_Widgets_List");
    }

    [Fact]
    public void UrlSegment_cross_route_unparseable_ambient_route_value_throws_with_guidance()
    {
        // Round-3 regression: LinkGenerator fills the URL-segment parameter from ambient
        // Request.RouteValues — not from RequestedApiVersion. So when the source route has
        // an unrelated parameter colliding with the target's segment name (e.g., the source
        // is /widgets/{version} where "version" identifies a non-apiVersion concept like
        // "draft"), and the source is query/header versioned (RequestedApiVersion is set
        // and declared by the target), the previous shape — validate RequestedApiVersion
        // first, fall back to ambient — let the cross-route call pass validation while
        // LinkGenerator emitted /vdraft/... for the target. The validator must inspect
        // ambient first; if ambient is present but does not parse as an ApiVersion, it
        // is itself a leak and must throw.
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Request.RouteValues["version"] = "draft"; // unparseable as ApiVersion

        var v1 = new ApiVersion(new DateOnly(2026, 11, 12));
        var targetEndpoint = BuildUrlSegmentEndpointDeclaring(
            "Segment_Widgets_List",
            "v{version:apiVersion}/segments/widgets",
            v1);

        Action act = () => HttpContextPageUrlExtensions.ValidateUrlSegmentCrossRouteOrThrow(
            ctx,
            targetEndpoint,
            routeName: "Segment_Widgets_List",
            values: []);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("ambient route value")
            .And.Contain("'version'='draft'")
            .And.Contain("not a valid ApiVersion")
            .And.Contain("Segment_Widgets_List");
    }

    [Fact]
    public void UrlSegment_cross_route_ambient_takes_priority_over_RequestedApiVersion()
    {
        // Round-3 regression: validator must prefer ambient Request.RouteValues[segmentKey]
        // over RequestedApiVersion when both are set, because LinkGenerator uses ambient
        // to fill the segment. A query-versioned source whose RequestedApiVersion the
        // target declares (so a RequestedApiVersion-first check would pass) but whose
        // ambient route value is a version the target does NOT declare must still throw.
        var v1 = new ApiVersion(new DateOnly(2026, 11, 12));
        var v2 = new ApiVersion(new DateOnly(2026, 12, 1));

        // Set up a context where RequestedApiVersion is v1 (= target-declared) but ambient
        // route value for the segment key is v2 (= NOT target-declared). The httpContext
        // doesn't expose a public setter for RequestedApiVersion, so use Features:
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Features.Set<global::Asp.Versioning.IApiVersioningFeature>(
            new global::Asp.Versioning.ApiVersioningFeature(ctx) { RequestedApiVersion = v1 });
        ctx.Request.RouteValues["version"] = ApiVersionV2; // = v2, target does NOT declare

        var targetEndpoint = BuildUrlSegmentEndpointDeclaring(
            "Segment_Widgets_List",
            "v{version:apiVersion}/segments/widgets",
            v1); // declares ONLY v1

        Action act = () => HttpContextPageUrlExtensions.ValidateUrlSegmentCrossRouteOrThrow(
            ctx,
            targetEndpoint,
            routeName: "Segment_Widgets_List",
            values: []);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("URL-segment")
            .And.Contain($"api-version='{ApiVersionV2}'") // ambient, not requested
            .And.Contain("Segment_Widgets_List");
    }

    [Fact]
    public void UrlSegment_cross_route_validates_ambient_route_value_when_no_RequestedApiVersion()
    {
        // Round-3 regression: ValidateUrlSegmentCrossRouteOrThrow's earlier shape returned
        // early when httpContext.RequestedApiVersion was null — leaving LinkGenerator free
        // to fill the {version} segment from ambient Request.RouteValues. That covers
        // hosts whose source endpoint has the {version:apiVersion} segment in its template
        // but where no IApiVersionReader produced a RequestedApiVersion (e.g., the source
        // is API-version-neutral, or the host registered only a query-string reader). The
        // validator must inspect ambient route values for the segment key and validate
        // them against the target's declared versions.
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Request.RouteValues["version"] = ApiVersionV2;

        var targetEndpoint = BuildUrlSegmentEndpointDeclaring(
            "Segment_Widgets_List",
            "v{version:apiVersion}/segments/widgets",
            new ApiVersion(new DateOnly(2026, 11, 12)));

        Action act = () => HttpContextPageUrlExtensions.ValidateUrlSegmentCrossRouteOrThrow(
            ctx,
            targetEndpoint,
            routeName: "Segment_Widgets_List",
            values: []);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("URL-segment")
            .And.Contain($"api-version='{ApiVersionV2}'")
            .And.Contain("Segment_Widgets_List");
    }

    [Fact]
    public async Task Default_api_version_not_declared_by_target_falls_through_to_throw()
    {
        // Bug-regression: ResolveApiVersion's host-level fallback (step 3) used to return
        // ApiVersioningOptions.DefaultApiVersion unconditionally. Asp.Versioning's
        // DefaultApiVersion is non-null by default (1.0) even on date-versioned hosts that
        // never explicitly configured it — so a date-versioned multi-version target with no
        // matching DefaultApiVersion would silently get `?api-version=1.0`, a URL the target
        // rejects. The fallback must validate the configured default against the target's
        // declared versions exactly like step 1 (RequestedApiVersion) does. Otherwise this
        // path leaks an undeclared version into the emitted URL.
        var defaultThatTargetDoesNotDeclare = new ApiVersion(1, 0);
        var ctx = BuildHttpContextWithDefaultApiVersion(defaultThatTargetDoesNotDeclare);
        var endpoint = BuildMultiVersionEndpointWithoutDefault();

        Action act = () =>
            HttpResponseOptionsBuilderApiVersioningExtensions.ResolveApiVersion(
                ctx,
                endpoint,
                callerLabel: "the next-page URL",
                explicitOverloadHint: "PageUrl(routeName, ApiVersion, ...)");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("declares more than one")
            .And.Contain("next-page URL")
            .And.Contain("PageUrl")
            .And.Contain("ApiVersion");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Multi_version_resolution_failure_message_mentions_PageUrl_overload()
    {
        // Direct unit test on the internal resolver. The HTTP-driven path isn't reachable for
        // this scenario without weakening the request-time invariants (multi-version target
        // requires a default version or explicit client version, both of which feed the resolver
        // an answer before it hits the throw branch). We pin the message wording the resolver
        // emits when PageUrl calls it — that's what the NIT was about.
        var ctx = BuildHttpContextWithoutRequestedVersion();
        var endpoint = BuildMultiVersionEndpointWithoutDefault();

        Action act = () =>
            HttpResponseOptionsBuilderApiVersioningExtensions.ResolveApiVersion(
                ctx,
                endpoint,
                callerLabel: "the next-page URL",
                explicitOverloadHint: "PageUrl(routeName, ApiVersion, ...)");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("next-page URL")
            .And.Contain("PageUrl")
            .And.Contain("ApiVersion");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Split_mapping_declared_versions_counted_as_multi_not_single()
    {
        // Asp.Versioning splits declarations across mapping kinds when a controller carries
        // [ApiVersion(v1)] and an action carries [MapToApiVersion(v2)]: v1 lands under
        // Implicit, v2 under Explicit. The single-declared fallback (step 2) must count the
        // distinct union — Map(Implicit) ∪ Map(Explicit) — not just one mapping. Otherwise
        // a target that declares two versions would look single-declared via Implicit alone
        // and the resolver would silently return v1, defeating the cross-route validation
        // built into step 1.
        var ctx = BuildHttpContextWithoutRequestedVersion();
        var endpoint = BuildSplitMappingEndpointWithoutDefault();

        Action act = () =>
            HttpResponseOptionsBuilderApiVersioningExtensions.ResolveApiVersion(
                ctx,
                endpoint,
                callerLabel: "the next-page URL",
                explicitOverloadHint: "PageUrl(routeName, ApiVersion, ...)");

        // No requested version + no default + two distinct declared versions split across
        // mappings ⇒ must throw, not silently echo v1. If the resolver returned a string,
        // the single-declared fallback miscounted.
        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("declares more than one");

        await Task.CompletedTask;
    }

    #endregion

    #region Unversioned host (no AddApiVersioning called)

    [Fact]
    public async Task PerRequest_PageUrl_without_AddApiVersioning_emits_url_without_api_version()
    {
        // The host never calls services.AddApiVersioning(...) and the controller carries no
        // [ApiVersion] attribute, so the endpoint has no ApiVersionMetadata at all. The
        // per-request PageUrl helper must NOT throw the unresolvable-version error (which
        // tells callers to configure DefaultApiVersion or use the explicit overload —
        // advice that does not apply when versioning isn't in use). It must emit a clean
        // next-page URL with no api-version query parameter.
        using var host = CreateUnversionedHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/unversioned/widgets", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().NotContain("api-version");
        page.Next.Href.Should().Contain("/unversioned/widgets");
        page.Next.Href.Should().Contain("cursor=cur-2");
    }

    [Fact]
    public async Task Explicit_PageUrl_without_AddApiVersioning_skips_pin_silently()
    {
        // Same unversioned-host setup as above, but the controller calls the EXPLICIT
        // overload PageUrl(routeName, version, callback). With no ApiVersionMetadata on
        // the target, the pinned version has no declared-version set to validate against
        // and emitting it as a query parameter would be a stale URL artefact (the host
        // has no API-versioning middleware to consume it). The pin must be silently
        // suppressed — same precedent as [ApiVersionNeutral] — and a clean URL emitted.
        using var host = CreateUnversionedHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/unversioned/widgets/pinned", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().NotContain("api-version");
        page.Next.Href.Should().Contain("/unversioned/widgets/pinned");
        page.Next.Href.Should().Contain("cursor=cur-2");
    }

    #endregion

    #region Minimal APIs (named via .WithName(...))

    [Fact]
    public async Task MinimalApi_endpoint_named_via_WithName_resolves_for_PageUrl()
    {
        // Regression: a code-review pass questioned whether HttpContext.PageUrl(...) could
        // resolve minimal-API endpoints named via .WithName("..."). The concern was that
        // FindEndpointByRouteName only inspects RouteNameMetadata while minimal APIs use
        // EndpointNameMetadata. The ASP.NET Core source for .WithName (see
        // RoutingEndpointConventionBuilderExtensions.WithName) attaches BOTH metadata
        // types — so the canonical .WithName(...) shape works without a fallback. Pin
        // that with an end-to-end test so a future ASP.NET Core change (or a Trellis
        // refactor of FindEndpointByRouteName) that drops minimal-API coverage is caught.
        using var host = CreateMinimalApiHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/minimal/widgets", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await resp.Content.ReadFromJsonAsync<PagedShape>(JsonOpts, TestContext.Current.CancellationToken);
        page!.Next!.Href.Should().Contain("/minimal/widgets");
        page.Next.Href.Should().Contain("cursor=cur-2");
    }

    #endregion

    #region Helpers

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static void AssertLinkHeader(HttpResponseMessage resp, string expectedNextHref)
    {
        resp.Headers.TryGetValues("Link", out var values).Should().BeTrue();
        var link = values!.Single();
        link.Should().Contain($"<{expectedNextHref}>; rel=\"next\"");
    }

    private static IHost CreateSingleVersionHost()
    {
        var b = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(SingleVersionPagedController).Assembly);
                    s.AddApiVersioning(o =>
                    {
                        o.ApiVersionReader = new QueryStringApiVersionReader("api-version");
                        o.AssumeDefaultVersionWhenUnspecified = true;
                        o.DefaultApiVersion = new ApiVersion(new DateOnly(2026, 11, 12));
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return b.Start();
    }

    private static IHost CreateMultiVersionHost(ApiVersion? defaultVersion)
    {
        var b = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(MultiVersionPagedController).Assembly);
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
        return b.Start();
    }

    private static IHost CreateNeutralHost()
    {
        var b = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(NeutralPagedController).Assembly);
                    s.AddApiVersioning().AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return b.Start();
    }

    private static IHost CreateUrlSegmentHost()
    {
        var b = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(SegmentPagedController).Assembly);
                    s.AddApiVersioning(o => o.ApiVersionReader = new UrlSegmentApiVersionReader()).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return b.Start();
    }

    private static IHost CreatePathBaseHost()
    {
        var b = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(SingleVersionPagedController).Assembly);
                    s.AddApiVersioning(o =>
                    {
                        o.ApiVersionReader = new QueryStringApiVersionReader("api-version");
                        o.AssumeDefaultVersionWhenUnspecified = true;
                        o.DefaultApiVersion = new ApiVersion(new DateOnly(2026, 11, 12));
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    app.UsePathBase("/myapp");
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return b.Start();
    }

    private static IHost CreateMixedHost()
    {
        // Registers BOTH the v1-only SingleVersionPagedController (route "Widgets_List") and
        // the multi-version MultiVersionPagedController. A request hits the multi-version
        // controller at v2 and asks PageUrl to build a URL targeting the v1-only Widgets_List
        // route — the cross-route case where echoing the requested version would emit a URL
        // the target route doesn't accept.
        var b = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers()
                        .AddApplicationPart(typeof(SingleVersionPagedController).Assembly);
                    s.AddApiVersioning(o => o.ApiVersionReader = new QueryStringApiVersionReader("api-version")).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return b.Start();
    }

    private static IHost CreateMinimalApiHost()
    {
        // Pure minimal-API host: no MVC controllers, no AddApiVersioning(). Pins the
        // contract that HttpContext.PageUrl resolves endpoints registered via
        // app.MapGet("...").WithName("...") — the canonical minimal-API name-attachment
        // shape. ASP.NET Core's .WithName(...) attaches BOTH EndpointNameMetadata and
        // RouteNameMetadata (see RoutingEndpointConventionBuilderExtensions.WithName),
        // so FindEndpointByRouteName's RouteNameMetadata lookup catches it.
        var b = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e
                        .MapGet("/minimal/widgets", (HttpContext ctx) =>
                        {
                            var (page, _) = PagedFixtures.NextPage(null);
                            var result = Trellis.Result.Ok(page);
                            return result.ToHttpResponse(
                                nextUrlBuilder: ctx.PageUrl(
                                    "Minimal_Widgets_List",
                                    (c, applied) => new RouteValueDictionary
                                    {
                                        ["cursor"] = c.Token,
                                        ["limit"] = applied,
                                    }),
                                body: WidgetResponse.From);
                        })
                        .WithName("Minimal_Widgets_List"));
                }));
        return b.Start();
    }

    private static IHost CreateUnversionedHost()
    {
        // No services.AddApiVersioning(...) call. The UnversionedPagedController carries no
        // [ApiVersion] attribute, so the registered endpoint will have no ApiVersionMetadata
        // — the canonical "host doesn't use API versioning" shape.
        // The application part is scoped to UnversionedPagedController only because other
        // test controllers carry routes with the `:apiVersion` constraint, which is only
        // registered when AddApiVersioning() runs — including them here would explode the
        // route table for reasons unrelated to the PageUrl behaviour under test.
        var b = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers()
                        .ConfigureApplicationPartManager(apm =>
                        {
                            apm.FeatureProviders.Clear();
                            apm.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(UnversionedPagedController)));
                        });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return b.Start();
    }

    private sealed class SingleControllerFeatureProvider : Microsoft.AspNetCore.Mvc.Controllers.ControllerFeatureProvider
    {
        private readonly HashSet<Type> _allowed;
        public SingleControllerFeatureProvider(params Type[] allowed) => _allowed = [.. allowed];
        protected override bool IsController(System.Reflection.TypeInfo typeInfo) => _allowed.Contains(typeInfo.AsType());
    }

    private static DefaultHttpContext BuildHttpContextWithoutRequestedVersion()
    {
        // Minimal HttpContext for the resolver: empty RequestServices (no DefaultApiVersion
        // configured) and no RequestedApiVersion. Forces ResolveApiVersion through the
        // multi-version unresolvable branch.
        var services = new ServiceCollection().BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private static DefaultHttpContext BuildHttpContextWithDefaultApiVersion(ApiVersion defaultVersion)
    {
        // Variant of BuildHttpContextWithoutRequestedVersion that wires IOptions<ApiVersioningOptions>
        // with a configured DefaultApiVersion. Lets the resolver's host-level fallback (step 3)
        // actually run, so tests can pin the cross-route emitted version OR observe the new
        // target-declaration validation that fails the fallback when the configured default is
        // not declared by the target endpoint.
        var services = new ServiceCollection();
        services.Configure<ApiVersioningOptions>(opt => opt.DefaultApiVersion = defaultVersion);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static RouteEndpoint BuildMultiVersionEndpointWithoutDefault()
    {
        // ApiVersionMetadata declaring 2 implicit versions and no explicit ones. Count != 1,
        // so the single-declared fallback doesn't apply. With no default version on the
        // HttpContext, the resolver hits the throw branch.
        var v1 = new ApiVersion(new DateOnly(2026, 11, 12));
        var v2 = new ApiVersion(new DateOnly(2026, 12, 1));
        var model = new ApiVersionModel([v1, v2], [v1, v2], [], [], []);
        var metadata = new ApiVersionMetadata(model, ApiVersionModel.Empty, "MultiVersionTarget");

        var endpoint = new RouteEndpoint(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("multi/widgets"),
            order: 0,
            metadata: new EndpointMetadataCollection(metadata),
            displayName: "MultiVersionTarget");
        return endpoint;
    }

    private static RouteEndpoint BuildSplitMappingEndpointWithoutDefault()
    {
        // ApiVersionMetadata that splits declarations across mapping kinds: v1 lives under
        // the Implicit mapping (the controller-level [ApiVersion(v1)] equivalent) and v2 under
        // the Explicit mapping (the action-level [MapToApiVersion(v2)] equivalent). Each
        // mapping individually has Count == 1 and would fool a single-mapping fallback into
        // returning v1; the distinct UNION is 2 and must therefore not pick a winner.
        var v1 = new ApiVersion(new DateOnly(2026, 11, 12));
        var v2 = new ApiVersion(new DateOnly(2026, 12, 1));
        var implicitModel = new ApiVersionModel([v1], [v1], [], [], []);
        var explicitModel = new ApiVersionModel([v2], [v2], [], [], []);
        var metadata = new ApiVersionMetadata(implicitModel, explicitModel, "SplitMappingTarget");

        var endpoint = new RouteEndpoint(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("split/widgets"),
            order: 0,
            metadata: new EndpointMetadataCollection(metadata),
            displayName: "SplitMappingTarget");
        return endpoint;
    }

    private static RouteEndpoint BuildUrlSegmentEndpointDeclaring(
        string displayName,
        string routeTemplate,
        params ApiVersion[] declaredVersions)
    {
        // URL-segment endpoint helper for the round-3 cross-route-validation unit tests.
        // routeTemplate must contain a {param:apiVersion} segment so
        // TryGetUrlSegmentVersionParameterName returns the parameter name; declaredVersions
        // populates the implicit declared set (single mapping is enough for the regression).
        var versions = declaredVersions.ToArray();
        var model = new ApiVersionModel(versions, versions, [], [], []);
        var metadata = new ApiVersionMetadata(model, ApiVersionModel.Empty, displayName);

        return new RouteEndpoint(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse(routeTemplate),
            order: 0,
            metadata: new EndpointMetadataCollection(metadata),
            displayName: displayName);
    }

    #endregion
}

#region Test fixture types

public sealed record WidgetResponse(string Id)
{
    public static WidgetResponse From(string id) => new(id);
}
public sealed record PagedShape(IReadOnlyList<WidgetResponse> Items, NextShape? Next);
public sealed record NextShape(string Cursor, string Href);

[ApiController]
[ApiVersion(HttpContextPageUrlExtensionsTests_ApiVersions.V1)]
[Route("widgets")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class SingleVersionPagedController : ControllerBase
{
    [HttpGet(Name = "Widgets_List")]
    public HttpResult List([FromQuery] string? cursor)
    {
        var (page, nextCursor) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Widgets_List",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("consumer-supplied", Name = "Widgets_ConsumerSupplied")]
    public HttpResult ConsumerSupplied([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Widgets_ConsumerSupplied",
                (c, applied) => new RouteValueDictionary
                {
                    ["cursor"] = c.Token,
                    ["limit"] = applied,
                    ["api-version"] = "2099-01-01",
                }),
            body: WidgetResponse.From);
    }

    [HttpGet("unknown-route", Name = "Widgets_UnknownRouteEndpoint")]
    public HttpResult UnknownRoute([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        // Target route name 'Widgets_RouteDoesNotExist' is not registered anywhere.
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Widgets_RouteDoesNotExist",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("funky-cursor", Name = "Widgets_FunkyCursor")]
    public HttpResult FunkyCursor([FromQuery] string? cursor)
    {
        if (cursor == "a+b/c d=e&f")
        {
            // Follow-up request: the cursor was URL-decoded by the model binder. Echo it back
            // raw in the body so the test can assert decode correctness.
            return Microsoft.AspNetCore.Http.Results.Ok(cursor);
        }

        // Initial request: emit a Page whose next cursor contains special chars.
        var page = new Page<string>(
            Items: ["w1", "w2"],
            Next: new Cursor("a+b/c d=e&f"),
            Previous: null,
            RequestedLimit: 2,
            AppliedLimit: 2);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Widgets_FunkyCursor",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: s => new WidgetResponse(s));
    }

    [HttpGet("shared-dict", Name = "Widgets_SharedDict")]
    public HttpResult SharedDict([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);

        // The same dict instance is returned every call to the callback. If the helper writes
        // `api-version` into this dict, the second invocation would see it left over from the
        // first — observable mutation. We assert no mutation by inspecting the dict after the
        // URL-building step has completed.
        var sharedDict = new RouteValueDictionary();

        var nextBuilder = HttpContext.PageUrl(
            "Widgets_SharedDict",
            (c, applied) =>
            {
                sharedDict["cursor"] = c.Token;
                sharedDict["limit"] = applied;
                return sharedDict;
            });

        // Invoke once to trigger the (potential) mutation.
        var built = nextBuilder(new Cursor("cur-2"), 2);
        if (sharedDict.ContainsKey("api-version"))
        {
            throw new InvalidOperationException(
                "Consumer dict was mutated: PageUrl helper wrote api-version into the dict that the callback returned.");
        }

        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Widgets_SharedDict",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }
}

[ApiController]
[ApiVersion(HttpContextPageUrlExtensionsTests_ApiVersions.V1)]
[ApiVersion(HttpContextPageUrlExtensionsTests_ApiVersions.V2)]
[Route("multi/widgets")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class MultiVersionPagedController : ControllerBase
{
    [HttpGet(Name = "Multi_Widgets_List")]
    public HttpResult List([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Multi_Widgets_List",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("pinned", Name = "Multi_Widgets_Pinned")]
    public HttpResult Pinned([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Multi_Widgets_Pinned",
                new ApiVersion(new DateOnly(2026, 12, 1)),
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("pin-overrides-consumer-supplied", Name = "Multi_Widgets_PinOverridesConsumer")]
    public HttpResult PinOverridesConsumer([FromQuery] string? cursor)
    {
        // Bug-regression: the explicit-pin overload used to honor a consumer-supplied
        // api-version in the callback dictionary, silently bypassing both the pin AND
        // its declared-version validation. Pinning V2 while the callback inserts V1
        // must result in V2 in the emitted URL — pinned means pinned.
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Multi_Widgets_PinOverridesConsumer",
                new ApiVersion(new DateOnly(2026, 12, 1)),
                (c, applied) => new RouteValueDictionary
                {
                    ["cursor"] = c.Token,
                    ["limit"] = applied,
                    ["api-version"] = HttpContextPageUrlExtensionsTests_ApiVersions.V1,
                }),
            body: WidgetResponse.From);
    }

    [HttpGet("cross-route-to-v1-only", Name = "Multi_Widgets_CrossRouteToV1Only")]
    public HttpResult CrossRouteToV1Only([FromQuery] string? cursor)
    {
        // Multi-version controller targeting the v1-only "Widgets_List" route. Even though the
        // current request may be v2, the target route declares ONLY v1. PageUrl must NOT echo
        // the request's v2 into the emitted URL — it should fall through to the target's single
        // declared version (v1).
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Widgets_List",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("pin-v2-on-v1-only", Name = "Multi_Widgets_PinV2OnV1Only")]
    public HttpResult PinV2OnV1Only([FromQuery] string? cursor)
    {
        // Bug-regression: the explicit-pin overload used to inject the pinned version without
        // validating it against the target endpoint's declared versions. Pinning v2 on the
        // v1-only "Widgets_List" route would generate a URL the target rejects when followed.
        // Must now throw with actionable guidance so the caller sees the misconfiguration at
        // emit time, not when the client follows the bad link.
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Widgets_List",
                new ApiVersion(new DateOnly(2026, 12, 1)),
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }
}

[ApiController]
[ApiVersionNeutral]
[Route("neutral/widgets")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class NeutralPagedController : ControllerBase
{
    [HttpGet(Name = "Neutral_Widgets_List")]
    public HttpResult List([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Neutral_Widgets_List",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("pinned", Name = "Neutral_Widgets_Pinned")]
    public HttpResult Pinned([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Neutral_Widgets_Pinned",
                new ApiVersion(new DateOnly(2026, 12, 1)),
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }
}

[ApiController]
[ApiVersion(HttpContextPageUrlExtensionsTests_ApiVersions.V1)]
[Route("v{version:apiVersion}/segments/widgets")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class SegmentPagedController : ControllerBase
{
    [HttpGet(Name = "Segment_Widgets_List")]
    public HttpResult List([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        // Consumer supplies ONLY cursor/limit. The `{version:apiVersion}` path segment is
        // filled from ambient route data because the LinkGenerator overload that takes
        // HttpContext honors the current request's route values for missing parameters.
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Segment_Widgets_List",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("explicit-pin", Name = "Segment_Widgets_ExplicitPin")]
    public HttpResult ExplicitPin([FromQuery] string? cursor)
    {
        // Explicit-version pin targeting a URL-segment-versioned route. The pin cannot be
        // honoured as a query parameter (the segment IS the version), and silently skipping
        // it would let LinkGenerator fill the segment from ambient route data — producing
        // a URL with the WRONG version. Fail loudly instead.
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Segment_Widgets_ExplicitPin",
                new ApiVersion(new DateOnly(2026, 12, 1)),
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }
}

[ApiController]
[ApiVersion(HttpContextPageUrlExtensionsTests_ApiVersions.V1)]
[ApiVersion(HttpContextPageUrlExtensionsTests_ApiVersions.V2)]
[Route("v{version:apiVersion}/segments/multi")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class MultiVersionSegmentPagedController : ControllerBase
{
    [HttpGet("cross-route-to-v1-only", Name = "Segment_MultiVersion_CrossRoute")]
    public HttpResult CrossRouteToV1Only([FromQuery] string? cursor)
    {
        // Cross-route to the v1-only SegmentPagedController.List target. With v2 ambient,
        // LinkGenerator would fill the {version} segment from ambient route data (= v2)
        // even though the target only declares v1 — emitting a URL the target rejects.
        // The per-request PageUrl overload must validate ambient against target-declared
        // versions and throw exactly like the explicit overload does.
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Segment_Widgets_List",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("cross-route-with-explicit-segment", Name = "Segment_MultiVersion_CrossRouteWithSegment")]
    public HttpResult CrossRouteWithExplicitSegment([FromQuery] string? cursor)
    {
        // Escape hatch from the cross-route throw: caller supplies the {version} segment
        // route value explicitly in the routeValues callback. PageUrl must honour the
        // consumer-supplied segment (here pinned to V1, which the target declares) without
        // the ambient-version validation kicking in.
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Segment_Widgets_List",
                (c, applied) => new RouteValueDictionary
                {
                    ["cursor"] = c.Token,
                    ["limit"] = applied,
                    ["version"] = HttpContextPageUrlExtensionsTests_ApiVersions.V1,
                }),
            body: WidgetResponse.From);
    }

    [HttpGet("cross-route-with-api-version-query-only", Name = "Segment_MultiVersion_CrossRouteApiVersionQueryOnly")]
    public HttpResult CrossRouteWithApiVersionQueryOnly([FromQuery] string? cursor)
    {
        // Round-3 regression: caller supplies the QUERY-string escape-hatch key
        // ("api-version") but NOT the URL-segment key ("version"). The earlier round-3
        // fix gated URL-segment validation behind `!values.ContainsKey("api-version")`,
        // so this combination silently dropped through to LinkGenerator with the segment
        // filled from ambient route data — re-introducing the original cross-route leak.
        // PageUrl must still validate the URL-segment cross-route case here.
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Segment_Widgets_List",
                (c, applied) => new RouteValueDictionary
                {
                    ["cursor"] = c.Token,
                    ["limit"] = applied,
                    ["api-version"] = HttpContextPageUrlExtensionsTests_ApiVersions.V1,
                }),
            body: WidgetResponse.From);
    }
}

internal static class HttpContextPageUrlExtensionsTests_ApiVersions
{
    public const string V1 = "2026-11-12";
    public const string V2 = "2026-12-01";
}

[ApiController]
[Route("unversioned/widgets")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Test fixture controllers don't need to be static.")]
public sealed class UnversionedPagedController : ControllerBase
{
    // No [ApiVersion] attribute and no AddApiVersioning() on the host → the registered
    // endpoint will have no ApiVersionMetadata at all. Exercises the unversioned-host
    // path of HttpContext.PageUrl(...).

    [HttpGet(Name = "Unversioned_Widgets_List")]
    public HttpResult List([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Unversioned_Widgets_List",
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }

    [HttpGet("pinned", Name = "Unversioned_Widgets_Pinned")]
    public HttpResult Pinned([FromQuery] string? cursor)
    {
        var (page, _) = PagedFixtures.NextPage(cursor);
        var result = Trellis.Result.Ok(page);
        return result.ToHttpResponse(
            nextUrlBuilder: HttpContext.PageUrl(
                "Unversioned_Widgets_Pinned",
                new ApiVersion(new DateOnly(2026, 12, 1)),
                (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
            body: WidgetResponse.From);
    }
}

internal static class PagedFixtures
{
    /// <summary>
    /// Returns a 2-item page with a next-cursor of "cur-2" when no cursor is supplied,
    /// or a final 1-item page with no next-cursor when cursor == "cur-2".
    /// </summary>
    public static (Page<string> Page, string? NextCursor) NextPage(string? cursor)
    {
        if (cursor is null)
        {
            var page = new Page<string>(
                Items: ["w1", "w2"],
                Next: new Cursor("cur-2"),
                Previous: null,
                RequestedLimit: 2,
                AppliedLimit: 2);
            return (page, "cur-2");
        }

        var finalPage = new Page<string>(
            Items: ["w3"],
            Next: null,
            Previous: null,
            RequestedLimit: 2,
            AppliedLimit: 2);
        return (finalPage, null);
    }
}

#endregion