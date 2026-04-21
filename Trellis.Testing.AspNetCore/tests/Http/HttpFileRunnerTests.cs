namespace Trellis.Testing.AspNetCore.Tests.Http;

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trellis.Testing.AspNetCore.Http;

public class HttpFileRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAsync_chains_named_response_body_into_later_request_url()
    {
        // First request returns {"id":"abc"}; second URL references {{create.response.body.id}}.
        string? capturedUrl = null;
        using var handler = new StubHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/things", StringComparison.Ordinal) && req.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\":\"abc\",\"eTag\":\"v1\"}", Encoding.UTF8, "application/json"),
                };
            }

            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        const string file = """
            ### Create
            # @name create
            POST http://fake/things
            Content-Type: application/json

            {}

            ### Fetch
            GET http://fake/things/{{create.response.body.id}}
            """;
        var reqs = HttpFileParser.Parse(file);

        var results = await HttpFileRunner.RunAsync(client, reqs, Ct);

        results.Should().HaveCount(2);
        capturedUrl.Should().Be("http://fake/things/abc");
    }

    [Fact]
    public async Task RunAsync_substitutes_named_response_header_into_later_request()
    {
        string? capturedIfMatch = null;
        using var handler = new StubHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                resp.Headers.TryAddWithoutValidation("ETag", "\"abc123\"");
                return resp;
            }

            capturedIfMatch = req.Headers.TryGetValues("If-Match", out var v) ? string.Join(",", v) : null;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        const string file = """
            ### Create
            # @name make
            POST http://fake/r

            {}

            ### Update
            PUT http://fake/r
            If-Match: {{make.response.headers.ETag}}

            {}
            """;
        var reqs = HttpFileParser.Parse(file);
        _ = await HttpFileRunner.RunAsync(client, reqs, Ct);

        capturedIfMatch.Should().Be("\"abc123\"");
    }

    [Fact]
    public async Task RunAsync_records_status_in_result()
    {
        using var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };

        const string file = """
            ### Miss
            # @expect status: 404
            GET http://fake/missing
            """;
        var reqs = HttpFileParser.Parse(file);

        var results = await HttpFileRunner.RunAsync(client, reqs, Ct);

        results[0].Response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var act = () => HttpFileAssertions.AssertExpectationsMet(results[0]);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task AssertExpectationsMet_defaults_to_non_error_range_when_no_expect()
    {
        using var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        var reqs = HttpFileParser.Parse("### x\nGET http://fake/x\n");
        var results = await HttpFileRunner.RunAsync(client, reqs, Ct);
        var act = () => HttpFileAssertions.AssertExpectationsMet(results[0]);
        act.Should().Throw<HttpFileAssertionException>();
    }

    [Fact]
    public async Task AssertExpectationsMet_enforces_required_header()
    {
        using var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        const string file = """
            ### E
            # @expect header: ETag
            GET http://fake/x
            """;
        var results = await HttpFileRunner.RunAsync(client, HttpFileParser.Parse(file), Ct);
        var act = () => HttpFileAssertions.AssertExpectationsMet(results[0]);
        act.Should().Throw<HttpFileAssertionException>().WithMessage("*ETag*");
    }

    [Fact]
    public async Task RunAsync_preserves_query_string_when_url_starts_with_slash()
    {
        // Regression: on Unix, Uri.TryCreate("/api/x?limit=10", Absolute) succeeds
        // as a file:// URI and percent-encodes '?' into the path. BuildUri must
        // route leading-slash URLs through HttpClient.BaseAddress instead.
        Uri? capturedUri = null;
        using var handler = new StubHandler((req, _) =>
        {
            capturedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var reqs = HttpFileParser.Parse("### list\nGET /api/accounts?limit=10\n");
        var results = await HttpFileRunner.RunAsync(client, reqs, Ct);

        results.Should().HaveCount(1);
        capturedUri.Should().NotBeNull();
        capturedUri!.Scheme.Should().Be("http");
        capturedUri.AbsolutePath.Should().Be("/api/accounts");
        capturedUri.Query.Should().Be("?limit=10");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request, cancellationToken));
    }
}
