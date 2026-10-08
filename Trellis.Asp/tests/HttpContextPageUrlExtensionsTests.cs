namespace Trellis.Asp.Tests;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

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

    [Fact]
    public void PageUrl_Cache_RepeatedBuilders_ReusesCandidatesWithoutCachingRequestValues()
    {
        using var dataSource = new MutableEndpointDataSource(BuildEndpoint("widgets"));
        using var services = CreateServices(dataSource);
        var candidates = new List<IReadOnlyList<Endpoint>>();
        var callbackCalls = 0;
        Func<Cursor, PageDirection, int, string> Builder(HttpContext context) =>
            context.PageUrl(RouteName, (cursor, direction, limit) =>
            {
                callbackCalls++;
                return new()
                {
                    [direction == PageDirection.Next ? "after" : "before"] = cursor.Token,
                    ["limit"] = limit
                };
            }, route =>
            {
                candidates.Add(route.Candidates);
                route.RouteValues["host"] = route.HttpContext.Request.Host.Host;
                return route.Candidates[0];
            });
        var first = Builder(CreateContext(services));
        first(new Cursor("next"), PageDirection.Next, 2).Should().Contain("after=next");
        var readsAfterWarmup = dataSource.EndpointReads;

        first(new Cursor("previous"), PageDirection.Previous, 3)
            .Should().Contain("before=previous").And.Contain("limit=3");
        var otherContext = CreateContext(services);
        otherContext.Request.Host = new HostString("other.example.com");
        var otherUrl = Builder(otherContext)(new Cursor("later"), PageDirection.Next, 4);

        otherUrl.Should().StartWith("https://other.example.com/gateway/widgets?")
            .And.Contain("after=later").And.Contain("limit=4").And.Contain("host=other.example.com");
        candidates.Should().HaveCount(3).And.OnlyContain(candidate => ReferenceEquals(candidate, candidates[0]));
        callbackCalls.Should().Be(3);
        dataSource.EndpointReads.Should().Be(readsAfterWarmup);
    }

    [Fact]
    public void PageUrl_Cache_DifferentRouteNames_ReusesEndpointIndex()
    {
        const string otherName = "Other_List";
        using var dataSource = new MutableEndpointDataSource(
            BuildEndpoint("widgets"), BuildEndpoint("other", routeName: otherName));
        using var services = CreateServices(dataSource);
        var context = CreateContext(services);
        context.PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2)
            .Should().EndWith("/widgets");
        var readsAfterWarmup = dataSource.EndpointReads;

        context.PageUrl(otherName, (_, _) => new())(new Cursor("next"), 2)
            .Should().EndWith("/other");

        dataSource.EndpointReads.Should().Be(readsAfterWarmup);
    }

    [Fact]
    public void PageUrl_Cache_SharedRoute_ReusesPatternValidation()
    {
        var firstPolicy = new CountingRouteConstraint();
        var secondPolicy = new CountingRouteConstraint();
        RouteEndpoint Target(CountingRouteConstraint policy) =>
            new(_ => Task.CompletedTask,
                RoutePatternFactory.Parse("widgets/{id}", defaults: null, parameterPolicies: new { id = policy }), 0,
                new EndpointMetadataCollection(new RouteNameMetadata(RouteName)), RouteName);
        using var services = CreateServices(Target(firstPolicy), Target(secondPolicy));
        var context = CreateContext(services);
        var builder = context.PageUrl(RouteName, (_, _) => new() { ["id"] = 123 }, route => route.Candidates[0]);
        builder(new Cursor("next"), 2).Should().EndWith("/widgets/123");
        var comparisonsAfterWarmup = firstPolicy.Comparisons + secondPolicy.Comparisons;
        comparisonsAfterWarmup.Should().BeGreaterThan(0);

        for (var index = 0; index < 5; index++)
            builder(new Cursor("next"), 2).Should().EndWith("/widgets/123");

        (firstPolicy.Comparisons + secondPolicy.Comparisons).Should().Be(comparisonsAfterWarmup);
    }

    [Fact]
    public void PageUrl_Cache_EndpointReplacement_InvalidatesExistingBuilders()
    {
        var original = BuildEndpoint("original");
        var replacement = BuildEndpoint("replacement");
        using var dataSource = new MutableEndpointDataSource(original);
        using var services = CreateServices(dataSource);
        var seen = new List<IReadOnlyList<Endpoint>>();
        var builder = CreateContext(services).PageUrl(RouteName, (_, _) => new(), route =>
        {
            seen.Add(route.Candidates);
            return route.Candidates[0];
        });
        builder(new Cursor("next"), 2).Should().EndWith("/original");

        dataSource.Replace(replacement);

        builder(new Cursor("next"), 2).Should().EndWith("/replacement");
        var readsAfterReplacement = dataSource.EndpointReads;
        builder(new Cursor("later"), 2).Should().EndWith("/replacement");
        seen[0].Should().Equal([original]);
        seen[1].Should().Equal([replacement]);
        seen[1].Should().BeSameAs(seen[2]).And.NotBeSameAs(seen[0]);
        dataSource.EndpointReads.Should().Be(readsAfterReplacement);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageUrl_Cache_EndpointRemovedOrSuppressed_InvalidatesAndRecovers(bool suppressed)
    {
        using var dataSource = new MutableEndpointDataSource(BuildEndpoint("widgets"));
        using var services = CreateServices(dataSource);
        var builder = CreateContext(services).PageUrl(RouteName, (_, _) => new());
        builder(new Cursor("next"), 2).Should().EndWith("/widgets");

        dataSource.Replace(suppressed ? [BuildEndpoint("widgets", suppress: true)] : []);
        Action removed = () => builder(new Cursor("next"), 2);
        removed.Should().Throw<InvalidOperationException>().WithMessage("*no registered*Widgets_List*");

        dataSource.Replace(BuildEndpoint("restored"));
        builder(new Cursor("next"), 2).Should().EndWith("/restored");
    }

    [Fact]
    public void PageUrl_Cache_ChangedSharedRoute_RevalidatesCompatibility()
    {
        var original = BuildEndpoint("widgets");
        using var dataSource = new MutableEndpointDataSource(original);
        using var services = CreateServices(dataSource);
        var context = CreateContext(services);
        context.SetEndpoint(original);
        var builder = context.PageUrl(RouteName, (_, _) => new());
        builder(new Cursor("next"), 2).Should().EndWith("/widgets");

        dataSource.Replace(original, BuildEndpoint("different"));
        Action ambiguous = () => builder(new Cursor("next"), 2);
        ambiguous.Should().Throw<InvalidOperationException>().WithMessage("*different*templates*");

        dataSource.Replace(original, BuildEndpoint("widgets"));
        builder(new Cursor("next"), 2).Should().EndWith("/widgets");
    }

    [Fact]
    public void PageUrl_Cache_DifferentDataSources_RemainIsolated()
    {
        using var first = CreateServices(BuildEndpoint("first"));
        using var second = CreateServices(BuildEndpoint("second"));

        CreateContext(first).PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2)
            .Should().EndWith("/first");
        CreateContext(second).PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2)
            .Should().EndWith("/second");
        CreateContext(first).PageUrl(RouteName, (_, _) => new())(new Cursor("next"), 2)
            .Should().EndWith("/first");
    }

    [Fact]
    public void PageUrl_Cache_ChangeDuringEndpointRead_DoesNotPublishStaleCandidates()
    {
        var replacement = BuildEndpoint("replacement");
        using var dataSource = new MutableEndpointDataSource(BuildEndpoint("original"));
        using var services = CreateServices(dataSource);
        dataSource.OnNextRead = () => dataSource.Replace(replacement);
        var builder = CreateContext(services).PageUrl(RouteName, (_, _) => new(), route =>
        {
            route.Candidates.Should().Equal([replacement]);
            return route.Candidates[0];
        });

        builder(new Cursor("next"), 2).Should().EndWith("/replacement");
    }

    [Fact]
    public async Task PageUrl_Cache_ConcurrentFirstBuilders_IndexEndpointsOnce()
    {
        using var dataSource = new MutableEndpointDataSource(BuildEndpoint("widgets"));
        using var services = CreateServices(dataSource);
        services.GetRequiredService<LinkGenerator>()
            .GetUriByRouteValues(CreateContext(services), RouteName, new RouteValueDictionary())
            .Should().EndWith("/widgets");
        var readsAfterLinkGeneratorWarmup = dataSource.EndpointReads;
        var candidates = new ConcurrentBag<IReadOnlyList<Endpoint>>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, 16).Select(async index =>
        {
            await start.Task;
            return CreateContext(services).PageUrl(RouteName,
                (cursor, _) => new() { ["cursor"] = cursor.Token }, route =>
                {
                    candidates.Add(route.Candidates);
                    return route.Candidates[0];
                })(new Cursor($"next-{index}"), 2);
        }).ToArray();

        start.SetResult();
        var urls = await Task.WhenAll(calls);

        urls.Should().HaveCount(16).And.OnlyContain(url => url.Contains("/widgets?cursor=next-", StringComparison.Ordinal));
        candidates.Should().HaveCount(16).And.OnlyContain(candidate => ReferenceEquals(candidate, candidates.First()));
        dataSource.EndpointReads.Should().Be(readsAfterLinkGeneratorWarmup + 1);
    }

    private sealed class CountingRouteConstraint : IRouteConstraint
    {
        public int Comparisons { get; private set; }

        public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
            RouteValueDictionary values, RouteDirection routeDirection) => true;

        public override bool Equals(object? obj)
        {
            Comparisons++;
            return obj is CountingRouteConstraint;
        }

        public override int GetHashCode() => typeof(CountingRouteConstraint).GetHashCode();
    }

    private sealed class MutableEndpointDataSource(params Endpoint[] endpoints) : EndpointDataSource, IDisposable
    {
        private IReadOnlyList<Endpoint> _endpoints = endpoints;
        private CancellationTokenSource _changes = new();
        private int _endpointReads;

        public int EndpointReads => Volatile.Read(ref _endpointReads);
        public Action? OnNextRead { get; set; }

        public override IReadOnlyList<Endpoint> Endpoints
        {
            get
            {
                Interlocked.Increment(ref _endpointReads);
                var snapshot = _endpoints;
                var onRead = OnNextRead;
                OnNextRead = null;
                onRead?.Invoke();
                return snapshot;
            }
        }

        public override IChangeToken GetChangeToken() => new CancellationChangeToken(_changes.Token);

        public void Replace(params Endpoint[] replacement)
        {
            _endpoints = replacement;
            var previous = _changes;
            _changes = new();
            previous.Cancel();
            previous.Dispose();
        }

        public void Dispose() => _changes.Dispose();
    }

    private static RouteEndpoint BuildEndpoint(string pattern, bool suppress = false, string routeName = RouteName) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0,
            new EndpointMetadataCollection(suppress
                ? [new RouteNameMetadata(routeName), new SuppressLinkGenerationMetadata()]
                : [new RouteNameMetadata(routeName)]), pattern);

    private static ServiceProvider CreateServices(params Endpoint[] endpoints) => CreateServices(endpoints, null);

    private static ServiceProvider CreateServices(Endpoint[] endpoints, Action<TrellisAspOptions>? configure) =>
        CreateServices(new DefaultEndpointDataSource(endpoints), configure);

    private static ServiceProvider CreateServices(EndpointDataSource dataSource, Action<TrellisAspOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton(dataSource);
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
