namespace ConditionalRequestExample.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Replays <c>Examples/ConditionalRequestExample/api.http</c> end-to-end against the
/// in-process host. Proves that the sample's documented request sequence still works,
/// including ETag-based chaining (If-Match / If-None-Match).
/// </summary>
public class ApiHttpFileReplayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiHttpFileReplayTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Replays_api_http_contract_end_to_end()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "api.http");
        // Treat URLs as host-relative so HttpClient.BaseAddress resolves them.
        var vars = new Dictionary<string, string> { ["host"] = string.Empty };
        var requests = HttpFileParser.ParseFile(path, vars);

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