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

public sealed class HttpRequestPaginationIntegrationTests
{
    [Theory]
    [InlineData("/tfr8-pagination/minimal", "?cursor=", ValidationCodes.CursorMalformed, "cursor")]
    [InlineData("/tfr8-pagination/minimal", "?limit=not-an-integer", ValidationCodes.FormatInteger, "limit")]
    [InlineData("/tfr8-pagination/mvc", "?cursor=", ValidationCodes.CursorMalformed, "cursor")]
    [InlineData("/tfr8-pagination/mvc", "?limit=not-an-integer", ValidationCodes.FormatInteger, "limit")]
    public async Task TryCreatePageRequest_CrossHost_MalformedQueryReturnsQueryLocated422(
        string path,
        string query,
        string code,
        string parameter)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(path + query, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var violation = body.RootElement.GetProperty("fieldViolations")
            .EnumerateArray().Should().ContainSingle().Which;
        violation.GetProperty("code").GetString().Should().Be(code);
        violation.GetProperty("location").GetProperty("in").GetString().Should().Be("query");
        violation.GetProperty("location").GetProperty("name").GetString().Should().Be(parameter);
    }

    [Theory]
    [InlineData("/tfr8-pagination/minimal/decode")]
    [InlineData("/tfr8-pagination/mvc/decode")]
    public async Task TryCreatePageRequest_CrossHost_DecodedCursorFailureReturnsQueryLocated422(string path)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            path + "?cursor=not-a-valid-cursor",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var violation = body.RootElement.GetProperty("fieldViolations")
            .EnumerateArray().Should().ContainSingle().Which;
        violation.GetProperty("code").GetString().Should().Be(ValidationCodes.CursorMalformed);
        violation.GetProperty("location").GetProperty("in").GetString().Should().Be("query");
        violation.GetProperty("location").GetProperty("name").GetString().Should().Be("cursor");
    }

    private static IHost CreateHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddProblemDetails();
                    services.AddTrellisAsp();
                    services.AddControllers().AddApplicationPart(typeof(PaginationQueryController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/tfr8-pagination/minimal", (HttpRequest request) =>
                            request.TryCreatePageRequest().ToHttpResponse());
                        endpoints.MapGet("/tfr8-pagination/minimal/decode", (HttpRequest request) =>
                            request.TryCreatePageRequest()
                                .Bind(pageRequest => pageRequest.Decode(CursorCodec.Scalar<int>()))
                                .ToHttpResponse())
                            .WithInputOrigin(InputLocation.Query);
                        endpoints.MapControllers();
                    });
                }))
            .Start();
}

[ApiController]
[Route("tfr8-pagination/mvc")]
public sealed class PaginationQueryController : ControllerBase
{
    [HttpGet]
    public Microsoft.AspNetCore.Http.IResult Get() =>
        Request.TryCreatePageRequest().ToHttpResponse();

    [HttpGet("decode")]
    [InputOrigin(InputLocation.Query)]
    public Microsoft.AspNetCore.Http.IResult Decode() =>
        Request.TryCreatePageRequest()
            .Bind(pageRequest => pageRequest.Decode(CursorCodec.Scalar<int>()))
            .ToHttpResponse();
}
