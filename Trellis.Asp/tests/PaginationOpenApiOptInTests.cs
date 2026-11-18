namespace Trellis.Asp.Tests;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public sealed class PaginationOpenApiOptInTests
{
    [Fact]
    public async Task WithPagination_WithoutAdapterRegistration_LeavesOpenApiParametersUnchanged()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddTrellisAsp();
                    services.AddOpenApi();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/pagination", () => Results.NoContent()).WithPagination();
                        endpoints.MapOpenApi();
                    });
                }))
            .Start();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            "/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var operation = body.RootElement.GetProperty("paths").GetProperty("/pagination").GetProperty("get");
        operation.TryGetProperty("parameters", out _).Should().BeFalse();
    }
}
