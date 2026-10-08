namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

public sealed class HttpContextPageUrlExtensionsTests
{
    private const string RouteName = "Widgets_List";

    [Fact]
    public void PageUrl_Default_UnversionedRoute_PreservesRequestAndEncodesValues()
    {
        using var services = CreateServices(BuildEndpoint("widgets"));
        var context = CreateContext(services);
        var values = new RouteValueDictionary { ["cursor"] = "a +/?", ["limit"] = 7 };
        var builder = context.PageUrl(RouteName, (_, _) => values);

        var url = new Uri(builder(new Cursor("next"), 7));

        url.GetLeftPart(UriPartial.Path).Should().Be("https://example.com:8443/gateway/widgets");
        QueryHelpers.ParseQuery(url.Query)["cursor"].ToString().Should().Be("a +/?");
        QueryHelpers.ParseQuery(url.Query)["limit"].ToString().Should().Be("7");
        values.Should().HaveCount(2);
        typeof(TrellisAspOptions).Assembly.GetReferencedAssemblies()
            .Should().NotContain(assembly => assembly.Name!.StartsWith("Asp.Versioning", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PageDirection.Next, "after")]
    [InlineData(PageDirection.Previous, "before")]
    public void PageUrl_Directional_UnversionedRoute_ForwardsDirectionAndLimit(PageDirection direction, string key)
    {
        using var services = CreateServices(BuildEndpoint("widgets"));
        var context = CreateContext(services);
        var builder = context.PageUrl(RouteName, (cursor, actualDirection, limit) =>
            new RouteValueDictionary
            {
                [actualDirection == PageDirection.Next ? "after" : "before"] = cursor.Token,
                ["limit"] = limit
            });

        var query = QueryHelpers.ParseQuery(new Uri(builder(new Cursor("next"), direction, 9)).Query);

        query[key].ToString().Should().Be("next");
        query["limit"].ToString().Should().Be("9");
        query.Should().HaveCount(2);
    }

    [Fact]
    public void PageUrl_Default_PathParameters_UsesAmbientValues()
    {
        using var services = CreateServices(BuildEndpoint("tenants/{tenant}/widgets"));
        var context = CreateContext(services);
        context.Request.RouteValues["tenant"] = "north";

        context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2)
            .Should().Be("https://example.com:8443/gateway/tenants/north/widgets");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_Default_SharedSelfRoute_UsesActiveEndpoint(bool wrapped)
    {
        var first = BuildEndpoint("widgets");
        var second = BuildEndpoint("widgets");
        using var services = CreateServices(first, second);
        var context = CreateContext(services);
        context.SetEndpoint(wrapped
            ? new Endpoint(second.RequestDelegate, second.Metadata, second.DisplayName)
            : second);

        context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2)
            .Should().Be("https://example.com:8443/gateway/widgets");
    }

    [Fact]
    public void PageUrl_Default_SuppressedCandidate_IsIgnored()
    {
        using var services = CreateServices(BuildEndpoint("suppressed", suppress: true), BuildEndpoint("widgets"));
        var context = CreateContext(services);

        context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2)
            .Should().EndWith("/widgets");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_Default_MissingDestination_Throws(bool suppressed)
    {
        using var services = CreateServices(suppressed ? [BuildEndpoint("widgets", suppress: true)] : []);
        var context = CreateContext(services);
        Action action = () => context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*no registered*Widgets_List*");
    }

    [Fact]
    public void PageUrl_Default_SharedCrossRoute_ThrowsRatherThanSelectingArbitrarily()
    {
        using var services = CreateServices(BuildEndpoint("widgets"), BuildEndpoint("widgets"));
        var context = CreateContext(services);
        Action action = () => context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*ambiguous*");
    }

    [Fact]
    public void PageUrl_Default_SharedRouteDifferentLayouts_ThrowsEvenForSelfPagination()
    {
        var current = BuildEndpoint("first");
        using var services = CreateServices(current, BuildEndpoint("second"));
        var context = CreateContext(services);
        context.SetEndpoint(current);
        Action action = () => context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*different*templates*");
    }

