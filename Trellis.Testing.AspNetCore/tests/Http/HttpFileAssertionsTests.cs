namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Direct branch coverage for <see cref="HttpFileAssertions"/>: status range edge cases,
/// required header lookup across response and content headers, missing header diagnostics,
/// and the body-truncation path used in failure messages.
/// </summary>
public class HttpFileAssertionsTests
{
    private static HttpFileResult MakeResult(
        HttpStatusCode status,
        string title = "x",
        string? body = null,
        ExpectedOutcome? expected = null,
        Dictionary<string, string>? responseHeaders = null,
        Dictionary<string, string>? contentHeaders = null,
        bool withContent = true)
    {
        var resp = new HttpResponseMessage(status);
        if (withContent)
        {
            resp.Content = new StringContent(body ?? string.Empty, Encoding.UTF8);
            if (contentHeaders is not null)
                foreach (var (k, v) in contentHeaders)
                    resp.Content.Headers.TryAddWithoutValidation(k, v);
        }

        if (responseHeaders is not null)
            foreach (var (k, v) in responseHeaders)
                resp.Headers.TryAddWithoutValidation(k, v);

        var req = new HttpFileRequest(
            Title: title, Method: "GET", Url: "/x",
            Headers: new Dictionary<string, string>(),
            Body: null, Name: null, Expected: expected, ParityMode: null);

        return new HttpFileResult(req, resp, body, expected);
    }

    [Fact]
    public void Throws_on_null_result()
        => FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void Default_contract_passes_for_2xx_when_no_expectations()
    {
        var r = MakeResult(HttpStatusCode.OK);
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Status_min_equals_max_uses_single_value_in_message()
    {
        var r = MakeResult(HttpStatusCode.OK, expected: new ExpectedOutcome(404, 404, new List<string>()));
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>()
            .WithMessage("*expected status 404, got 200*");
    }

    [Fact]
    public void Status_range_uses_dash_format_in_message()
    {
        var r = MakeResult(HttpStatusCode.InternalServerError,
            expected: new ExpectedOutcome(200, 299, new List<string>()));
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>()
            .WithMessage("*expected status 200-299, got 500*");
    }

    [Fact]
    public void Status_in_inclusive_range_passes()
    {
        var r = MakeResult(HttpStatusCode.OK,
            expected: new ExpectedOutcome(200, 299, new List<string>()));
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Required_header_found_on_response_passes()
    {
        var r = MakeResult(HttpStatusCode.OK,
            expected: new ExpectedOutcome(200, 200, new List<string> { "ETag" }),
            responseHeaders: new() { ["ETag"] = "\"v\"" });
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Required_header_found_on_content_headers_passes()
    {
        var r = MakeResult(HttpStatusCode.OK,
            expected: new ExpectedOutcome(200, 200, new List<string> { "Content-Type" }));
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Required_header_missing_throws_with_diagnostic()
    {
        var r = MakeResult(HttpStatusCode.OK,
            expected: new ExpectedOutcome(200, 200, new List<string> { "ETag" }),
            responseHeaders: new() { ["X-Other"] = "1" });
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>()
            .WithMessage("*expected header 'ETag'*Present headers:*X-Other*");
    }

    [Fact]
    public void Default_contract_failure_includes_truncated_body()
    {
        var longBody = new string('a', 500);
        var r = MakeResult(HttpStatusCode.InternalServerError, body: longBody);
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>()
            .WithMessage("*…*"); // truncation marker
    }

    [Fact]
    public void Default_contract_failure_with_empty_body_says_empty()
    {
        var r = MakeResult(HttpStatusCode.BadRequest, body: null);
        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>()
            .WithMessage("*<empty>*");
    }
}
