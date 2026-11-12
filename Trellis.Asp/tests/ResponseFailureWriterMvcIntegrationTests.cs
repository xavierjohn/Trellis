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
using Microsoft.OpenApi;
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
    private static readonly (string Name, string Version)[] OpenApiDocuments = [("v1", "1.0"), ("v2", "2.0")];

    private static IHost CreateHost(bool addVersionedOpenApi = false)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddProblemDetails();
                    s.AddControllers().AddApplicationPart(typeof(DiagController).Assembly);

                    if (addVersionedOpenApi)
                    {
                        var apiv = s.AddApiVersioning(o =>
                        {
                            o.AssumeDefaultVersionWhenUnspecified = true;
                            o.DefaultApiVersion = new global::Asp.Versioning.ApiVersion(1, 0);
                        });
                        apiv.AddMvc();
                        apiv.AddApiExplorer(options => options.GroupNameFormat = "'v'VVV");
                        foreach (var (name, version) in OpenApiDocuments)
                        {
                            s.AddOpenApi(name, options =>
                            {
                                options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
                                options.AddDocumentTransformer((document, _, _) =>
                                {
                                    document.Info.Version = version;
                                    return Task.CompletedTask;
                                });
                            });
                        }
                    }
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapControllers();
                        if (addVersionedOpenApi)
                            e.MapOpenApi();
                    });
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

        body.RootElement.GetProperty("code").GetString().Should().Be("error.unspecified");
        body.RootElement.GetProperty("kind").GetString().Should().Be("unprocessable-content");
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

        body.RootElement.TryGetProperty("ruleViolations", out var rules)
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
        body.RootElement.GetProperty("code").GetString().Should().Be("error.unspecified");
        body.RootElement.GetProperty("kind").GetString().Should().Be("not-found");
    }

    [Fact]
    public async Task UnprocessableContent_with_field_violations_writes_errors_dict_under_full_pipeline()
    {
        using var host = CreateHost(addVersionedOpenApi: true);
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/diag/422-fields", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be((HttpStatusCode)422);
        using var body = await ReadBodyAsync(resp);
        var raw = body.RootElement.GetRawText();

        body.RootElement.TryGetProperty("errors", out var errors)
            .Should().BeTrue($"errors dict missing under MVC + ApiVersioning + OpenApi. body={raw}");
        errors.GetProperty("email").EnumerateArray().Should().HaveCount(2);
    }

    [Theory]
    [InlineData("v1", "/diag/422-fields", "/diag/v2-only")]
    [InlineData("v2", "/diag/v2-only", "/diag/422-fields")]
    public async Task OpenApi_VersionedDocuments_RequestedVersion_ContainsOnlyMatchingOperations(
        string version, string includedPath, string excludedPath)
    {
        using var host = CreateHost(addVersionedOpenApi: true);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync($"/openapi/{version}.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadBodyAsync(response);
        body.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.1.");
        body.RootElement.GetProperty("info").GetProperty("version").GetString().Should().Be($"{version[1..]}.0");
        var paths = body.RootElement.GetProperty("paths");
        paths.TryGetProperty(includedPath, out var operation).Should().BeTrue();
        paths.TryGetProperty(excludedPath, out _).Should().BeFalse();
        operation.GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Should().Contain(parameter => parameter.GetProperty("name").GetString() == "api-version");
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
        return Result.Fail<T>(new Error.InvalidInput(fields))
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
        return Result.Fail<T>(new Error.InvalidInput(default, rules))
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

[ApiController]
[Route("diag")]
[global::Asp.Versioning.ApiVersion("2.0")]
public sealed class DiagV2Controller : ControllerBase
{
    [HttpGet("v2-only")]
    public ActionResult<int> Get() => 2;
}

internal static class TaskEx
{
    public static Task<ActionResult<T>> ToTask<T>(this ActionResult<T> r) => Task.FromResult(r);
}
#pragma warning restore CA1822