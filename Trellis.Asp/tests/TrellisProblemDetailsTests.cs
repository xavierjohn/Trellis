namespace Trellis.Asp.Tests;

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// Verifies <c>AddTrellisProblemDetails()</c> applies the canonical Trellis
/// ProblemDetails enrichment (trace id, 500 detail rewrite, 405 Allow projection)
/// and that <c>UseTrellisProblemDetails()</c> wires the standard
/// <c>UseExceptionHandler</c> + <c>UseStatusCodePages</c> pipeline so ASP.NET
/// short-circuits (404 / 405 / 415) become ProblemDetails bodies.
/// </summary>
public sealed class TrellisProblemDetailsTests
{
    // ---- helpers ---------------------------------------------------------

    private static ProblemDetailsContext MakeContext(int status, string? allowHeader = null)
    {
        var http = new DefaultHttpContext();
        if (allowHeader is not null)
            http.Response.Headers["Allow"] = allowHeader;
        return new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = status },
        };
    }

    private static Action<ProblemDetailsContext> ResolveCustomizer(IServiceCollection services)
    {
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value;
        return options.CustomizeProblemDetails ?? (_ => { });
    }

    private static IHost CreatePipelineHost(Action<IServiceCollection>? configureServices = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddTrellisProblemDetails();
                    configureServices?.Invoke(s);
                })
                .Configure(app =>
                {
                    app.UseTrellisProblemDetails();
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/ok", () => Results.Ok(new { ok = true }));
                        e.MapGet("/boom", () =>
                        {
                            throw new InvalidOperationException("kaboom-secret");
                        });
                    });
                }))
            .Start();

    // ---- registration / null guards --------------------------------------

    [Fact]
    public void AddTrellisProblemDetails_null_services_throws()
    {
        Action act = () => ((IServiceCollection)null!).AddTrellisProblemDetails();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseTrellisProblemDetails_null_app_throws()
    {
        Action act = () => ((IApplicationBuilder)null!).UseTrellisProblemDetails();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrellisProblemDetails_registers_IProblemDetailsService()
    {
        var services = new ServiceCollection();

        services.AddTrellisProblemDetails();

        using var sp = services.BuildServiceProvider();
        sp.GetService<IProblemDetailsService>().Should().NotBeNull();
    }

    // ---- trace id projection ---------------------------------------------

    [Fact]
    public void Trace_id_uses_Activity_id_when_available()
    {
        using var activity = new Activity("trellis-test").Start();
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(400);

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions["traceId"].Should().Be(activity.Id);
    }

    [Fact]
    public void Trace_id_falls_back_to_HttpContext_TraceIdentifier_when_no_Activity()
    {
        // Activity.Current is an AsyncLocal that leaks across tests when written to
        // without restoration; xUnit's default parallel scheduling can then surface
        // flaky trace-id assertions in unrelated tests. Capture-and-restore so this
        // test is self-contained regardless of what the host process set Current to.
        var originalActivity = Activity.Current;
        try
        {
            Activity.Current = null;
            var services = new ServiceCollection();
            services.AddTrellisProblemDetails();
            var ctx = MakeContext(400);
            ctx.HttpContext.TraceIdentifier = "test-trace-id-xyz";

            ResolveCustomizer(services).Invoke(ctx);

            ctx.ProblemDetails.Extensions["traceId"].Should().Be("test-trace-id-xyz");
        }
        finally
        {
            Activity.Current = originalActivity;
        }
    }

    // ---- 500 detail rewrite ----------------------------------------------

    [Fact]
    public void Rewrites_detail_for_500_responses_to_support_friendly_message()
    {
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(StatusCodes.Status500InternalServerError);
        ctx.ProblemDetails.Detail = "Some raw stack-trace fragment that leaks internals";

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Detail.Should().NotContain("stack-trace");
        ctx.ProblemDetails.Detail.Should().Contain("trace id",
            "the rewritten detail nudges the user to file a support ticket with the trace id");
    }

    [Fact]
    public void Leaves_detail_unchanged_for_non_500_responses()
    {
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(StatusCodes.Status400BadRequest);
        ctx.ProblemDetails.Detail = "Invalid input on field X";

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Detail.Should().Be("Invalid input on field X");
    }

    // ---- 405 Allow header projection -------------------------------------

    [Theory]
    [InlineData("GET", new[] { "GET" })]
    [InlineData("GET,HEAD", new[] { "GET", "HEAD" })]
    [InlineData("GET, HEAD", new[] { "GET", "HEAD" })]
    [InlineData("GET, HEAD, POST", new[] { "GET", "HEAD", "POST" })]
    [InlineData("  GET  ,  POST  ", new[] { "GET", "POST" })]
    public void Projects_Allow_header_to_allow_extension_array_on_405(string header, string[] expected)
    {
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(StatusCodes.Status405MethodNotAllowed, header);

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions.Should().ContainKey("allow");
        ctx.ProblemDetails.Extensions["allow"].Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Does_not_add_allow_extension_when_405_has_no_Allow_header()
    {
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(StatusCodes.Status405MethodNotAllowed);

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions.Should().NotContainKey("allow");
    }

    [Fact]
    public void Does_not_add_allow_extension_for_non_405_responses_even_if_header_present()
    {
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(StatusCodes.Status404NotFound, "GET, POST");

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions.Should().NotContainKey("allow");
    }

    // ---- composition with consumer's CustomizeProblemDetails -------------

    [Fact]
    public void Composes_with_parameterless_prior_AddProblemDetails()
    {
        var services = new ServiceCollection();
        services.AddProblemDetails();
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(500);

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions["traceId"].Should().NotBeNull();
    }

    [Fact]
    public void Composes_with_prior_AddProblemDetails_callback_preserving_consumer_extensions()
    {
        var services = new ServiceCollection();
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["consumer-flag"] = "yes");
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(500);

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions["consumer-flag"].Should().Be("yes",
            "the prior consumer customization must survive AddTrellisProblemDetails");
        ctx.ProblemDetails.Extensions["traceId"].Should().NotBeNull(
            "Trellis defaults must still apply even when a prior customization exists");
    }

    [Fact]
    public void Composes_with_posterior_AddProblemDetails_callback_preserving_consumer_extensions()
    {
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["consumer-flag"] = "yes");
        var ctx = MakeContext(500);

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions["consumer-flag"].Should().Be("yes",
            "a posterior consumer customization must compose, not erase, Trellis defaults");
        ctx.ProblemDetails.Extensions["traceId"].Should().NotBeNull();
    }

    [Fact]
    public void Consumer_customization_runs_after_Trellis_so_consumer_wins_collisions()
    {
        // User chose: Trellis runs FIRST, consumer customization runs LAST so the
        // consumer can override Trellis defaults (e.g. trace-id format, support message).
        var services = new ServiceCollection();
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = "consumer-override");
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(500);

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions["traceId"].Should().Be("consumer-override");
    }

    [Fact]
    public void Consumer_customization_can_override_500_detail_rewrite()
    {
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Detail = "Application-specific 500 message");
        var ctx = MakeContext(500);

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Detail.Should().Be("Application-specific 500 message");
    }

    [Fact]
    public void Idempotent_when_called_multiple_times()
    {
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        services.AddTrellisProblemDetails();
        services.AddTrellisProblemDetails();
        var ctx = MakeContext(StatusCodes.Status405MethodNotAllowed, "GET, POST");

        ResolveCustomizer(services).Invoke(ctx);

        ctx.ProblemDetails.Extensions["traceId"].Should().NotBeNull();
        string[] expected = ["GET", "POST"];
        ctx.ProblemDetails.Extensions["allow"].Should().BeEquivalentTo(expected);
    }

    // ---- pipeline integration --------------------------------------------

    [Fact]
    public async Task Pipeline_returns_500_problem_details_with_trace_id_when_endpoint_throws()
    {
        using var host = CreatePipelineHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/boom", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        doc.RootElement.GetProperty("detail").GetString().Should().NotContain("kaboom-secret",
            "raw exception detail must not leak to clients");
        doc.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Pipeline_returns_405_problem_details_with_allow_extension_when_method_wrong()
    {
        using var host = CreatePipelineHost();
        using var client = host.GetTestClient();

        var resp = await client.PostAsync("/ok", content: null, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(405);
        doc.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.TryGetProperty("allow", out var allow).Should().BeTrue(
            "the 405 body should echo the Allow header as a structured array");
        allow.ValueKind.Should().Be(JsonValueKind.Array);
        allow.EnumerateArray().Select(e => e.GetString()).Should().Contain("GET");
    }

    [Fact]
    public async Task Pipeline_returns_404_problem_details_with_trace_id_when_route_missing()
    {
        using var host = CreatePipelineHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/does-not-exist", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        doc.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }
}