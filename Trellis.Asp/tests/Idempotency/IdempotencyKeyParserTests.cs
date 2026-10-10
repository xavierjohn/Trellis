namespace Trellis.Asp.Tests.Idempotency;

using Trellis.Asp.Idempotency;

/// <summary>
/// Pins Trellis's compatibility key grammar: nonempty bare RFC 7230 tokens or nonempty
/// quoted printable-ASCII strings with only <c>\\</c> and <c>\"</c> escapes.
/// This is not strict RFC 8941 <c>sf-string</c> or <c>sf-token</c> parsing.
/// </summary>
public sealed class IdempotencyKeyParserTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("9d6f6c44-1234-5678-9abc-def012345678")]
    [InlineData("a.b_c-d~e!f#g$h%i&j'k*l+m^n`o|p")]
    public void Bare_token_is_returned_verbatim(string token)
    {
        IdempotencyKeyParser.TryParse(token, "Idempotency-Key", out var parsed, out _).Should().BeTrue();
        parsed.Should().Be(token);
    }

    [Theory]
    [InlineData("\"hello world\"", "hello world")]
    [InlineData("\"a:b\"", "a:b")]
    [InlineData("\"a/b\"", "a/b")]
    [InlineData("\"?1\"", "?1")]
    public void Quoted_string_returns_content_without_quotes(string quoted, string content)
    {
        IdempotencyKeyParser.TryParse(quoted, "Idempotency-Key", out var parsed, out _).Should().BeTrue();
        parsed.Should().Be(content);
    }

    [Fact]
    public void Quoted_string_with_escaped_backslash_and_quote_is_unescaped()
    {
        IdempotencyKeyParser.TryParse("\"a\\\"b\\\\c\"", "Idempotency-Key", out var parsed, out _).Should().BeTrue();
        parsed.Should().Be("a\"b\\c");
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"unterminated")]
    [InlineData("with space outside quotes")]
    [InlineData("ünicode")]
    [InlineData("\"\\x\"")]
    [InlineData("\"abc\"junk\"")]
    [InlineData("\"a\"b\"")]
    [InlineData("\"\"")]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("?1")]
    public void Invalid_inputs_return_false(string input)
    {
        IdempotencyKeyParser.TryParse(input, "Idempotency-Key", out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryParse_OversizedInput_ReturnsFalseQuickly()
    {
        var oversized = new string('a', 8 * 1024);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = IdempotencyKeyParser.TryParse(oversized, "Idempotency-Key", out _, out _);
        sw.Stop();

        ok.Should().BeFalse();
        sw.ElapsedMilliseconds.Should().BeLessThan(100, "rejection must be O(1)-ish, not proportional to input length");
    }

    [Fact]
    public void Diagnostic_message_uses_configured_header_name()
    {
        IdempotencyKeyParser.TryParse("bad key with space", "X-Custom-Idem-Key", out _, out var error).Should().BeFalse();
        error.Should().Contain("X-Custom-Idem-Key");
        error.Should().NotContain("Idempotency-Key");
    }
}