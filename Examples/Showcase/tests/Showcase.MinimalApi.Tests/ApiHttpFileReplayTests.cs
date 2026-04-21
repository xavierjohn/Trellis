namespace Trellis.Showcase.MinimalApi.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Showcase.MinimalApi;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Replays <c>Examples/Showcase/api.http</c> against the Showcase Minimal API host.
/// </summary>
public class ApiHttpFileReplayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiHttpFileReplayTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Replays_api_http_contract_end_to_end_against_minimal_api_host()
    {
        var requests = LoadRequests();

        using var client = _factory.CreateClient();
        var results = await HttpFileRunner.RunAsync(client, requests, Ct);

        var failures = new StringBuilder();
        foreach (var result in results)
        {
            try
            {
                HttpFileAssertions.AssertExpectationsMet(result);
            }
            catch (HttpFileAssertionException ex)
            {
                failures.AppendLine(ex.Message);
            }
        }

        failures.Length.Should().Be(0, failures.ToString());
    }

    private static IReadOnlyList<HttpFileRequest> LoadRequests()
    {
        var httpPath = Path.Combine(AppContext.BaseDirectory, "api.http");
        var envPath = Path.Combine(AppContext.BaseDirectory, "http-client.env.json");
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(envPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(envPath));
            if (doc.RootElement.TryGetProperty("$shared", out var shared))
            {
                foreach (var prop in shared.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        vars[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }
            }
        }

        vars["host"] = string.Empty;
        return HttpFileParser.ParseFile(httpPath, vars);
    }
}
