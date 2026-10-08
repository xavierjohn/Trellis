namespace PaginationConsumer.Tests;

using global::Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.ApiVersioning;
using Trellis.ServiceDefaults;

public sealed class PaginationPolicyTests
{
    private const string RouteName = "Widgets_List";
    private static readonly ApiVersion V1 = new(1, 0);
    private static readonly ApiVersion V2 = new(2, 0);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PageUrl_OrdinaryCall_DirectAndBuilderConfiguration_PreservesRequestedVersionAndOtherOptions(
        bool useBuilder, bool configurePolicyFirst)
    {
        var endpoint = BuildEndpoint();
        var services = CreateServices(endpoint);
        Action<TrellisAspOptions> language = options => options.ProblemContentLanguage = "en";
        Action<TrellisAspOptions> policy = options => options.UseVersionedPageUrls();
        var first = configurePolicyFirst ? policy : language;
        var second = configurePolicyFirst ? language : policy;
        if (useBuilder)
            services.AddTrellis(builder => builder.UseAsp(first).UseAsp().UseAsp(second));
        else
            services.AddTrellisAsp(first).AddTrellisAsp().AddTrellisAsp(second);
        using var provider = services.BuildServiceProvider();
        var context = CreateContext(provider, endpoint);
        var shared = new RouteValueDictionary { ["cursor"] = "next" };

        context.PageUrl(RouteName, (_, _) => shared)(new Cursor("next"), 2)
            .Should().Contain("api-version=2.0");
        provider.GetRequiredService<TrellisAspOptions>().ProblemContentLanguage.Should().Be("en");
        shared.Should().ContainSingle().Which.Key.Should().Be("cursor");
    }

    [Fact]
    public void PageUrl_ExplicitPin_OverridesHostPolicyWithoutRegistration()
    {
        var endpoint = BuildEndpoint();
        var services = CreateServices(endpoint);
        services.AddTrellisAsp(options =>
            options.PageUrlRouteResolver = _ => throw new InvalidOperationException("Host policy must not run."));
        using var provider = services.BuildServiceProvider();
        var context = CreateContext(provider, endpoint);
        var shared = new RouteValueDictionary { ["api-version"] = "2.0" };

        context.PageUrl(RouteName, V1, (_, _) => shared)(new Cursor("next"), 2)
            .Should().Contain("api-version=1.0");
        shared["api-version"].Should().Be("2.0");
    }

    [Fact]
    public void UseVersionedPageUrls_RepeatedCalls_PreservesUnrelatedConfiguration()
    {
        var options = new TrellisAspOptions { ProblemContentLanguage = "en" };

        options.UseVersionedPageUrls().UseVersionedPageUrls().Should().BeSameAs(options);

        options.PageUrlRouteResolver.Should().NotBeNull();
        options.ProblemContentLanguage.Should().Be("en");
    }

    [Fact]
    public void UseVersionedPageUrls_NullOptions_Throws()
    {
        TrellisAspOptions options = null!;
        Action action = () => options.UseVersionedPageUrls();

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(options));
    }

    private static ServiceCollection CreateServices(Endpoint endpoint)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(endpoint));
        return services;
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, Endpoint endpoint)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");
        context.SetEndpoint(endpoint);
        context.Features.Set<IApiVersioningFeature>(new ApiVersioningFeature(context) { RequestedApiVersion = V2 });
        return context;
    }

    private static RouteEndpoint BuildEndpoint() =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse("widgets"), 0,
            new EndpointMetadataCollection(new RouteNameMetadata(RouteName),
                new ApiVersionMetadata(new ApiVersionModel([V1, V2], [V1, V2], [], [], []), ApiVersionModel.Empty)), RouteName);
}
