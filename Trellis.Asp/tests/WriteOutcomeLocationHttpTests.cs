namespace Trellis.Asp.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public sealed class WriteOutcomeLocationHttpTests
{
    public static TheoryData<bool, string, string?> CreatedFallbackCases
    {
        get
        {
            var cases = new TheoryData<bool, string, string?>();
            bool[] hosts = [false, true];
            string[] kinds = ["literal", "selector", "route", "action", "with-location"];
            string?[] locations = [null, "", " \t "];
            foreach (var mvc in hosts)
                foreach (var kind in kinds)
                    foreach (var location in locations)
                        cases.Add(mvc, kind, location);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(CreatedFallbackCases))]
    public async Task Put_CreatedWithAbsentLocation_UsesFollowableBuilderLocation(
        bool mvc, string kind, string? location)
    {
        var path = mvc ? "/write-outcome/mvc/items/42" : "/write-outcome/minimal/items/42";
        using var host = CreateHost(options =>
        {
            options.WithETag(_ => "builder")
                .WithLastModified(_ => new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
            switch (kind)
            {
                case "literal":
                    options.Created(path);
                    break;
                case "selector":
                    options.Created(id => FormattableString.Invariant(
                        $"/write-outcome/{(mvc ? "mvc" : "minimal")}/items/{id}"));
                    break;
                case "route":
                    options.CreatedAtRoute(mvc ? "WriteOutcome_Mvc_Get" : "WriteOutcome_Minimal_Get", id => id);
                    break;
                case "action":
                    options.CreatedAtAction(nameof(WriteOutcomeLocationController.Get),
                        id => new RouteValueDictionary { ["id"] = id }, "WriteOutcomeLocation");
                    break;
                case "with-location":
                    options.WithLocation(mvc ? "WriteOutcome_Mvc_Get" : "WriteOutcome_Minimal_Get", id => id);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }, location);
        using var client = host.GetTestClient();
        var ct = TestContext.Current.CancellationToken;

        using var response = await client.PutAsync(path, null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var expected = kind == "action" ? "/write-outcome/mvc/items/42" : path;
        response.Headers.Location!.OriginalString.Should().Be(expected);
        response.Headers.ETag!.Tag.Should().Be("\"outcome\"");
        response.Content.Headers.LastModified.Should().Be(
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        (await response.Content.ReadFromJsonAsync<CreationBody>(ct)).Should().Be(new CreationBody(142));
        using var followed = await client.GetAsync(response.Headers.Location, ct);
        followed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await followed.Content.ReadFromJsonAsync<CreationBody>(ct)).Should().Be(new CreationBody(42));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, " \t ")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, " \t ")]
    public async Task Put_CreatedWithoutLocationConfiguration_OmitsLocation(bool mvc, string? location)
    {
        using var host = CreateHost(_ => { }, location);
        using var client = host.GetTestClient();
        var path = mvc ? "/write-outcome/mvc/items/42" : "/write-outcome/minimal/items/42";

        using var response = await client.PutAsync(path, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Contains("Location").Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_UnresolvedNamedRoute_ReturnsProblemDetails(bool mvc)
    {
        using var host = CreateHost(options => options.CreatedAtRoute("Missing", id => id), null);
        using var client = host.GetTestClient();
        var path = mvc ? "/write-outcome/mvc/items/42" : "/write-outcome/minimal/items/42";

        using var response = await client.PutAsync(path, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
            TestContext.Current.CancellationToken);
        body.GetProperty("code").GetString().Should().Be(FaultCodes.ResponseLocationUnresolved);
        response.Headers.Contains("Location").Should().BeFalse();
    }

    private static IHost CreateHost(Action<HttpResponseOptionsBuilder<int>> configure, string? location) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddTrellisAsp();
                    services.AddSingleton(new CreationState(location, configure));
                    services.AddControllers()
                        .AddApplicationPart(typeof(WriteOutcomeLocationController).Assembly)
                        .ConfigureApplicationPartManager(manager =>
                        {
                            manager.FeatureProviders.Clear();
                            manager.FeatureProviders.Add(new LocationControllerFeatureProvider());
                        });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapGet("/write-outcome/minimal/items/{id:int}",
                            (int id) => new CreationBody(id)).WithName("WriteOutcome_Minimal_Get");
                        endpoints.MapPut("/write-outcome/minimal/items/{id:int}",
                            (int id, CreationState state) => state.Create(id)
                                .ToHttpResponse(value => new CreationBody(value + 100), state.Configure));
                    });
                }))
            .Start();

    private sealed class LocationControllerFeatureProvider : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) =>
            typeInfo.AsType() == typeof(WriteOutcomeLocationController);
    }

    public sealed record CreationBody(int Id);

    public sealed record CreationState(string? Location, Action<HttpResponseOptionsBuilder<int>> Configure)
    {
        internal Result<WriteOutcome<int>> Create(int id) =>
            Result.Ok(WriteOutcome.Created(id, Location, RepresentationMetadata.WithStrongETag("outcome")));
    }
}

[ApiController]
[Route("write-outcome/mvc")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC test fixture actions.")]
public sealed class WriteOutcomeLocationController : ControllerBase
{
    [HttpGet("items/{id:int}", Name = "WriteOutcome_Mvc_Get")]
    public ActionResult<WriteOutcomeLocationHttpTests.CreationBody> Get(int id) =>
        new WriteOutcomeLocationHttpTests.CreationBody(id);

    [HttpPut("items/{id:int}")]
    public ActionResult<WriteOutcomeLocationHttpTests.CreationBody> Put(
        int id, [FromServices] WriteOutcomeLocationHttpTests.CreationState state) =>
        state.Create(id)
            .ToHttpResponse(value => new WriteOutcomeLocationHttpTests.CreationBody(value + 100), state.Configure)
            .AsActionResult<WriteOutcomeLocationHttpTests.CreationBody>();
}
