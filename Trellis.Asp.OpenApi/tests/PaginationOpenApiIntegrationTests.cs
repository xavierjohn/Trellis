namespace Trellis.Asp.OpenApi.Tests;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Trellis.Testing;

public sealed class PaginationOpenApiIntegrationTests
{
    private static readonly string[] DocumentNames = ["v1", "v2"];

    [Theory]
    [InlineData("v1", "/pagination-docs/minimal", OpenApiSpecVersion.OpenApi3_1)]
    [InlineData("v2", "/pagination-docs/minimal", OpenApiSpecVersion.OpenApi3_1)]
    [InlineData("v1", "/pagination-docs/mvc", OpenApiSpecVersion.OpenApi3_1)]
    [InlineData("v2", "/pagination-docs/mvc", OpenApiSpecVersion.OpenApi3_1)]
    [InlineData("v1", "/pagination-docs/minimal", OpenApiSpecVersion.OpenApi3_0)]
    [InlineData("v1", "/pagination-docs/mvc", OpenApiSpecVersion.OpenApi3_0)]
    [InlineData("v1", "/pagination-docs/minimal", null)]
    [InlineData("v1", "/pagination-docs/mvc", null)]
    public async Task WithPagination_CrossHost_NamedDocuments_EmitsOptionalQueryParameters(
        string document, string path, OpenApiSpecVersion? openApiVersion)
    {
        using var host = CreateHost(openApiVersion);
        using var client = host.GetTestClient();
        using var body = await ReadOpenApiAsync(client, document);

        body.RootElement.GetProperty("openapi").GetString().Should().StartWith(openApiVersion switch
        {
            OpenApiSpecVersion.OpenApi3_0 => "3.0.",
            OpenApiSpecVersion.OpenApi3_1 => "3.1.",
            _ => "3.2."
        });
        var parameters = Parameters(body, path);
        parameters.Should().HaveCount(2);
        parameters.Should().OnlyContain(parameter => parameter.GetProperty("in").GetString() == "query");
        parameters.Where(parameter =>
                parameter.TryGetProperty("required", out var required) && required.GetBoolean())
            .Should().BeEmpty();

        var cursor = Parameter(body, path, "cursor").GetProperty("schema");
        cursor.GetProperty("type").GetString().Should().Be("string");
        cursor.GetProperty("minLength").GetInt32().Should().Be(1);
        var limit = Parameter(body, path, "limit");
        var schema = limit.GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("integer");
        schema.GetProperty("format").GetString().Should().Be("int32");
        schema.GetProperty("minimum").GetInt32().Should().Be(1);
        schema.GetProperty("maximum").GetInt32().Should().Be(int.MaxValue);
        schema.GetProperty("default").GetInt32().Should().Be(PageSize.Default);
        limit.GetProperty("description").GetString().Should().Contain("100").And.Contain("clamped");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(" \t\r\n", false)]
    [InlineData("\u0085", false)]
    [InlineData("\u00A0", false)]
    [InlineData("\u3000", false)]
    [InlineData("\uFEFF", true)]
    [InlineData(" opaque ", true)]
    public async Task WithPagination_CursorSchema_Whitespace_MatchesParser(string token, bool valid)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        using var body = await ReadOpenApiAsync(client);
        var schema = Parameter(body, "/pagination-docs/minimal", "cursor").GetProperty("schema");

        if (valid)
            Cursor.TryCreate(token).Should().BeSuccess();
        else
            Cursor.TryCreate(token).Should().BeFailure();

