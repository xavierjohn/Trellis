namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Covers the <c># @expect content-type:</c> directive.
/// </summary>
/// <remarks>
/// <para>
/// This directive exists because of a regression the harness could not see. Applying
/// <c>[Produces("application/json")]</c> to an MVC controller silently rewrites a
/// <c>ProblemDetails</c> response from <c>application/problem+json</c> to
/// <c>application/json</c>: the status code stays correct and the body stays correct, so
/// every status-and-header assertion still passes while the response has stopped conforming
/// to RFC 9457. Content type was the only observable that changed, and nothing could assert
/// on it.
/// </para>
/// </remarks>
public class HttpFileContentTypeExpectationTests
{
    private static HttpFileResult MakeResult(
        string? contentType,
        ExpectedOutcome? expected,
        HttpStatusCode status = HttpStatusCode.OK,
        string title = "x",
        bool withContent = true)
    {
        var resp = new HttpResponseMessage(status);
        if (withContent)
        {
            resp.Content = new StringContent("{}", Encoding.UTF8);
            resp.Content.Headers.Remove("Content-Type");
            if (contentType is not null)
                resp.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        var req = new HttpFileRequest(
            Title: title, Method: "GET", Url: "/x",
            Headers: new Dictionary<string, string>(),
            Body: null, Name: null, Expected: expected, ParityMode: null);

        return new HttpFileResult(req, resp, "{}", expected);
    }

    private static ExpectedOutcome Expect(string contentType)
        => new(null, null, [], contentType);

    [Fact]
    public void Parser_reads_the_content_type_directive()
    {
        const string file = """
            ### Fetch a thing
            # @expect content-type: application/problem+json
            GET {{host}}/thing
            """;

        var requests = HttpFileParser.Parse(file);

        requests.Should().ContainSingle();
        requests[0].Expected!.ContentType.Should().Be("application/problem+json");
    }

    [Fact]
    public void Parser_keeps_content_type_parameters_verbatim()
    {
        // The value is stored exactly as written -- not normalized down to a bare media type --
        // so the PowerShell replay parser must capture the rest of the line rather than the first
        // whitespace-delimited token. Matching still ignores the parameters.
        const string file = """
            ### Fetch a thing
            # @expect content-type: application/problem+json; charset=utf-8
            GET {{host}}/thing
            """;

        var requests = HttpFileParser.Parse(file);

        requests.Should().ContainSingle();
        requests[0].Expected!.ContentType.Should().Be("application/problem+json; charset=utf-8");
    }

    [Fact]
    public void Parser_creates_an_expectation_when_content_type_is_the_only_directive()
    {
        const string file = """
            ### Fetch a thing
            # @expect content-type: application/json
            GET {{host}}/thing
            """;

        var requests = HttpFileParser.Parse(file);

        // Regression guard: an expectation carrying only a content type must still be
        // materialised. If ExpectedOutcome is only built when a status or header was
        // declared, this directive is parsed and then silently discarded.
        requests[0].Expected.Should().NotBeNull();
        requests[0].Expected!.ContentType.Should().Be("application/json");
    }

    [Fact]
    public void Parser_keeps_content_type_alongside_status_and_header_directives()
    {
        const string file = """
            ### Fetch a thing
            # @expect status: 422
            # @expect header: Link
            # @expect content-type: application/problem+json
            GET {{host}}/thing
            """;

        var expected = HttpFileParser.Parse(file)[0].Expected!;

        expected.StatusMin.Should().Be(422);
        expected.RequiredHeaders.Should().Equal(["Link"]);
        expected.ContentType.Should().Be("application/problem+json");
    }

    [Fact]
    public void Passes_when_the_media_type_matches_exactly()
    {
        var r = MakeResult("application/problem+json", Expect("application/problem+json"));

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Passes_when_the_response_adds_a_charset_parameter()
    {
        // Real responses almost always carry `; charset=utf-8`. Comparing the raw header
        // would make the directive unusable in practice, so only the media type is compared.
        var r = MakeResult("application/problem+json; charset=utf-8", Expect("application/problem+json"));

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Passes_when_the_declared_expectation_carries_its_own_parameter()
    {
        var r = MakeResult("application/problem+json; charset=utf-8", Expect("application/problem+json; charset=utf-8"));

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Passes_when_case_differs()
    {
        // RFC 9110 §8.3.1: media types are case-insensitive.
        var r = MakeResult("Application/Problem+JSON", Expect("application/problem+json"));

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Throws_when_the_media_type_differs()
    {
        var r = MakeResult("application/json; charset=utf-8", Expect("application/problem+json"));

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>()
            .WithMessage("*expected content type 'application/problem+json', got 'application/json'*");
    }

    [Fact]
    public void Throws_when_the_response_declares_no_content_type()
    {
        var r = MakeResult(contentType: null, Expect("application/problem+json"));

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>()
            .WithMessage("*expected content type 'application/problem+json'*none*");
    }

    [Fact]
    public void Throws_when_the_response_has_no_content_at_all()
    {
        var r = MakeResult(contentType: null, Expect("application/problem+json"), HttpStatusCode.NoContent, withContent: false);

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>();
    }

    [Fact]
    public void Ignores_content_type_when_no_expectation_declares_one()
    {
        var r = MakeResult("text/plain", new ExpectedOutcome(200, 200, []));

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r)).Should().NotThrow();
    }

    [Fact]
    public void Catches_the_produces_attribute_regression()
    {
        // The exact shape that shipped undetected: a 422 whose body is still a problem
        // document, whose status is still correct, but whose media type has been
        // downgraded to application/json by [Produces("application/json")].
        var expected = new ExpectedOutcome(422, 422, [], "application/problem+json");
        var r = MakeResult("application/json; charset=utf-8", expected, HttpStatusCode.UnprocessableEntity);

        FluentActions.Invoking(() => HttpFileAssertions.AssertExpectationsMet(r))
            .Should().Throw<HttpFileAssertionException>()
            .WithMessage("*content type*");
    }

    [Fact]
    public void Parse_DirectiveQuotedInFileHeaderAboveBannerRule_DoesNotLeakOntoFirstRequest()
    {
        // A file header that documents the directive by quoting it must not thereby impose
        // it. The banner rule below the prose is decoration, and decoration ends a block.
        const string Content = """
            ###############################################################################
            # Every failure response asserts
            # @expect content-type: application/problem+json
            ###############################################################################

            ### List accounts
            GET https://localhost/api/accounts
            """;

        var requests = HttpFileParser.Parse(Content);

        requests.Should().ContainSingle();
        requests[0].Expected.Should().BeNull();
    }

    [Fact]
    public void Parse_StatusDirectiveQuotedInFileHeader_DoesNotLeakOntoFirstRequest()
    {
        const string Content = """
            ###############################################################################
            # @expect status: 404
            ###############################################################################

            ### List accounts
            GET https://localhost/api/accounts
            """;

        var requests = HttpFileParser.Parse(Content);

        requests.Should().ContainSingle();
        requests[0].Expected.Should().BeNull();
    }

    [Fact]
    public void Parse_MultipleCommentaryTitleLines_StillTitleAndKeepDirectives()
    {
        // The reset must key on decoration only: consecutive '### text' commentary lines
        // are a normal preamble and must keep both the title and the directives. The title
        // is pinned exactly because it is joined from every commentary line, which is worth
        // noticing if it ever changes.
        const string Content = """
            ### Get account - malformed id
            ### Rejected by the route constraint before the endpoint runs.
            # @expect status: 404
            GET https://localhost/api/accounts/not-a-guid
            """;

        var requests = HttpFileParser.Parse(Content);

        requests.Should().ContainSingle();
        requests[0].Title.Should().Be("Get account - malformed id / Rejected by the route constraint before the endpoint runs.");
        requests[0].Expected!.StatusMin.Should().Be(404);
    }
}