    [Fact]
    public void PageUrl_Default_SharedRouteDifferentDefaults_Throws()
    {
        RouteEndpoint Target(string defaultValue) =>
            new(_ => Task.CompletedTask, RoutePatternFactory.Parse("widgets/{category}", new { category = defaultValue }, null), 0,
                new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), defaultValue);
        using var services = CreateServices(Target("first"), Target("second"));
        var context = CreateContext(services);
        Action action = () => context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*different*defaults*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_Default_SharedRouteNonParameterDefaultConsumesCursor_Throws(bool reverseOrder)
    {
        var selected = BuildEndpoint("widgets/{id}");
        var other = new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("widgets/{id}", new { cursor = "next" }, null), 0,
            new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), RouteName);
        using var services = CreateServices(reverseOrder ? [selected, other] : [other, selected]);
        var context = CreateContext(services);
        context.SetEndpoint(selected);
        var builder = context.PageUrl(RouteName, (cursor, limit) =>
            new() { ["id"] = 123, ["cursor"] = cursor.Token, ["limit"] = limit });
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*different*defaults*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_CustomResolver_SharedRouteNonMvcSelectorConsumesCursor_Throws(bool reverseOrder)
    {
        RouteEndpoint Target(string cursor) =>
            new(_ => Task.CompletedTask,
                RoutePatternFactory.Parse("widgets/{id}", defaults: new { cursor },
                    parameterPolicies: null, requiredValues: new { cursor }), 0,
                new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), cursor);
        var other = Target("next");
        var selected = Target("later");
        using var services = CreateServices(reverseOrder ? [selected, other] : [other, selected]);
        var context = CreateContext(services);
        var builder = context.PageUrl(RouteName, (cursor, limit) =>
            new() { ["id"] = 123, ["cursor"] = cursor.Token, ["limit"] = limit }, _ => selected);
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*different*defaults*");
    }

    [Theory]
    [InlineData("controller")]
    [InlineData("action")]
    [InlineData("area")]
    public void PageUrl_CustomResolver_SharedRouteSelectorKeyWithoutRequiredValue_Throws(string key)
    {
        RouteEndpoint Target(string value) =>
            new(_ => Task.CompletedTask,
                RoutePatternFactory.Parse("widgets/{id}",
                    new RouteValueDictionary { [key] = value }, parameterPolicies: null), 0,
                new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), value);
        var other = Target("next");
        var selected = Target("later");
        using var services = CreateServices(other, selected);
        var context = CreateContext(services);
        var builder = context.PageUrl(RouteName, (_, _) => new() { ["id"] = 123, [key] = "next" }, _ => selected);
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*different*defaults*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_CustomResolver_SharedRouteDifferentParameterPolicies_Throws(bool reverseOrder)
    {
        RouteEndpoint Target(IParameterPolicy policy) =>
            new(_ => Task.CompletedTask,
                RoutePatternFactory.Parse("widgets/{id}", defaults: null, parameterPolicies: new { id = policy }), 0,
                new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), policy.GetType().Name);
        var selected = Target(new IntRouteConstraint());
        var other = Target(new AlphaRouteConstraint());
        using var services = CreateServices(reverseOrder ? [other, selected] : [selected, other]);
        var context = CreateContext(services);
        var builder = context.PageUrl(RouteName, (_, _) => new() { ["id"] = "abc" }, _ => selected);
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*different*parameter policies*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_CustomResolver_SharedRouteDifferentRequiredValues_Throws(bool reverseOrder)
    {
        RouteEndpoint Target(string controller) =>
            new(_ => Task.CompletedTask,
                RoutePatternFactory.Parse("{controller}/{action}", defaults: null, parameterPolicies: null,
                    requiredValues: new { controller, action = "List" }), 0,
                new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), controller);
        var other = Target("First");
        var selected = Target("Second");
        using var services = CreateServices(reverseOrder ? [other, selected] : [selected, other]);
        var context = CreateContext(services);
        context.Request.RouteValues["controller"] = "First";
        context.Request.RouteValues["action"] = "List";
        var builder = context.PageUrl(RouteName, (_, _) => new(), _ => selected);
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*different*required values*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_CustomResolver_SharedRouteDifferentNonParameterValues_GeneratesSamePath(bool reverseOrder)
    {
        RouteEndpoint Target(string controller) =>
            new(_ => Task.CompletedTask,
                RoutePatternFactory.Parse("widgets/{id}", defaults: new { controller, action = "List" },
                    parameterPolicies: null, requiredValues: new { controller, action = "List" }), 0,
                new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), controller);
        var other = Target("First");
        var selected = Target("Second");
        using var services = CreateServices(reverseOrder ? [other, selected] : [selected, other]);
        var context = CreateContext(services);
        context.Request.RouteValues["controller"] = "First";
        context.Request.RouteValues["action"] = "List";

        context.PageUrl(RouteName, (_, _) => new() { ["id"] = 123 }, _ => selected)(new Cursor("next"), 2)
            .Should().Be("https://example.com:8443/gateway/widgets/123");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_CustomResolver_SharedRouteMatchingPoliciesAndRequiredValues_GeneratesUrl(bool inlinePolicy)
    {
        var policy = new IntRouteConstraint();
        RouteEndpoint Target() =>
            new(_ => Task.CompletedTask,
                RoutePatternFactory.Parse(inlinePolicy ? "widgets/{id:int}" : "widgets/{id}", defaults: null,
                    parameterPolicies: inlinePolicy ? null : new { id = policy },
                    requiredValues: new { id = 123 }), 0,
                new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), RouteName);
        var first = Target();
        var selected = Target();
        using var services = CreateServices(first, selected);
        var context = CreateContext(services);

        context.PageUrl(RouteName, (_, _) => new() { ["id"] = 123 }, _ => selected)(new Cursor("next"), 2)
            .Should().Be("https://example.com:8443/gateway/widgets/123");
    }

    [Fact]
    public void PageUrl_Default_SuppressedActiveEndpoint_CannotWinSelection()
    {
        using var services = CreateServices(BuildEndpoint("widgets"), BuildEndpoint("widgets"));
        var context = CreateContext(services);
        context.SetEndpoint(BuildEndpoint("widgets", suppress: true));
        Action action = () => context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*ambiguous*");
    }

    [Fact]
    public void PageUrl_CustomResolver_Throws_PropagatesOriginalException()
    {
        using var services = CreateServices(BuildEndpoint("widgets"));
        var context = CreateContext(services);
        var failure = new InvalidOperationException("Policy failure.");
        var builder = context.PageUrl(RouteName, (_, _) => new(), _ => throw failure);
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
    }

    [Fact]
    public void PageUrl_Default_MissingPathParameter_Throws()
    {
        using var services = CreateServices(BuildEndpoint("widgets/{id}"));
        var context = CreateContext(services);
        Action action = () => context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*LinkGenerator*Widgets_List*");
    }

    [Fact]
    public void PageUrl_Default_NullCallbackDictionary_Throws()
    {
        using var services = CreateServices(BuildEndpoint("widgets"));
        var context = CreateContext(services);
        Action action = () => context.PageUrl(RouteName, (_, _) => null!)(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*callback returned null*");
    }

    [Fact]
    public void PageUrl_HostResolver_EnrichesCloneAndRemainsHostLocal()
    {
        var values = new RouteValueDictionary();
        using var configured = CreateServices([BuildEndpoint("widgets")], options =>
            options.PageUrlRouteResolver = route =>
            {
                route.RouteName.Should().Be(RouteName);
                route.HttpContext.Request.Host.Host.Should().Be("example.com");
                route.RouteValues["policy"] = "configured";
                return route.Candidates[0];
            });
        using var ordinary = CreateServices(BuildEndpoint("widgets"));

        CreateContext(configured).PageUrl(RouteName, (_, _) => values)(new Cursor("next"), 2)
            .Should().Contain("policy=configured");
        CreateContext(ordinary).PageUrl(RouteName, (_, _) => values)(new Cursor("next"), 2)
            .Should().NotContain("policy=");
        values.Should().BeEmpty();
    }

    [Fact]
    public void PageUrl_PerBuilderResolver_OverridesHostResolver()
    {
        using var services = CreateServices([BuildEndpoint("widgets")], options =>
            options.PageUrlRouteResolver = _ => throw new InvalidOperationException("Host policy must not run."));
        var context = CreateContext(services);
        var builder = context.PageUrl(RouteName, (_, _) => new(), route =>
        {
            route.RouteValues["policy"] = "pinned";
            return route.Candidates[0];
        });

        builder(new Cursor("next"), 2).Should().Contain("policy=pinned");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_CustomResolver_InvalidDestination_Throws(bool returnsNull)
    {
        using var services = CreateServices(BuildEndpoint("widgets"));
        var context = CreateContext(services);
        var builder = context.PageUrl(RouteName, (_, _) => new(), _ =>
            returnsNull ? null! : BuildEndpoint("unrelated"));
        Action action = () => builder(new Cursor("next"), 2);

        action.Should().Throw<InvalidOperationException>().WithMessage("*resolver*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void PageUrl_Default_InvalidRouteName_ThrowsAtCreation(string? routeName)
    {
        Action action = () => new DefaultHttpContext().PageUrl(routeName!, (_, _) => new());

        action.Should().Throw<ArgumentException>().WithParameterName(nameof(routeName));
    }

    [Fact]
    public void PageUrl_Default_NullArguments_ThrowAtCreation()
    {
        Action nullContext = () => ((HttpContext)null!).PageUrl(RouteName, (_, _) => new());
        Action nullCallback = () => new DefaultHttpContext().PageUrl(RouteName, (Func<Cursor, int, RouteValueDictionary>)null!);

        nullContext.Should().Throw<ArgumentNullException>().WithParameterName("httpContext");
        nullCallback.Should().Throw<ArgumentNullException>().WithParameterName("routeValues");
    }

    private static RouteEndpoint BuildEndpoint(string pattern, bool suppress = false) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0,
            new EndpointMetadataCollection(suppress
                ? [new RouteNameMetadata(RouteName), new SuppressLinkGenerationMetadata()]
                : [new RouteNameMetadata(RouteName)]), pattern);

    private static ServiceProvider CreateServices(params Endpoint[] endpoints) => CreateServices(endpoints, null);

    private static ServiceProvider CreateServices(Endpoint[] endpoints, Action<TrellisAspOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(endpoints));
        if (configure is not null)
            services.AddTrellisAsp(configure);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com", 8443);
        context.Request.PathBase = "/gateway";
        return context;
    }
}
