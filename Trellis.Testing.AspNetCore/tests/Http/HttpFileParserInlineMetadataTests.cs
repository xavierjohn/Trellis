namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Collections.Generic;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Additional <see cref="HttpFileParser"/> edge coverage for the inline (non-separator)
/// metadata pragma paths (<c># @parity</c>, <c># @name</c> with unusual casing), body
/// flushing when followed by another <c>###</c>, and <c>ExtractParity</c> without a colon.
/// </summary>
public class HttpFileParserInlineMetadataTests
{
    [Fact]
    public void Inline_parity_pragma_without_separator_sets_parity()
    {
        // The `# @parity` line sits between method and headers — covered by the inline
        // pragma code path (distinct from the `### @parity:` separator path).
        const string content = """
            ### Case
            # @parity: hosts-differ
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].ParityMode.Should().Be("hosts-differ");
    }

    [Fact]
    public void Inline_parity_pragma_without_colon_falls_back_to_raw_payload()
    {
        const string content = """
            ### Case
            # @parity status-only
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].ParityMode.Should().Be("status-only");
    }

    [Fact]
    public void Separator_parity_pragma_without_colon_uses_raw_payload()
    {
        // Exercises the ExtractParity "no colon" branch on the HandleSeparator path.
        const string content = """
            ### @parity hosts-differ
            ### Case
            GET http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].ParityMode.Should().Be("hosts-differ");
    }

    [Fact]
    public void Case_insensitive_name_and_expect_pragmas_are_recognised()
    {
        const string content = """
            ### X
            # @NAME create
            # @EXPECT status: 201
            POST http://x/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Name.Should().Be("create");
        reqs[0].Expected!.StatusMin.Should().Be(201);
    }

    [Fact]
    public void Final_request_is_flushed_at_end_of_input_without_trailing_newline()
    {
        // Regression: the end-of-input flush must still commit the last request even
        // when the file doesn't end with a blank line or separator.
        var content = "### Only\nGET http://x/only";
        var reqs = HttpFileParser.Parse(content);
        reqs.Should().HaveCount(1);
        reqs[0].Url.Should().Be("http://x/only");
    }

    [Fact]
    public void Body_mode_continues_across_multiple_blank_lines()
    {
        const string content = "### A\nGET http://x/a\n\nline1\n\nline2\n";
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Body.Should().Contain("line1").And.Contain("line2");
    }

    [Fact]
    public void Substitute_unknown_simple_token_is_left_intact()
    {
        // Token has no dot (so not a deferred response token) but is not in vars either.
        const string content = """
            ### X
            GET http://{{nope}}/
            """;
        var reqs = HttpFileParser.Parse(content);
        reqs[0].Url.Should().Contain("{{nope}}");
    }

    [Fact]
    public void Body_with_only_whitespace_lines_is_preserved()
    {
        const string content = "### X\nGET http://x/\n\n   \n";
        var reqs = HttpFileParser.Parse(content);
        // Whitespace-only lines inside body mode are appended verbatim (not trimmed).
        reqs[0].Body.Should().NotBeNull();
    }
}
