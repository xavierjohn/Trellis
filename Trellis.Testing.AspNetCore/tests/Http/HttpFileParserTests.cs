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
}
