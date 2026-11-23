namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Collections.Generic;
using Trellis.Testing.AspNetCore.Http;

public class HttpFileParserTests
{
    [Fact]
    public void Parses_plain_request_method_url_headers_and_body()
    {
        const string content = """
            ### Create widget
            POST http://example.com/widgets
            Content-Type: application/json

            {
              "name": "Widget"
            }
            """;

        var reqs = HttpFileParser.Parse(content);

        reqs.Should().HaveCount(1);
        reqs[0].Title.Should().Be("Create widget");
        reqs[0].Method.Should().Be("POST");
        reqs[0].Url.Should().Be("http://example.com/widgets");
        reqs[0].Headers.Should().ContainKey("Content-Type").WhoseValue.Should().Be("application/json");
        reqs[0].Body.Should().Contain("\"name\"");
    }

    [Fact]
    public void Parse_MixedLineEndings_PreservesBodyAndDeferredTokens()
    {
        const string content = "@host = http://local\r\n@alias = {{$guid}}\n### Send\r\n" +
            "# @name sent\n  post   {{HOST}}/{{prior.response.status}} HTTP/1.1\r\n" +
            "X-Key: old\nx-key: {{alias}}:{{ host }}\r\n\r\n" +
            "  first\r\n    ### is body text\nlast\rinside\r\r\n\r\n" +
            "### Read\nGET {{host}}/{{sent.response.status}}\r\n";

        var requests = HttpFileParser.Parse(content);

        requests.Should().HaveCount(2);
        requests[0].Method.Should().Be("POST");
        requests[0].Url.Should().Be("http://local/{{prior.response.status}}");
        requests[0].Name.Should().Be("sent");
        requests[0].Headers.Should().ContainSingle().Which.Key.Should().Be("X-Key");
        requests[0].Headers["x-key"].Should().Be("{{$guid}}:http://local");
        requests[0].Body.Should().Be(string.Join(Environment.NewLine, "  first", "    ### is body text", "last\rinside"));
        requests[1].Url.Should().Be("http://local/{{sent.response.status}}");
        requests[1].Headers.Should().BeEmpty();
        requests[1].Body.Should().BeNull();
    }

    [Fact]
    public void Parse_IgnoredCommentLines_DoNotAllocateInProportionToInputSize()
    {
        const string request = "GET http://local/items";
        var withComments = string.Concat(Enumerable.Repeat("  // " + new string('x', 256) + "\r\n", 128)) + request;

        _ = HttpFileParser.Parse(withComments);
        var baseline = Math.Min(Measure(request), Measure(request));

        Math.Min(Measure(withComments), Measure(withComments)).Should().BeLessThanOrEqualTo(baseline + 4096);
    }

    [Fact]
    public void Parse_DuplicateHeaders_OnlySubstitutesTheLastValue()
    {
        var options = new Dictionary<string, string> { ["message"] = new string('x', 1024), ["final"] = "selected" };
        var template = "GET http://local/items\n" +
            string.Concat(Enumerable.Repeat("X-Note: {{missing}}\n", 16)) +
            "x-note: {{final}}";
        var withOverwrittenValues = template.Replace("{{missing}}", "{{message}}", StringComparison.Ordinal);

        var headers = HttpFileParser.Parse(withOverwrittenValues, options).Single().Headers;
        headers.Should().ContainSingle().Which.Value.Should().Be("selected");
        var baseline = Math.Min(Measure(template, options), Measure(template, options));
        var actual = Math.Min(Measure(withOverwrittenValues, options), Measure(withOverwrittenValues, options));

        actual.Should().BeLessThanOrEqualTo(baseline + 4096);
    }