        Regex.IsMatch(token, schema.GetProperty("pattern").GetString()!, RegexOptions.CultureInvariant)
            .Should().Be(valid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddTrellisAsp_RegistrationOrder_NamedDocuments_EmitsPaginationParameters(
        bool registerAspFirst)
    {
        using var host = CreateHost(registerAspFirst: registerAspFirst);
        using var client = host.GetTestClient();

        foreach (var document in DocumentNames)
        {
            using var body = await ReadOpenApiAsync(client, document);
            Parameters(body, "/pagination-docs/minimal").Select(parameter =>
                parameter.GetProperty("name").GetString()).Should().Equal(["cursor", "limit"]);
        }
    }

    [Theory]
    [InlineData("/pagination-docs/minimal")]
    [InlineData("/pagination-docs/mvc")]
    public async Task TransformAsync_NamedDocuments_UnregisteredDocument_LeavesParametersUnchanged(string path)
    {
        using var host = CreateHost(paginationDocument: "v1");
        using var client = host.GetTestClient();
        using var enabled = await ReadOpenApiAsync(client, "v1");
        using var disabled = await ReadOpenApiAsync(client, "v2");

        Parameters(enabled, path).Select(parameter => parameter.GetProperty("name").GetString())
            .Should().Equal(["cursor", "limit"]);
        Parameters(disabled, path).Should().BeEmpty();
    }

    [Theory]
    [InlineData("/pagination-docs/minimal/custom")]
    [InlineData("/pagination-docs/mvc/custom")]
    [InlineData("/pagination-docs/group/override")]
    public async Task WithPagination_Overrides_CustomRejectionSettings_EmitsConfiguredParameters(string path)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        using var body = await ReadOpenApiAsync(client);

        Parameters(body, path).Select(parameter => parameter.GetProperty("name").GetString())
            .Should().Equal(["after", "take"]);
        var limit = Parameter(body, path, "take");
        var schema = limit.GetProperty("schema");
        schema.GetProperty("maximum").GetInt32().Should().Be(25);
        schema.GetProperty("default").GetInt32().Should().Be(10);
        limit.GetProperty("description").GetString().Should().Contain("rejected");
    }

    [Fact]
    public async Task WithPagination_RouteGroup_InheritedMetadata_EmitsConfiguredParameters()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        using var body = await ReadOpenApiAsync(client);

