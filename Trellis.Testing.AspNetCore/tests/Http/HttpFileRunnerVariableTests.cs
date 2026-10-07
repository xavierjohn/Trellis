namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Trellis.Testing.AspNetCore.Http;

public class HttpFileRunnerVariableTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAsync_GuidOccurrences_ReplayingParsedRequests_GeneratesDistinctValues()
    {
        var values = new List<string>();
        using var handler = new StubHandler(async (request, ct) =>
        {
            values.Add(request.RequestUri!.Segments[^1]);
            values.Add(request.Headers.GetValues("Idempotency-Key").Single());
            values.Add(request.Headers.GetValues("X-Second").Single());
            values.Add(request.Content!.Headers.GetValues("Content-Guid").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            values.Add(body.RootElement.GetProperty("first").GetString()!);
            values.Add(body.RootElement.GetProperty("second").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        const string file = """
            @host = http://fake
            @alias = {{$guid}}

            ### Create
            POST {{host}}/things/{{$guid}}
            Idempotency-Key: {{$guid}}
            X-Second: {{alias}}
            Content-Guid: {{$guid}}
            Content-Type: application/json

            {"first":"{{$guid}}","second":"{{$guid}}"}
            """;
        var requests = HttpFileParser.Parse(file, new Dictionary<string, string> { ["$guid"] = "not-a-dynamic-guid" });

        for (var replay = 0; replay < 2; replay++)
        {
            var results = await HttpFileRunner.RunAsync(client, requests, Ct);
            foreach (var result in results)
                result.Response.Dispose();
        }

        values.Should().HaveCount(12).And.OnlyHaveUniqueItems();
        foreach (var value in values)
            Guid.TryParseExact(value, "D", out _).Should().BeTrue("{0} must be an expanded GUID", value);
        requests[0].Url.Should().EndWith("{{$guid}}");
        requests[0].Headers["Idempotency-Key"].Should().Be("{{$guid}}");
        requests[0].Body.Should().Contain("{{$guid}}");
    }

    [Fact]
    public async Task RunSingleAsync_GuidAndNamedResponses_WithRecordedResponse_PreservesResolvedValues()
    {
        Uri? uri = null;
        string? ifMatch = null;
        string? capturedBody = null;
        using var handler = new StubHandler(async (request, ct) =>
        {
            uri = request.RequestUri;
            ifMatch = request.Headers.GetValues("If-Match").Single();
            capturedBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        var context = new ScenarioContext();
        context.Record("create", 201, new Dictionary<string, string> { ["ETag"] = "\"v1\"" }, """{"id":"abc"}""");
        var request = HttpFileParser.Parse("""
            @host = http://fake

            ### Update
            PUT {{host}}/things/{{create.response.body.id}}/{{$guid}}
            If-Match: {{create.response.headers.ETag}}
            Content-Type: application/json

            {"id":"{{create.response.body.id}}","status":"{{create.response.status}}"}
            """)[0];

        var result = await HttpFileRunner.RunSingleAsync(client, request, context, Ct);
        using var response = result.Response;

        uri!.AbsolutePath.Should().StartWith("/things/abc/");
        Guid.TryParseExact(uri.Segments[^1], "D", out _).Should().BeTrue();
        ifMatch.Should().Be("\"v1\"");
        capturedBody.Should().Be("""{"id":"abc","status":"201"}""");
    }

    [Theory]
    [InlineData("URL", "{{missing}}")]
    [InlineData("header", "{{missing}}")]
    [InlineData("content header", "{{missing}}")]
    [InlineData("header name", "{{missing}}")]
    [InlineData("header name", "{{ missing }}")]
    [InlineData("body", "{{missing}}")]
    [InlineData("URL", "{{create.response.body.missing}}")]
    [InlineData("header", "{{create.response.headers.Missing}}")]
    [InlineData("body", "{{absent.response.body.id}}")]
    [InlineData("header", "{{$timestamp}}")]
    [InlineData("body", "{{$randomInt}}")]
    [InlineData("URL", "{{$datetime}}")]
    [InlineData("body", "{{unclosed")]
    public async Task RunSingleAsync_UnresolvedPlaceholder_InRequestFields_RejectsBeforeSending(string location, string token)
    {
        var sends = 0;
        using var handler = new StubHandler((_, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        var context = new ScenarioContext();
        context.Record("create", 201, new Dictionary<string, string>(), """{"id":"abc"}""");
        var request = new HttpFileRequest(
            "Reject token", "POST", location == "URL" ? "/things/" + token : "/things",
            new Dictionary<string, string>
            {
                [location == "header name" ? token : "X-Value"] = location == "header" ? token : "known",
                ["Content-Type"] = location == "content header" ? token : "text/plain",
            },
            location == "body" ? "private-body " + token : "private-body", "reject", null);
        Func<Task> act = async () => await HttpFileRunner.RunSingleAsync(client, request, context, Ct);

        var failure = await act.Should().ThrowAsync<HttpFileAssertionException>();

        failure.Which.Message.Should().Contain("Reject token").And.Contain("{{").And.NotContain("private-body");
        failure.Which.Message.Should().Contain(location == "content header" ? "header 'Content-Type'" : location);
        if (token.EndsWith("}}", StringComparison.Ordinal))
            failure.Which.Message.Should().Contain(token);
        sends.Should().Be(0);
    }

    [Theory]
    [InlineData("{{missing}}")]
    [InlineData("{{$guid}}")]
    public async Task RunSingleAsync_ResponseValue_WithPlaceholder_RejectsBeforeSending(string token)
    {
        var sends = 0;
        using var handler = new StubHandler((_, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        var context = new ScenarioContext();
        context.Record("create", 200, new Dictionary<string, string>(), JsonSerializer.Serialize(new { value = token }));
        var request = HttpFileParser.Parse("""
            ### Reject resolved value
            POST /things

            {{create.response.body.value}}
            """)[0];
        Func<Task> act = async () => await HttpFileRunner.RunSingleAsync(client, request, context, Ct);

        var failure = await act.Should().ThrowAsync<HttpFileAssertionException>();
        failure.Which.Message.Should().Contain(token);
        sends.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_UnresolvedPlaceholder_InLaterRequest_StopsAndDisposesEarlierResponses()
    {
        var sends = 0;
        using var firstResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        using var handler = new StubHandler((_, _) =>
        {
            sends++;
            return Task.FromResult(firstResponse);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        var requests = HttpFileParser.Parse("""
            ### First
            GET /first

            ### Reject token
            GET /{{missing}}

            ### Never sent
            GET /third
            """);
        Func<Task> act = async () => await HttpFileRunner.RunAsync(client, requests, Ct);

        var failure = await act.Should().ThrowAsync<HttpFileAssertionException>();
        failure.Which.Message.Should().Contain("Reject token").And.Contain("{{missing}}");
        sends.Should().Be(1);
        Func<Task> readDisposed = async () => await firstResponse.Content.ReadAsStringAsync(Ct);
        await readDisposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
