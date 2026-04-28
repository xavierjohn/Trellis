namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Collections.Generic;
using System.IO;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Extra branch coverage for <see cref="HttpFileParser"/>: malformed pragmas, decoration-only
/// separators, parity-as-comment metadata, ParseFile via disk, file-level title appending, and
/// graceful handling of a malformed request line.
/// </summary>
public class HttpFileParserExtraTests
{
    [Fact]
    public void Parse_throws_on_null_content()
        => FluentActions.Invoking(() => HttpFileParser.Parse(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Decoration_only_separator_is_ignored_for_title()
    {
        const string content = """
            ### Case
            ### ════════════════════
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().HaveCount(1);
        reqs[0].Title.Should().Be("Case");
    }

    [Fact]
    public void Multiple_separators_append_titles_with_slash()
    {
        const string content = """
            ### A
            ### B
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Title.Should().Be("A / B");
    }

    [Fact]
    public void Inline_parity_directive_via_separator_does_not_create_request()
    {
        const string content = """
            ### @parity: hosts-differ
            ### Case
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().HaveCount(1);
        reqs[0].ParityMode.Should().Be("hosts-differ");
        reqs[0].Title.Should().Be("Case");
    }

    [Fact]
    public void Inline_unknown_directive_via_separator_is_ignored()
    {
        const string content = """
            ### @other: ignore-me
            ### Case
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Title.Should().Be("Case");
        reqs[0].ParityMode.Should().BeNull();
    }

    [Fact]
    public void Malformed_request_line_is_skipped()
    {
        // Only one token on the request line — parser treats it as malformed.
        const string content = """
            ### X
            GET
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().BeEmpty();
    }

    [Fact]
    public void Header_with_no_value_is_kept_as_empty_string()
    {
        const string content = """
            ### X
            GET http://x/
            X-Empty:
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Headers.Should().ContainKey("X-Empty").WhoseValue.Should().BeEmpty();
    }

    [Fact]
    public void Body_in_default_request_falls_through_to_separator()
    {
        const string content = "### A\nGET http://x/\n\nbody1\n\n### B\nGET http://y/\n";
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().HaveCount(2);
        reqs[0].Body.Should().Contain("body1");
        reqs[1].Body.Should().BeNull();
    }

    [Fact]
    public void Expect_pragma_without_colon_is_ignored()
    {
        const string content = """
            ### X
            # @expect noargs
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Expected.Should().BeNull();
    }

    [Fact]
    public void Expect_unknown_status_expression_is_ignored()
    {
        const string content = """
            ### X
            # @expect status: notanumber
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Expected.Should().BeNull();
    }

    [Fact]
    public void Expect_header_with_empty_value_is_ignored()
    {
        const string content = """
            ### X
            # @expect header:
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Expected.Should().BeNull();
    }

    [Fact]
    public void Body_substitution_uses_file_vars()
    {
        const string content = """
            @who = world

            ### X
            POST http://x/
            Content-Type: text/plain

            hello {{who}}
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Body.Should().Be("hello world");
    }

    [Fact]
    public void Header_value_substitution_uses_file_vars()
    {
        const string content = """
            @v = 42

            ### X
            GET http://x/
            X-V: {{v}}
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Headers["X-V"].Should().Be("42");
    }

    [Fact]
    public void Variable_line_with_invalid_syntax_is_ignored()
    {
        // No '=' so the regex fails and the line is silently skipped.
        const string content = """
            @notavar

            ### X
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().HaveCount(1);
    }

    [Fact]
    public void Title_defaults_to_request_index_when_separator_blank()
    {
        const string content = "GET http://x/\n";
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().HaveCount(1);
        reqs[0].Title.Should().Be("Request 1");
    }

    [Fact]
    public void ParseFile_throws_on_null_path()
        => FluentActions.Invoking(() => HttpFileParser.ParseFile(null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void ParseFile_reads_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trellis-httpfile-{Guid.NewGuid():N}.http");
        try
        {
            File.WriteAllText(path, "### Disk\nGET http://x/disk\n");
            var reqs = HttpFileParser.ParseFile(path);
            reqs.Should().HaveCount(1);
            reqs[0].Url.Should().Be("http://x/disk");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Substitute_static_vars_leaves_token_with_dot_intact()
    {
        // Tokens with '.' are deferred to the runner — even when the token
        // matches a known variable name, they are not substituted at parse time.
        const string content = """
            @host = example.com

            ### X
            GET http://{{host}}/{{create.response.body.id}}
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Url.Should().Contain("example.com");
        reqs[0].Url.Should().Contain("{{create.response.body.id}}");
    }

    [Fact]
    public void Substitute_static_vars_leaves_unmatched_braces_alone()
    {
        const string content = """
            ### X
            GET http://x/{{ unclosed
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Url.Should().Contain("{{");
    }
}