        const string path = "/pagination-docs/group/inherited";
        Parameters(body, path).Select(parameter => parameter.GetProperty("name").GetString())
            .Should().Equal(["continuation", "count"]);
        var limit = Parameter(body, path, "count").GetProperty("schema");
        limit.GetProperty("maximum").GetInt32().Should().Be(75);
        limit.GetProperty("default").GetInt32().Should().Be(15);
    }

    [Theory]
    [InlineData("/pagination-docs/minimal/no-parser")]
    [InlineData("/pagination-docs/mvc/no-parser")]
    public async Task WithPagination_CrossHost_MetadataOnly_DoesNotBindInputs(string path)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            path + "?cursor=&limit=not-an-integer",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("/pagination-docs/minimal/custom", "?after=", ValidationCodes.CursorMalformed, "after")]
    [InlineData("/pagination-docs/mvc/custom", "?after=", ValidationCodes.CursorMalformed, "after")]
    [InlineData("/pagination-docs/minimal/custom", "?take=bad", ValidationCodes.FormatInteger, "take")]
    [InlineData("/pagination-docs/mvc/custom", "?take=bad", ValidationCodes.FormatInteger, "take")]
    [InlineData("/pagination-docs/minimal/custom", "?take=26", ValidationCodes.PageSizeOutOfRange, "take")]
    [InlineData("/pagination-docs/mvc/custom", "?take=26", ValidationCodes.PageSizeOutOfRange, "take")]
    public async Task WithPagination_CrossHost_MalformedQuery_PreservesQueryLocated422(
        string path, string query, string code, string parameter)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(path + query, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var violation = body.RootElement.GetProperty("fieldViolations")
            .EnumerateArray().Should().ContainSingle().Which;
        violation.GetProperty("code").GetString().Should().Be(code);
        violation.GetProperty("location").GetProperty("in").GetString().Should().Be("query");
        violation.GetProperty("location").GetProperty("name").GetString().Should().Be(parameter);
    }

    [Theory]
    [InlineData("/pagination-docs/minimal/capped-default", OpenApiSpecVersion.OpenApi3_1)]
    [InlineData("/pagination-docs/mvc/capped-default", OpenApiSpecVersion.OpenApi3_1)]
    [InlineData("/pagination-docs/minimal/capped-default", OpenApiSpecVersion.OpenApi3_0)]
    [InlineData("/pagination-docs/mvc/capped-default", OpenApiSpecVersion.OpenApi3_0)]
    [InlineData("/pagination-docs/minimal/capped-default", null)]
    [InlineData("/pagination-docs/mvc/capped-default", null)]
    public async Task WithPagination_CrossHost_RejectDefaultAboveCap_OmitsSchemaDefault(
        string path, OpenApiSpecVersion? openApiVersion)
    {
        using var host = CreateHost(openApiVersion);
        using var client = host.GetTestClient();
        using var document = await ReadOpenApiAsync(client);

        var limit = Parameter(document, path, "limit");
        var schema = limit.GetProperty("schema");
        schema.TryGetProperty("default", out _).Should().BeFalse(
            "the requested default exceeds the cap, so sending it explicitly would be rejected");
        schema.GetProperty("maximum").GetInt32().Should().Be(10);
        limit.GetProperty("description").GetString().Should().Contain("50").And.Contain("10");

        var omitted = await ReadSizeAsync(client, path);
        omitted.Requested.Should().Be(50);
        omitted.Applied.Should().Be(10);

        var explicitApplied = await ReadSizeAsync(client, path + "?limit=10");
        explicitApplied.Requested.Should().Be(10);
        explicitApplied.Applied.Should().Be(10);
    }

    [Theory]
    [InlineData("/pagination-docs/minimal/clamp-default", OpenApiSpecVersion.OpenApi3_1)]
    [InlineData("/pagination-docs/mvc/clamp-default", OpenApiSpecVersion.OpenApi3_1)]
    [InlineData("/pagination-docs/minimal/clamp-default", OpenApiSpecVersion.OpenApi3_0)]
    [InlineData("/pagination-docs/mvc/clamp-default", OpenApiSpecVersion.OpenApi3_0)]
    [InlineData("/pagination-docs/minimal/clamp-default", null)]
    [InlineData("/pagination-docs/mvc/clamp-default", null)]
    public async Task WithPagination_CrossHost_ClampDefaultAboveCap_SchemaDefaultMatchesOmission(
        string path, OpenApiSpecVersion? openApiVersion)
    {
        using var host = CreateHost(openApiVersion);
        using var client = host.GetTestClient();
        using var document = await ReadOpenApiAsync(client);

        var schema = Parameter(document, path, "limit").GetProperty("schema");
        var schemaDefault = schema.GetProperty("default").GetInt32();
        schemaDefault.Should().Be(50);
        schema.GetProperty("maximum").GetInt32().Should().Be(int.MaxValue);

        var omitted = await ReadSizeAsync(client, path);
        var explicitDefault = await ReadSizeAsync(client, path + "?limit=" + schemaDefault);

        omitted.Should().Be((50, 10, true));
        explicitDefault.Should().Be(omitted, "a client that fills in the schema default must see the same result");
    }

    private static async Task<(int Requested, int Applied, bool WasCapped)> ReadSizeAsync(
        HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var size = body.RootElement.GetProperty("size");
        var requested = size.GetProperty("requested").GetInt32();
        var applied = size.GetProperty("applied").GetInt32();
        return (requested, applied, applied < requested);
    }

    [Fact]
    public async Task WithPagination_ExistingQueryParameter_DeclaredBinding_IsPreservedWithoutDuplicates()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        using var body = await ReadOpenApiAsync(client);

        const string path = "/pagination-docs/existing";
        Parameters(body, path).Should().HaveCount(2);
        var cursor = Parameter(body, path, "cursor");
        cursor.GetProperty("required").GetBoolean().Should().BeTrue();
        cursor.GetProperty("description").GetString().Should().Be("Existing cursor documentation.");
    }

    [Fact]
    public async Task WithPagination_UnmarkedEndpoint_NoDeclaration_LeavesParametersUnchanged()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        using var body = await ReadOpenApiAsync(client);

        Parameters(body, "/pagination-docs/unmarked").Should().BeEmpty();
    }

    private static JsonElement[] Parameters(JsonDocument document, string path) =>
        document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get")
            .TryGetProperty("parameters", out var parameters)
            ? [.. parameters.EnumerateArray()]
            : [];

    private static JsonElement Parameter(JsonDocument document, string path, string name) =>
        Parameters(document, path).Single(parameter => parameter.GetProperty("name").GetString() == name);

    private static async Task<JsonDocument> ReadOpenApiAsync(HttpClient client, string document = "v1")
    {
        using var response = await client.GetAsync(
            $"/openapi/{document}.json", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    private static IHost CreateHost(
        OpenApiSpecVersion? openApiVersion = OpenApiSpecVersion.OpenApi3_1,
        bool registerAspFirst = true,
        string? paginationDocument = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddProblemDetails();
                    if (registerAspFirst)
                        services.AddTrellisAsp();
                    foreach (var document in DocumentNames)
                    {
                        services.AddOpenApi(document, options =>
                        {
                            if (openApiVersion is { } version)
                                options.OpenApiVersion = version;
                            if (paginationDocument is null || paginationDocument == document)
                                options.AddOperationTransformer<TrellisPaginationOperationTransformer>();
                        });
                    }

                    if (!registerAspFirst)
                        services.AddTrellisAsp();
                    services.AddControllers().AddApplicationPart(typeof(PaginationMetadataController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/pagination-docs/minimal", (HttpRequest request) =>
                            request.TryCreatePageRequest().ToHttpResponse())
                            .WithPagination();
                        endpoints.MapGet("/pagination-docs/minimal/custom", (HttpRequest request) =>
                            request.TryCreatePageRequest("after", "take", 25, 10, PageSizeLimitPolicy.Reject)
                                .ToHttpResponse())
                            .WithPagination("after", "take", 25, 10, PageSizeLimitPolicy.Reject);
                        endpoints.MapGet("/pagination-docs/minimal/capped-default", (HttpRequest request) =>
                            request.TryCreatePageRequest(max: 10, defaultSize: 50, policy: PageSizeLimitPolicy.Reject)
                                .ToHttpResponse())
                            .WithPagination(max: 10, defaultSize: 50, policy: PageSizeLimitPolicy.Reject);
                        endpoints.MapGet("/pagination-docs/minimal/clamp-default", (HttpRequest request) =>
                            request.TryCreatePageRequest(max: 10, defaultSize: 50)
                                .ToHttpResponse())
                            .WithPagination(max: 10, defaultSize: 50);
                        endpoints.MapGet("/pagination-docs/minimal/no-parser", () => Results.NoContent())
                            .WithPagination();

                        var group = endpoints.MapGroup("/pagination-docs/group")
                            .WithPagination("continuation", "count", 75, 15, PageSizeLimitPolicy.Reject);
                        group.MapGet("/inherited", () => Results.NoContent());
                        group.MapGet("/override", () => Results.NoContent())
                            .WithPagination("after", "take", 25, 10, PageSizeLimitPolicy.Reject);

                        endpoints.MapGet("/pagination-docs/existing",
                            ([FromQuery, System.ComponentModel.Description("Existing cursor documentation.")] string cursor) =>
                                Results.Ok(cursor))
                            .WithPagination();
                        endpoints.MapGet("/pagination-docs/unmarked", () => Results.NoContent());
                        endpoints.MapControllers();
                        endpoints.MapOpenApi();
                    });
                }))
            .Start();
}

