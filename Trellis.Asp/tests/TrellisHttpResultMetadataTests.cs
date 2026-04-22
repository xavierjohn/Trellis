namespace Trellis.Asp.Tests;

using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Outcome coverage for the OpenAPI / ApiExplorer metadata surface of the Trellis
/// IResult types: the <see cref="IStatusCodeHttpResult"/>, <see cref="IValueHttpResult"/>,
/// <see cref="IContentTypeHttpResult"/> hints consumed by the framework's metadata pipeline,
/// and the <see cref="IEndpointMetadataProvider"/> contract that supplies
/// <c>ProducesResponseTypeMetadata</c> to the endpoint builder.
/// </summary>
public sealed class TrellisHttpResultMetadataTests
{
    private sealed record Thing(int Id, string Name);

    private sealed record ThingBody(int Id);

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class NoopPds : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext c) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext c) => false;
#pragma warning restore CA1822
    }

    private static MethodInfo DummyMethod() =>
        typeof(TrellisHttpResultMetadataTests).GetMethod(nameof(DummyMethod), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static RouteEndpointBuilder NewEndpointBuilder() =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);

    [Fact]
    public void StatusCode_hint_is_201_when_Created_location_configured()
    {
        var r = Result.Ok(new Thing(1, "x"));
        var http = r.ToHttpResponse(t => new ThingBody(t.Id), o => o.Created("/things/1"));

        ((IStatusCodeHttpResult)http).StatusCode.Should().Be(201);
    }

    [Fact]
    public void StatusCode_hint_is_200_when_no_location_configured()
    {
        var http = Result.Ok(new Thing(1, "x")).ToHttpResponse(t => new ThingBody(t.Id));

        ((IStatusCodeHttpResult)http).StatusCode.Should().Be(200);
    }

    [Fact]
    public void Value_hint_returns_projected_body_on_success()
    {
        var http = Result.Ok(new Thing(5, "y")).ToHttpResponse(t => new ThingBody(t.Id));

        var val = ((IValueHttpResult)http).Value;
        val.Should().BeOfType<ThingBody>().Which.Id.Should().Be(5);

        var typed = ((IValueHttpResult<ThingBody>)http).Value;
        typed.Should().NotBeNull();
        typed!.Id.Should().Be(5);
    }

    [Fact]
    public void Value_hint_returns_domain_value_when_no_body_projector()
    {
        var thing = new Thing(9, "z");
        var http = Result.Ok(thing).ToHttpResponse();

        ((IValueHttpResult)http).Value.Should().BeSameAs(thing);
    }

    [Fact]
    public void Value_hint_returns_null_on_failure()
    {
        var http = Result.Fail<Thing>(new Error.NotFound(new ResourceRef("Thing", "1")))
            .ToHttpResponse(t => new ThingBody(t.Id));

        ((IValueHttpResult)http).Value.Should().BeNull();
        ((IValueHttpResult<ThingBody>)http).Value.Should().BeNull();
    }

    [Fact]
    public void ContentType_hint_is_application_json()
    {
        var http = Result.Ok(new Thing(1, "x")).ToHttpResponse(t => new ThingBody(t.Id));
        ((IContentTypeHttpResult)http).ContentType.Should().Be("application/json");
    }

    [Fact]
    public void PopulateMetadata_adds_ProducesResponseType_entries_for_Result_body()
    {
        // Drive the IEndpointMetadataProvider contract the same way the ASP.NET endpoint
        // pipeline does: build an endpoint, call the provider, inspect accumulated metadata.
        var builder = NewEndpointBuilder();

        TrellisHttpResult<Thing, ThingBody>.PopulateMetadata(DummyMethod(), builder);

        var statuses = builder.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Select(m => m.StatusCode).ToHashSet();
        statuses.Should().Contain([200, 201, 206, 304, 400, 404, 412, 500]);
    }

    [Fact]
    public void PopulateMetadata_for_WriteOutcome_declares_201_204_202_and_problem_entries()
    {
        var builder = NewEndpointBuilder();

        TrellisWriteOutcomeResult<Thing, ThingBody>.PopulateMetadata(DummyMethod(), builder);

        var statuses = builder.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Select(m => m.StatusCode).ToHashSet();
        statuses.Should().Contain([200, 201, 204, 202, 400, 412]);
    }

    [Fact]
    public void PopulateMetadata_throws_on_null_builder_for_Result_type()
        => FluentActions.Invoking(() =>
                TrellisHttpResult<Thing, ThingBody>.PopulateMetadata(DummyMethod(), null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void PopulateMetadata_throws_on_null_builder_for_WriteOutcome_type()
        => FluentActions.Invoking(() =>
                TrellisWriteOutcomeResult<Thing, ThingBody>.PopulateMetadata(DummyMethod(), null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void WriteOutcome_StatusCode_hint_is_200()
    {
        var outcome = new WriteOutcome<Thing>.UpdatedNoContent();
        var http = Result.Ok<WriteOutcome<Thing>>(outcome).ToHttpResponse();

        ((IStatusCodeHttpResult)http).StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task CreatedAtRoute_with_registered_route_emits_201_with_Location_header()
    {
        // Exercise the successful LinkGenerator.GetUriByName / GetPathByName path that the
        // "unknown-route returns 500" test skips.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        var source = new TestEndpointDataSource();
        source.AddNamedRoute("GetThing", "/things/{id}");
        services.AddSingleton<EndpointDataSource>(source);
        var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new MemoryStream();

        var r = Result.Ok(new Thing(42, "t"));
        await r.ToHttpResponse(t => new ThingBody(t.Id),
                o => o.CreatedAtRoute("GetThing", t => new RouteValueDictionary(new { id = t.Id })))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Contain("/things/42");
    }

    // Minimal custom EndpointDataSource exposing one named endpoint so LinkGenerator can resolve it.
    private sealed class TestEndpointDataSource : EndpointDataSource
    {
        private readonly List<Endpoint> _endpoints = new();
        public void AddNamedRoute(string name, string pattern)
        {
            var eb = new RouteEndpointBuilder(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse(pattern),
                order: 0);
            eb.Metadata.Add(new RouteNameMetadata(name));
            eb.Metadata.Add(new EndpointNameMetadata(name));
            _endpoints.Add(eb.Build());
        }

        public override IReadOnlyList<Endpoint> Endpoints => _endpoints;

        public override Microsoft.Extensions.Primitives.IChangeToken GetChangeToken()
            => new Microsoft.Extensions.Primitives.CancellationChangeToken(System.Threading.CancellationToken.None);
    }
}
