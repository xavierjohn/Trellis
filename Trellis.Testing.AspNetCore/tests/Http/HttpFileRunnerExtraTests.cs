namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Extra coverage for <see cref="HttpFileRunner"/>: argument validation, content vs request
/// header routing, BuildUri unanchored relative path, response capture for unnamed requests,
/// substitution leaves unknown tokens intact.
/// </summary>
public class HttpFileRunnerExtraTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> r) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(r(req, ct));
    }

    [Fact]
    public Task RunAsync_throws_on_null_client()
        => FluentActions.Invoking(async () =>
                await HttpFileRunner.RunAsync(null!, new List<HttpFileRequest>()))
            .Should().ThrowAsync<ArgumentNullException>();

    [Fact]
    public Task RunAsync_throws_on_null_requests()
    {
        var client = new HttpClient { BaseAddress = new Uri("http://x/") };
        return FluentActions.Invoking(async () =>
                await HttpFileRunner.RunAsync(client, null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunSingleAsync_validates_arguments()
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://x/") };
        var req = new HttpFileRequest("t", "GET", "/", new Dictionary<string, string>(), null, null, null, null);
        var ctx = new ScenarioContext();

        await FluentActions.Invoking(async () => await HttpFileRunner.RunSingleAsync(null!, req, ctx))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(async () => await HttpFileRunner.RunSingleAsync(client, null!, ctx))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(async () => await HttpFileRunner.RunSingleAsync(client, req, null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_routes_Content_Type_header_onto_HttpContent()
    {
        string? capturedCt = null;
        using var handler = new StubHandler((req, _) =>
        {
            capturedCt = req.Content?.Headers.ContentType?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        const string file = """
            ### X
            POST http://fake/x
            Content-Type: application/json

            {}
            """;
        var reqs = HttpFileParser.Parse(file);
        await HttpFileRunner.RunAsync(client, reqs, Ct);

        capturedCt.Should().Be("application/json");
    }

    [Fact]
    public async Task RunAsync_creates_default_content_when_only_content_header_specified()
    {
        // Content-Length is a content header. With no body, runner creates an empty
        // StringContent so the header has a place to live.
        string? capturedCl = null;
        using var handler = new StubHandler((req, _) =>
        {
            capturedCl = req.Content?.Headers.ContentLength?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        var req = new HttpFileRequest(
            Title: "X", Method: "GET", Url: "/x",
            Headers: new Dictionary<string, string> { ["Content-Length"] = "0" },
            Body: null, Name: null, Expected: null, ParityMode: null);

        await HttpFileRunner.RunAsync(client, new[] { req }, Ct);

        capturedCl.Should().Be("0");
    }

    [Fact]
    public async Task RunAsync_unanchored_relative_url_resolves_against_base_address()
    {
        Uri? captured = null;
        using var handler = new StubHandler((req, _) =>
        {
            captured = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var reqs = HttpFileParser.Parse("### X\nGET items/1\n");
        await HttpFileRunner.RunAsync(client, reqs, Ct);

        captured.Should().NotBeNull();
        captured!.AbsolutePath.Should().Be("/items/1");
    }

    [Fact]
    public async Task RunAsync_unnamed_request_does_not_record_into_context()
    {
        // Smoke: an unnamed request should still execute and return a result, but its
        // response should not be available for substitution by a later named lookup.
        string? capturedSecond = null;
        using var handler = new StubHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\":\"abc\"}", Encoding.UTF8, "application/json"),
                };
            capturedSecond = req.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        const string file = """
            ### Create (unnamed)
            POST http://fake/things

            {}

            ### Fetch — substitution token will not resolve
            GET http://fake/things/{{create.response.body.id}}
            """;
        var reqs = HttpFileParser.Parse(file);
        var results = await HttpFileRunner.RunAsync(client, reqs, Ct);

        results.Should().HaveCount(2);
        // Token is left unresolved (no named record) — the URL gets percent-encoded
        // when the HttpRequestMessage Uri is constructed.
        capturedSecond.Should().NotBeNull();
        capturedSecond!.Should().NotContain("abc");
        Uri.UnescapeDataString(capturedSecond!).Should().Contain("{{create.response.body.id}}");
    }

    [Fact]
    public async Task RunAsync_substitution_passes_through_input_with_no_braces()
    {
        Uri? captured = null;
        using var handler = new StubHandler((req, _) =>
        {
            captured = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };

        var reqs = HttpFileParser.Parse("### X\nGET /plain/path\n");
        await HttpFileRunner.RunAsync(client, reqs, Ct);

        captured!.AbsolutePath.Should().Be("/plain/path");
    }

    [Fact]
    public async Task RunAsync_substitution_preserves_unmatched_open_braces()
    {
        Uri? captured = null;
        using var handler = new StubHandler((req, _) =>
        {
            captured = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://x/") };

        var reqs = HttpFileParser.Parse("### X\nGET /a/{{unclosed\n");
        await HttpFileRunner.RunAsync(client, reqs, Ct);

        captured!.AbsolutePath.Should().Contain("unclosed");
    }
}