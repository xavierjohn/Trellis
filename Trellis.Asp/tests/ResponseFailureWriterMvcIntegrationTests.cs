namespace Trellis.Asp.Tests;

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis;

/// <summary>
/// Integration tests that exercise <see cref="ResponseFailureWriter"/> through the full MVC pipeline
/// (controllers + ProblemDetails + optional API versioning + OpenAPI). The writer relies on
/// <c>Results.ValidationProblem</c> / <c>Results.Problem</c>, which serialize via
/// <c>IProblemDetailsService</c>. These tests guarantee the polymorphic <c>HttpValidationProblemDetails</c>
/// payload (and the framework's <c>code</c>/<c>kind</c>/<c>rules</c> extensions) survives that pipeline.
/// </summary>
public sealed class ResponseFailureWriterMvcIntegrationTests
{
    private static IHost CreateHost(bool addApiVersioning = false, bool addOpenApi = false)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddProblemDetails();
                    s.AddControllers().AddApplicationPart(typeof(DiagController).Assembly);

                    if (addApiVersioning)
                    {
                        var apiv = s.AddApiVersioning(o =>
                        {
                            o.AssumeDefaultVersionWhenUnspecified = true;
                            o.DefaultApiVersion = new global::Asp.Versioning.ApiVersion(1, 0);
                        });
                        apiv.AddMvc();
                        apiv.AddApiExplorer();
                        if (addOpenApi)
                            apiv.AddOpenApi();
                    }

                    if (addOpenApi)
                        s.AddOpenApi();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage r)
    {
        var bytes = await r.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        // Caller is responsible for disposing the returned JsonDocument (e.g. `using var body = ...`).
        return JsonDocument.Parse(bytes);
    }

    [Fact]
    public async Task UnprocessableContent_with_field_violations_writes_errors_dict()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/diag/422-fields", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be((HttpStatusCode)422);
        using var body = await ReadBodyAsync(resp);
        var raw = body.RootElement.GetRawText();

        body.RootElement.GetProperty("code").GetString().Should().Be("unprocessable-content");
        body.RootElement.TryGetProperty("errors", out var errors)
            .Should().BeTrue($"errors dict missing. body={raw}");
        errors.GetProperty("email").EnumerateArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task UnprocessableContent_with_rule_violations_writes_rules_extension()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/diag/422-rules", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be((HttpStatusCode)422);
        using var body = await ReadBodyAsync(resp);
        var raw = body.RootElement.GetRawText();

        body.RootElement.TryGetProperty("rules", out var rules)
            .Should().BeTrue($"rules extension missing. body={raw}");
        rules.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task NotFound_writes_code_and_kind_extensions()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/diag/404", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var body = await ReadBodyAsync(resp);
        body.RootElement.GetProperty("code").GetString().Should().Be("not-found");
        body.RootElement.GetProperty("kind").GetString().Should().Be("not-found");
    }

    [Fact]
    public async Task UnprocessableContent_with_field_violations_writes_errors_dict_under_full_pipeline()
    {
        using var host = CreateHost(addApiVersioning: true, addOpenApi: true);
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/diag/422-fields", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be((HttpStatusCode)422);
        using var body = await ReadBodyAsync(resp);
        var raw = body.RootElement.GetRawText();

        body.RootElement.TryGetProperty("errors", out var errors)
            .Should().BeTrue($"errors dict missing under MVC + ApiVersioning + OpenApi. body={raw}");
        errors.GetProperty("email").EnumerateArray().Should().HaveCount(2);
    }
}

[ApiController]
[Route("diag")]
[global::Asp.Versioning.ApiVersion("1.0")]
#pragma warning disable CA1822
public sealed class DiagController : ControllerBase
{
    public record T(int Id);

    [HttpGet("422-fields")]
    public Task<ActionResult<T>> Fields()
    {
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/email"), "format", null, "must be email"),
            new FieldViolation(new InputPointer("/email"), "required", null, "required"));
        return Result.Fail<T>(new Error.UnprocessableContent(fields))
            .ToHttpResponse(t => t)
            .AsActionResult<T>()
            .ToTask();
    }

    [HttpGet("422-rules")]
    public Task<ActionResult<T>> Rules()
    {
        var rules = EquatableArray.Create(
            new RuleViolation("must_have_items",
                EquatableArray.Create(new InputPointer("/items")),
                null, "Order must have items."));
        return Result.Fail<T>(new Error.UnprocessableContent(default, rules))
            .ToHttpResponse(t => t)
            .AsActionResult<T>()
            .ToTask();
    }

    [HttpGet("404")]
    public Task<ActionResult<T>> NotFoundDiag() =>
        Result.Fail<T>(new Error.NotFound(new ResourceRef("Todo", "abc")) { Detail = "Todo abc not found." })
            .ToHttpResponse(t => t)
            .AsActionResult<T>()
            .ToTask();
}

internal static class TaskEx
{
    public static Task<ActionResult<T>> ToTask<T>(this ActionResult<T> r) => Task.FromResult(r);
}
#pragma warning restore CA1822