[ApiController]
[Route("pagination-docs/mvc")]
[Pagination]
public sealed class PaginationMetadataController : ControllerBase
{
    [HttpGet]
    public Microsoft.AspNetCore.Http.IResult Get() =>
        Request.TryCreatePageRequest().ToHttpResponse();

    [HttpGet("custom")]
    [Pagination("after", "take", 25, 10, PageSizeLimitPolicy.Reject)]
    public Microsoft.AspNetCore.Http.IResult Custom() =>
        Request.TryCreatePageRequest("after", "take", 25, 10, PageSizeLimitPolicy.Reject).ToHttpResponse();

    [HttpGet("capped-default")]
    [Pagination(max: 10, defaultSize: 50, policy: PageSizeLimitPolicy.Reject)]
    public Microsoft.AspNetCore.Http.IResult CappedDefault() =>
        Request.TryCreatePageRequest(max: 10, defaultSize: 50, policy: PageSizeLimitPolicy.Reject)
            .ToHttpResponse();

    [HttpGet("clamp-default")]
    [Pagination(max: 10, defaultSize: 50)]
    public Microsoft.AspNetCore.Http.IResult ClampDefault() =>
        Request.TryCreatePageRequest(max: 10, defaultSize: 50).ToHttpResponse();

    [HttpGet("no-parser")]
    public NoContentResult NoParser() => NoContent();
}
