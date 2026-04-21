namespace Trellis.Showcase.Tests.Api;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Showcase.Mvc;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Replays <c>Examples/Showcase/api.http</c> against the Showcase MVC host.
/// </summary>
public class ApiHttpFileReplayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiHttpFileReplayTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Replays_api_http_contract_end_to_end_against_mvc_host()
    {
        var requests = ApiHttpFileReplaySupport.LoadShowcaseRequests();

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
}

/// <summary>
/// Shared helpers for loading the Showcase <c>api.http</c> + env file from the
/// test output directory.
/// </summary>
internal static class ApiHttpFileReplaySupport
{
    internal static IReadOnlyList<HttpFileRequest> LoadShowcaseRequests()
    {
        var httpPath = Path.Combine(AppContext.BaseDirectory, "api.http");
        var envPath = Path.Combine(AppContext.BaseDirectory, "http-client.env.json");
        var vars = LoadEnv(envPath);
        vars["host"] = string.Empty;
        return HttpFileParser.ParseFile(httpPath, vars);
    }

    private static Dictionary<string, string> LoadEnv(string path)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return vars;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("$shared", out var shared))
        {
            return vars;
        }

        foreach (var prop in shared.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                vars[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }

        return vars;
    }
}
