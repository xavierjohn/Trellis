namespace Trellis.Showcase.Tests.Api;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Cross-host parity: replays <c>api.http</c> against BOTH the MVC host and
/// the Minimal API host and verifies the pair of responses is equivalent
/// after stripping volatile fields. Any divergence is reported per-request
/// with both response bodies in the failure message.
/// </summary>
public class ApiHttpFileParityTests : IClassFixture<ApiHttpFileParityTests.ParityFixture>
{
    private readonly ParityFixture _fixture;

    public ApiHttpFileParityTests(ParityFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly HashSet<string> VolatileFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "traceId", "faultId", "instance", "timestamp",
        "createdAtUtc", "lastModifiedUtc", "openedAtUtc", "asOfUtc",
        "next", "previous",
    };

    [Fact]
    public async Task MvcAndMinimalApi_ProduceEquivalentResponses_ForApiHttpFile()
    {
        var requests = ApiHttpFileReplaySupport.LoadShowcaseRequests();

        using var mvcClient = _fixture.Mvc.CreateClient();
        using var minClient = _fixture.Minimal.CreateClient();

        var mvcResults = await HttpFileRunner.RunAsync(mvcClient, requests, Ct);
        var minResults = await HttpFileRunner.RunAsync(minClient, requests, Ct);

        mvcResults.Count.Should().Be(minResults.Count);

        var failures = new StringBuilder();
        for (int i = 0; i < mvcResults.Count; i++)
        {
            var m = mvcResults[i];
            var n = minResults[i];
            var title = m.Request.Title;

            // Status parity — always required.
            if (m.Response.StatusCode != n.Response.StatusCode)
            {
                failures.AppendLine(CultureInfo.InvariantCulture,
                    $"[{title}] status diverged: MVC={(int)m.Response.StatusCode} Minimal={(int)n.Response.StatusCode}");
                continue;
            }

            // Content-type family (before ';') parity.
            var mvcType = m.Response.Content?.Headers.ContentType?.MediaType;
            var minType = n.Response.Content?.Headers.ContentType?.MediaType;
            if (!string.Equals(mvcType, minType, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(m.Request.ParityMode, "status-only", StringComparison.OrdinalIgnoreCase))
            {
                failures.AppendLine(CultureInfo.InvariantCulture,
                    $"[{title}] content-type diverged: MVC='{mvcType}' Minimal='{minType}'");
                continue;
            }

            if (string.Equals(m.Request.ParityMode, "status-only", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var mvcBody = Normalize(m.Body);
            var minBody = Normalize(n.Body);
            if (!string.Equals(mvcBody, minBody, StringComparison.Ordinal))
            {
                failures.AppendLine(CultureInfo.InvariantCulture,
                    $"[{title}] body diverged.\n  MVC:     {Truncate(mvcBody)}\n  Minimal: {Truncate(minBody)}");
            }
        }

        failures.Length.Should().Be(0, failures.ToString());
    }

    private static string Normalize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(body);
            if (node is null)
            {
                return body.Trim();
            }

            Strip(node);
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            return body.Trim();
        }
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var keys = obj.Select(kv => kv.Key).ToList();
                foreach (var key in keys)
                {
                    if (VolatileFields.Contains(key))
                    {
                        obj.Remove(key);
                        continue;
                    }

                    Strip(obj[key]);
                }

                // Canonicalize key order for deterministic comparison.
                var reordered = obj.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                foreach (var kv in reordered)
                {
                    obj.Remove(kv.Key);
                }

                foreach (var kv in reordered)
                {
                    obj[kv.Key] = kv.Value;
                }

                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    Strip(item);
                }

                break;
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";

    /// <summary>
    /// Owns both MVC and Minimal API factories so the parity test can reuse the
    /// same hosts for every request pair.
    /// </summary>
    public sealed class ParityFixture : IDisposable
    {
        public WebApplicationFactory<Trellis.Showcase.Mvc.Program> Mvc { get; } = new();

        public WebApplicationFactory<Trellis.Showcase.MinimalApi.Program> Minimal { get; } = new();

        public void Dispose()
        {
            Mvc.Dispose();
            Minimal.Dispose();
        }
    }
}
