namespace Trellis.Showcase.Tests.Api;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Showcase.Application;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenApi_AccountPagination_CustomClampSettings_DescribesQueryContract(bool minimalApi)
    {
        using var client = minimalApi ? _fixture.Minimal.CreateClient() : _fixture.Mvc.CreateClient();
        using var document = JsonDocument.Parse(
            await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct));
        const string path = "/api/accounts";
        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get");
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();

        parameters.Should().HaveCount(2);
        parameters.Should().OnlyContain(parameter => parameter.GetProperty("in").GetString() == "query");
        parameters.Where(parameter =>
                parameter.TryGetProperty("required", out var required) && required.GetBoolean())
            .Should().BeEmpty();

        var cursor = parameters.Single(parameter => parameter.GetProperty("name").GetString() == "cursor")
            .GetProperty("schema");
        cursor.GetProperty("type").GetString().Should().Be("string");
        cursor.GetProperty("minLength").GetInt32().Should().Be(1);
        cursor.GetProperty("pattern").GetString().Should().NotBeNullOrEmpty();

        var limit = parameters.Single(parameter => parameter.GetProperty("name").GetString() == "limit");
        var schema = limit.GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("integer");
        schema.GetProperty("format").GetString().Should().Be("int32");
        schema.GetProperty("minimum").GetInt32().Should().Be(1);
        schema.GetProperty("maximum").GetInt32().Should().Be(int.MaxValue);
        schema.GetProperty("default").GetInt32().Should().Be(10);
        limit.GetProperty("description").GetString().Should().Contain("requests 10").And.Contain("clamped");
        var responses = operation.GetProperty("responses");
        responses.GetProperty("200").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString().Should().NotBeNullOrEmpty();
        responses.GetProperty("422").GetProperty("content")
            .EnumerateObject().Select(content => content.Name).Should().Contain("application/problem+json");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_AccountPagination_SchemaDefaultMatchesOmission(bool minimalApi)
    {
        using var client = minimalApi ? _fixture.Minimal.CreateClient() : _fixture.Mvc.CreateClient();
        using var document = JsonDocument.Parse(
            await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct));
        const string path = "/api/accounts";
        var appliedDefault = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get")
            .GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "limit")
            .GetProperty("schema").GetProperty("default").GetInt32();

        using var omitted = JsonDocument.Parse(
            await client.GetStringAsync(new Uri(path, UriKind.Relative), Ct));
        using var explicitDefault = JsonDocument.Parse(
            await client.GetStringAsync(new Uri($"{path}?limit={appliedDefault}", UriKind.Relative), Ct));

        appliedDefault.Should().Be(10);
        foreach (var body in new[] { omitted, explicitDefault })
        {
            body.RootElement.GetProperty("requestedLimit").GetInt32().Should().Be(10);
            body.RootElement.GetProperty("appliedLimit").GetInt32().Should().Be(5);
            body.RootElement.GetProperty("wasCapped").GetBoolean().Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(false, "limit=bad")]
    [InlineData(true, "limit=bad")]
    [InlineData(false, "limit=1&limit=2")]
    [InlineData(true, "limit=1&limit=2")]
    public async Task List_AccountPagination_InvalidRawLimit_Returns422QueryViolation(bool minimalApi, string query)
    {
        using var client = minimalApi ? _fixture.Minimal.CreateClient() : _fixture.Mvc.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/accounts?" + query, UriKind.Relative), Ct);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var violation = problem.RootElement.GetProperty("fieldViolations")[0];
        violation.GetProperty("code").GetString().Should().Be("format.integer");
        violation.GetProperty("location").GetProperty("in").GetString().Should().Be("query");
        violation.GetProperty("location").GetProperty("name").GetString().Should().Be("limit");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpTracing_NativeAspNetCore_RequestCompletes_PreservesSemanticAttributes(bool minimalApi)
    {
        using var parent = new Activity("showcase-native-http-tracing")
            .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded)
            .Start();
        var completed = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == parent.TraceId)
                    completed.TrySetResult(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var client = minimalApi ? _fixture.Minimal.CreateClient() : _fixture.Mvc.CreateClient();
        client.DefaultRequestHeaders.Add("traceparent", parent.Id);
        var path = $"/api/accounts/{Guid.NewGuid()}";

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        var server = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        server.Kind.Should().Be(ActivityKind.Server);
        server.GetTagItem("http.request.method").Should().Be("GET");
        server.GetTagItem("url.path").Should().Be(path);
        Convert.ToInt32(server.GetTagItem("http.response.status_code"), CultureInfo.InvariantCulture).Should().Be(404);
    }

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
    /// An unknown <c>AccountType</c> must carry the same machine-readable contract —
    /// <c>code</c>, <c>location</c>, <c>args.allowed</c> — from either host, so a client
    /// that switches hosts does not have to relearn how to read a rejection.
    /// <para>
    /// Prose is deliberately excluded from that contract, because this rejection has two
    /// producers that word it differently: <c>RequiredEnum.TryCreate</c> says "'Platinum' is
    /// not a valid AccountType. Valid values: …", and <c>RequiredEnumJsonConverter</c> says
    /// "Invalid AccountType value: 'Platinum'. Valid values are: …". Which one answers
    /// depends on how the value reached the model, which is the case for <c>args.allowed</c>
    /// in miniature: the members a rejection would have accepted are the API's contract, and
    /// belong somewhere a client can read without a parser.
    /// </para>
    /// <para>
    /// Note that both <see cref="WebApplicationFactory{TEntryPoint}"/> hosts here happen to
    /// take the <c>TryCreate</c> path, so their prose agrees today; the standalone Minimal API
    /// host takes the converter path and its prose does not. That divergence is precisely why
    /// this test asserts the machine-readable fields and only checks that <c>detail</c> is
    /// non-empty — asserting the sentence would pin one producer's wording and make the test
    /// depend on the test process's converter selection. This test does not run a standalone
    /// or Native AOT host; the CI runtime smoke checks the same structured contract through
    /// both actual Production hosts.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Unknown_enum_member_carries_the_same_machine_readable_contract_on_both_hosts()
    {
        using var mvcClient = _fixture.Mvc.CreateClient();
        using var minClient = _fixture.Minimal.CreateClient();

        var mvc = await RejectUnknownAccountTypeAsync(mvcClient);
        var min = await RejectUnknownAccountTypeAsync(minClient);

        foreach (var (host, violation) in new[] { ("MVC", mvc), ("Minimal API", min) })
        {
            violation.GetProperty("code").GetString().Should().Be("enum.name-undefined", $"{host} rejects by name");

            var location = violation.GetProperty("location");
            location.GetProperty("in").GetString().Should().Be("body", $"{host} rejected a body field, not a query or route value");
            location.GetProperty("pointer").GetString().Should().Be("/accountType", $"{host} points at the field");

            violation.GetProperty("args").GetProperty("allowed").EnumerateArray()
                .Select(member => member.GetString())
                .Should().Equal(["Checking", "MoneyMarket", "Savings"],
                    $"{host} must name the products it accepts, ordinally sorted");
        }

        mvc.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        min.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<JsonElement> RejectUnknownAccountTypeAsync(HttpClient client)
    {
        var payload = $$"""
            {
              "customerId": "{{ShowcaseSeed.AliceId}}",
              "accountType": "Platinum",
              "initialDeposit":       { "amount": 250.00, "currency": "USD" },
              "dailyWithdrawalLimit": { "amount": 500.00, "currency": "USD" },
              "overdraftLimit":       { "amount":   0.00, "currency": "USD" }
            }
            """;

        var response = await client.PostAsync(
            new Uri("/api/accounts", UriKind.Relative),
            new StringContent(payload, Encoding.UTF8, "application/json"),
            Ct);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableContent);

        // Clone before disposing: the returned element must outlive the document's pooled buffers.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.GetProperty("fieldViolations")[0].Clone();
    }

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