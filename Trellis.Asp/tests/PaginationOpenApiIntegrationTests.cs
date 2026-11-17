namespace Trellis.Asp.Tests;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;

public sealed class PaginationOpenApiIntegrationTests
{
    [Theory]
    [InlineData("v1", "/pagination-docs/minimal")]
    [InlineData("v2", "/pagination-docs/minimal")]
    [InlineData("v1", "/pagination-docs/mvc")]
    [InlineData("v2", "/pagination-docs/mvc")]
    public async Task WithPagination_CrossHost_NamedDocuments_EmitsOptionalQueryParameters(
        string document, string path)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        using var body = await ReadOpenApiAsync(client, document);

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
    [InlineData("/pagination-docs/minimal/capped-default")]
    [InlineData("/pagination-docs/mvc/capped-default")]
    public async Task WithPagination_CrossHost_DefaultAboveCap_DocumentsAppliedDefault(string path)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        using var document = await ReadOpenApiAsync(client);

        var limit = Parameter(document, path, "limit");
        var schema = limit.GetProperty("schema");
        schema.GetProperty("default").GetInt32().Should().Be(10);
        schema.GetProperty("maximum").GetInt32().Should().Be(10);
        limit.GetProperty("description").GetString().Should().Contain("50").And.Contain("10");

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var size = body.RootElement.GetProperty("size");
        size.GetProperty("requested").GetInt32().Should().Be(50);
        size.GetProperty("applied").GetInt32().Should().Be(10);
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

    private static IHost CreateHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddProblemDetails();
                    services.AddTrellisAsp();
                    services.AddOpenApi("v1", options => options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1);
                    services.AddOpenApi("v2", options => options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1);
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

    [HttpGet("no-parser")]
    public NoContentResult NoParser() => NoContent();
}
