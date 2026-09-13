namespace Trellis.Asp.Tests;

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public sealed class LocationRouteResolverTests
{
    [Fact]
    public void WithLocationRouteResolver_NullCallback_Throws()
    {
        var builder = new HttpResponseOptionsBuilder<int>();
        var act = () => builder.WithLocationRouteResolver(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resolver");
    }

    [Fact]
    public async Task WithLocationRouteResolver_Execute_ReceivesDestinationAndClonedResolvedValues()
    {
        var shared = new RouteValueDictionary { ["id"] = 42, ["remove"] = "stale" };
        var calls = 0;
        using var host = CreateHost(options => options
            .CreatedAtRoute("Destination", _ => shared)
            .WithRouteValueResolver("tenant", _ => "legacy")
            .WithLocationRouteResolver(context =>
            {
                context.RouteName.Should().Be("Destination");
                context.ActionName.Should().BeNull();
                context.ControllerName.Should().BeNull();
                context.HttpContext.Request.Path.Value.Should().Be("/create");
                context.RouteValues.Should().NotBeSameAs(shared);
                context.RouteValues["tenant"].Should().Be("legacy");
                context.RouteValues.Remove("remove");
                context.RouteValues["tenant"] = "resolved";
                calls++;
            }));
        using var client = host.GetTestClient();

        using var response = await client.PostAsync("/create", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().Be("/destination/42?tenant=resolved");
        shared.Should().HaveCount(2);
        shared["remove"].Should().Be("stale");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task WithLocationRouteResolver_RepeatedRegistration_LastWins()
    {
        using var host = CreateHost(options => options
            .WithLocationRouteResolver(_ => throw new InvalidOperationException("Replaced resolver ran."))
            .WithLocation("Destination", id => id)
            .WithLocationRouteResolver(context => context.RouteValues["tenant"] = "last"));
        using var client = host.GetTestClient();

        using var response = await client.PostAsync("/create", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Location!.OriginalString.Should().Be("/destination/42?tenant=last");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithLocationRouteResolver_LiteralOrSelectorLocation_DoesNotRun(bool selector)
    {
        using var host = CreateHost(options =>
        {
            options.WithLocationRouteResolver(_ => throw new InvalidOperationException("Resolver must not run."));
            if (selector)
                options.Created(id => $"/destination/{id}");
            else
                options.Created("/destination/42");
        });
        using var client = host.GetTestClient();

        using var response = await client.PostAsync("/create", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().Be("/destination/42");
    }

    [Fact]
    public async Task WithLocationRouteResolver_NullRouteValues_ThrowsBeforeCallback()
    {
        using var host = CreateHost(options => options
            .CreatedAtRoute("Destination", _ => null!)
            .WithLocationRouteResolver(_ => throw new InvalidOperationException("Resolver must not run.")));
        using var client = host.GetTestClient();
        var act = () => client.PostAsync("/create", null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*route-values selector returned null*");
    }

    [Fact]
    public async Task WithLocationRouteResolver_CallbackThrows_PropagatesException()
    {
        using var host = CreateHost(options => options
            .CreatedAtRoute("Destination", id => id)
            .WithLocationRouteResolver(_ => throw new InvalidOperationException("Destination resolution failed.")));
        using var client = host.GetTestClient();
        var act = () => client.PostAsync("/create", null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Destination resolution failed.");
    }

    private static IHost CreateHost(Action<HttpResponseOptionsBuilder<int>> configure) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddTrellisAsp();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/destination/{id:int}", () => Microsoft.AspNetCore.Http.Results.Ok())
                            .WithName("Destination");
                        endpoints.MapPost("/create", () => Result.Ok(42).ToHttpResponse(configure));
                    });
                }))
            .Start();
}