    private static long Measure(string content, IReadOnlyDictionary<string, string>? vars = null)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(HttpFileParser.Parse(content, vars));
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void Captures_at_name_metadata()
    {
        const string content = """
            ### First
            # @name createThing
            POST http://x/y

            {}
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Name.Should().Be("createThing");
    }

    [Theory]
    [InlineData("201", 201, 201)]
    [InlineData("2xx", 200, 299)]
    [InlineData("200-299", 200, 299)]
    [InlineData("4xx", 400, 499)]
    public void Parses_expect_status_expressions(string expr, int min, int max)
    {
        var content = $"""
            ### Case
            # @expect status: {expr}
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Expected.Should().NotBeNull();
        reqs[0].Expected!.StatusMin.Should().Be(min);
        reqs[0].Expected!.StatusMax.Should().Be(max);
    }

    [Fact]
    public void Parses_expect_header_pragma()
    {
        const string content = """
            ### Case
            # @expect header: ETag
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Expected!.RequiredHeaders.Should().ContainSingle().Which.Should().Be("ETag");
    }

    [Fact]
    public void Substitutes_at_host_variable()
    {
        const string content = """
            @host = http://local

            ### Get
            GET {{host}}/items
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Url.Should().Be("http://local/items");
    }

    [Fact]
    public void External_vars_merge_but_are_overridden_by_file_vars()
    {
        const string content = """
            @host = http://file

            ### Get
            GET {{host}}/items
            """;
        var external = new Dictionary<string, string> { ["host"] = "http://external" };
        var reqs = HttpFileParser.Parse(content, external);
        reqs[0].Url.Should().Be("http://file/items");
    }

    [Fact]
    public void External_vars_are_used_when_no_file_level_override()
    {
        const string content = """
            ### Get
            GET {{host}}/items
            """;
        var reqs = HttpFileParser.Parse(content, new Dictionary<string, string> { ["host"] = "http://ext" });
        reqs[0].Url.Should().Be("http://ext/items");
    }

    [Fact]
    public void Defers_response_substitution_tokens_until_runtime()
    {
        const string content = """
            ### Second
            GET http://x/items/{{createOptional.response.body.id}}
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Url.Should().Contain("{{createOptional.response.body.id}}");
    }

    [Fact]
    public void Ignores_plain_comment_lines()
    {
        const string content = """
            ### First
            // Just a comment
            # Plain hash comment (not @name / @expect)
            GET http://x/

            """;
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().HaveCount(1);
        reqs[0].Url.Should().Be("http://x/");
        reqs[0].Name.Should().BeNull();
        reqs[0].Expected.Should().BeNull();
    }

    [Fact]
    public void Separates_multiple_requests_by_triple_hash()
    {
        const string content = """
            ### A
            GET http://x/a

            ### B
            GET http://x/b
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().HaveCount(2);
        reqs[0].Title.Should().Be("A");
        reqs[1].Title.Should().Be("B");
    }

    [Fact]
    public void Parses_parity_directive()
    {
        const string content = """
            ### Case
            ### @parity: status-only
            POST http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Title.Should().Be("Case");
        reqs[0].ParityMode.Should().Be("status-only");
    }

    [Fact]
    public void Parse_strips_leading_UTF8_BOM_so_first_line_variable_is_registered()
    {
        // Inspection finding N-TA-1: many editors save .http files with a UTF-8 BOM
        // (U+FEFF prefix). Without BOM stripping, the first line was misclassified —
        // `line.StartsWith("###")` returned false (line starts with U+FEFF then '#'),
        // `line[0] == '@'` returned false (line[0] is U+FEFF), so the line fell into
        // ParseRequestLine and produced a bogus request with method "\uFEFF@HOST" and
        // URL "=". The variable definition was lost; subsequent {{host}} substitutions
        // resolved against an empty bag.
        const char Bom = '\uFEFF';
        var content = Bom + """
            @host = http://localhost:62266
            ### Get root
            GET {{host}}/
            """;

        var reqs = HttpFileParser.Parse(content);

        reqs.Should().HaveCount(1, "the BOM-prefixed @host line must be recognized as a variable, not a bogus request");
        reqs[0].Title.Should().Be("Get root");
        reqs[0].Method.Should().Be("GET");
        reqs[0].Url.Should().Be("http://localhost:62266/", "the {{host}} substitution must succeed against the BOM-prefixed @host variable");
    }
}