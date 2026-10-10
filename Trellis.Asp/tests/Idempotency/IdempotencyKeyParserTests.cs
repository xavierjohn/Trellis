namespace Trellis.Asp.Tests.Idempotency;

using Trellis.Asp.Idempotency;

/// <summary>
/// Pins RFC 9651 parsing with a String-valued Item as the idempotency key.
/// </summary>
public sealed class IdempotencyKeyParserTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("9d6f6c44-1234-5678-9abc-def012345678")]
    [InlineData("a.b_c-d~e!f#g$h%i&j'k*l+m^n`o|p")]
    public void TryParse_BareToken_ReturnsFalse(string token)
    {
        IdempotencyKeyParser.TryParse(token, "Idempotency-Key", out var parsed, out var error).Should().BeFalse();
        parsed.Should().BeEmpty();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("\"\"", "")]
    [InlineData("\"abc\"", "abc")]
    [InlineData("\"hello world\"", "hello world")]
    [InlineData("\"a:b\"", "a:b")]
    [InlineData("\"a/b\"", "a/b")]
    [InlineData("\"?1\"", "?1")]
    public void Quoted_string_returns_content_without_quotes(string quoted, string content)
    {
        IdempotencyKeyParser.TryParse(quoted, "Idempotency-Key", out var parsed, out var error).Should().BeTrue();
        parsed.Should().Be(content);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(" \"abc\"", "abc")]
    [InlineData("\"abc\" ", "abc")]
    [InlineData("  \" abc \"  ", " abc ")]
    [InlineData("  \"\"  ", "")]
    public void TryParse_StringItem_WithOuterSpaces_PreservesStringValue(string raw, string expected)
    {
        IdempotencyKeyParser.TryParse(raw, "Idempotency-Key", out var parsed, out var error).Should().BeTrue();
        parsed.Should().Be(expected);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("\"key\";flag")]
    [InlineData("\"key\"; flag=?1")]
    [InlineData("\"key\";  flag=?0")]
    [InlineData("\"key\";a=?0;a=?1;a")]
    [InlineData("\"key\";a0._-*=token")]
    [InlineData("\"key\";*=*")]
    [InlineData("\"key\";text=\"\"")]
    [InlineData("\"key\";text=\"a\\\"b\\\\c\"")]
    [InlineData("\"key\";token=AbC/a:b")]
    [InlineData("\"key\";token=*!#$%&'*+-.^_`|~:/")]
    [InlineData("\"key\";bytes=::")]
    [InlineData("\"key\";bytes=:YQ==:")]
    [InlineData("\"key\";bytes=:YQ:")]
    [InlineData("\"key\";bytes=:YQ=:")]
    [InlineData("\"key\";bytes=:YWI:")]
    [InlineData("\"key\";bytes=:YWJj:")]
    [InlineData("\"key\";bytes=:YR==:")]
    [InlineData(" \"key\";n=42;text=\"note\";token=AbC/a:b;bytes=:YQ:;flag  ")]
    public void TryParse_StringItem_WithParameters_ReturnsStringValue(string raw)
    {
        IdempotencyKeyParser.TryParse(raw, "Idempotency-Key", out var parsed, out var error).Should().BeTrue();
        parsed.Should().Be("key");
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("012")]
    [InlineData("999999999999999")]
    [InlineData("-999999999999999")]
    [InlineData("0.0")]
    [InlineData("-0.000")]
    [InlineData("000000000000.0")]
    [InlineData("999999999999.999")]
    [InlineData("-999999999999.999")]
    public void TryParse_NumberParameter_WithinRfcBounds_ReturnsTrue(string number)
    {
        IdempotencyKeyParser.TryParse($"\"key\";number={number}", "Idempotency-Key", out var parsed, out var error)
            .Should().BeTrue();
        parsed.Should().Be("key");
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("@0")]
    [InlineData("@-0")]
    [InlineData("@012")]
    [InlineData("@1659578233")]
    [InlineData("@-62135596800")]
    [InlineData("@253402214400")]
    [InlineData("@999999999999999")]
    [InlineData("@-999999999999999")]
    [InlineData("@000000000000000")]
    public void TryParse_DateParameter_WithinRfcBounds_ReturnsStringValue(string date)
    {
        IdempotencyKeyParser.TryParse($"\"key\";date={date}", "Idempotency-Key", out var parsed, out var error)
            .Should().BeTrue();
        parsed.Should().Be("key");
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@-")]
    [InlineData("@+1")]
    [InlineData("@ 1")]
    [InlineData("@\t1")]
    [InlineData("@@1")]
    [InlineData("@1.0")]
    [InlineData("@.1")]
    [InlineData("@1000000000000000")]
    [InlineData("@-1000000000000000")]
    [InlineData("@0000000000000000")]
    [InlineData("@1e2")]
    public void TryParse_DateParameter_InvalidSyntax_ReturnsFalse(string date)
    {
        IdempotencyKeyParser.TryParse($"\"key\";date={date}", "Idempotency-Key", out var parsed, out var error)
            .Should().BeFalse();
        parsed.Should().BeEmpty();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("%\"\"")]
    [InlineData("%\"hello world\"")]
    [InlineData("%\"%25%22\"")]
    [InlineData("%\"a\\b\"")]
    [InlineData("%\"%c3%bc\"")]
    [InlineData("%\"%e4%b8%ad\"")]
    [InlineData("%\"%f0%9f%98%80\"")]
    [InlineData("%\"%c2%80\"")]
    [InlineData("%\"%e0%a0%80\"")]
    [InlineData("%\"%f0%90%80%80\"")]
    [InlineData("%\"%f4%8f%bf%bf\"")]
    [InlineData("%\"%00%1f%7f\"")]
    [InlineData("%\"%ef%bf%be%ef%bf%bf\"")]
    [InlineData("%\"%e2%80%ae\"")]
    public void TryParse_DisplayStringParameter_ValidUtf8_ReturnsStringValue(string display)
    {
        IdempotencyKeyParser.TryParse($"\"key\";display={display}", "Idempotency-Key", out var parsed, out var error)
            .Should().BeTrue();
        parsed.Should().Be("key");
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("%")]
    [InlineData("%text")]
    [InlineData("%\"unterminated")]
    [InlineData("%\"%\"")]
    [InlineData("%\"%0\"")]
    [InlineData("%\"%gg\"")]
    [InlineData("%\"%c3%BC\"")]
    [InlineData("%\"%C3%bc\"")]
    [InlineData("%\"raw\tcontrol\"")]
    [InlineData("%\"raw\ncontrol\"")]
    [InlineData("%\"raw\u007f\"")]
    [InlineData("%\"raw\u0080\"")]
    [InlineData("%\"%80\"")]
    [InlineData("%\"%c3\"")]
    [InlineData("%\"%c3%28\"")]
    [InlineData("%\"%c0%af\"")]
    [InlineData("%\"%e0%80%af\"")]
    [InlineData("%\"%ed%a0%80\"")]
    [InlineData("%\"%ed%bf%bf\"")]
    [InlineData("%\"%f0%80%80%af\"")]
    [InlineData("%\"%f4%90%80%80\"")]
    [InlineData("%\"%f5%80%80%80\"")]
    [InlineData("%\"%f0%9f%98\"")]
    [InlineData("%\"text\"junk")]
    public void TryParse_DisplayStringParameter_InvalidEncoding_ReturnsFalse(string display)
    {
        IdempotencyKeyParser.TryParse($"\"key\";display={display}", "Idempotency-Key", out var parsed, out var error)
            .Should().BeFalse();
        parsed.Should().BeEmpty();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryParse_DisplayStringParameter_AllPrintableAscii_ReturnsStringValue()
    {
        for (var codePoint = 0x20; codePoint <= 0x7E; codePoint++)
        {
            var character = (char)codePoint;
            var encoded = character is '%' or '"' ? $"%{codePoint:x2}" : character.ToString();
            var raw = $"\"key\";display=%\"{encoded}\"";

            IdempotencyKeyParser.TryParse(raw, "Idempotency-Key", out var parsed, out _)
                .Should().BeTrue("0x{0:X2} is valid display content", codePoint);
            parsed.Should().Be("key");
        }
    }

    [Fact]
    public void TryParse_Parameters_SupportedMinimums_Accepts256ParametersAnd64CharacterKeys()
    {
        var parameters = string.Concat(Enumerable.Range(0, 256).Select(index => $";p{index}"));
        var raw = $"\"key\";{new string('p', 64)}=?1{parameters}";

        IdempotencyKeyParser.TryParse(raw, "Idempotency-Key", out var parsed, out _).Should().BeTrue();
        parsed.Should().Be("key");
    }

    [Fact]
    public void TryParse_String_AllPrintableAscii_RoundTrips()
    {
        for (var codePoint = 0x20; codePoint <= 0x7E; codePoint++)
        {
            var character = (char)codePoint;
            var raw = character is '"' or '\\' ? $"\"\\{character}\"" : $"\"{character}\"";

            IdempotencyKeyParser.TryParse(raw, "Idempotency-Key", out var parsed, out _)
                .Should().BeTrue("0x{0:X2} is printable ASCII", codePoint);
            parsed.Should().Be(character.ToString());
        }
    }

    [Fact]
    public void TryParse_String_SupportedMinimum_Accepts1024DecodedCharacters()
    {
        var content = new string('a', 1024);

        IdempotencyKeyParser.TryParse($"\"{content}\"", "Idempotency-Key", out var parsed, out _).Should().BeTrue();
        parsed.Should().Be(content);
    }

    [Fact]
    public void Quoted_string_with_escaped_backslash_and_quote_is_unescaped()
    {
        IdempotencyKeyParser.TryParse("\"a\\\"b\\\\c\"", "Idempotency-Key", out var parsed, out _).Should().BeTrue();
        parsed.Should().Be("a\"b\\c");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\"")]
    [InlineData("\"unterminated")]
    [InlineData("with space outside quotes")]
    [InlineData("ünicode")]
    [InlineData("\"\\x\"")]
    [InlineData("\"abc\"junk\"")]
    [InlineData("\"a\"b\"")]
    [InlineData("\"abc\\")]
    [InlineData("\"abc\\\"")]
    [InlineData("\"\\n\"")]
    [InlineData("\"\\u0041\"")]
    [InlineData("\"a\tb\"")]
    [InlineData("\"a\rb\"")]
    [InlineData("\"a\nb\"")]
    [InlineData("\"a\0b\"")]
    [InlineData("\"a\u001fb\"")]
    [InlineData("\"a\u007fb\"")]
    [InlineData("\"ünicode\"")]
    [InlineData("\t\"abc\"")]
    [InlineData("\"abc\"\t")]
    [InlineData("'abc'")]
    [InlineData("\"abc\", \"def\"")]
    [InlineData("(\"abc\")")]
    [InlineData("a=\"abc\"")]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("42")]
    [InlineData("1.2")]
    [InlineData(":YWJj:")]
    [InlineData("?0")]
    [InlineData("?1")]
    [InlineData("@0")]
    [InlineData("%\"text\"")]
    [InlineData("%\"%c3%bc\"")]
    [InlineData("\"key\"junk")]
    [InlineData("\"key\" ;flag")]
    [InlineData("\"key\";")]
    [InlineData("\"key\"; ")]
    [InlineData("\"key\";;flag")]
    [InlineData("\"key\";\tflag")]
    [InlineData("\"key\";Flag")]
    [InlineData("\"key\";9flag")]
    [InlineData("\"key\";_flag")]
    [InlineData("\"key\";flag/value")]
    [InlineData("\"key\";fläg")]
    [InlineData("\"key\";flag =?1")]
    [InlineData("\"key\";flag= ?1")]
    [InlineData("\"key\";flag=")]
    [InlineData("\"key\";flag=?")]
    [InlineData("\"key\";flag=?2")]
    [InlineData("\"key\";flag=?10")]
    [InlineData("\"key\";flag=?1 ;other")]
    [InlineData("\"key\";text=\"unterminated")]
    [InlineData("\"key\";text=\"\\n\"")]
    [InlineData("\"key\";text=\"ünicode\"")]
    [InlineData("\"key\";token=!token")]
    [InlineData("\"key\";token=token?")]
    [InlineData("\"key\";number=-")]
    [InlineData("\"key\";number=+1")]
    [InlineData("\"key\";number=1000000000000000")]
    [InlineData("\"key\";number=-1000000000000000")]
    [InlineData("\"key\";number=1000000000000.0")]
    [InlineData("\"key\";number=1.")]
    [InlineData("\"key\";number=.1")]
    [InlineData("\"key\";number=1.0000")]
    [InlineData("\"key\";number=1e2")]
    [InlineData("\"key\";number=1.2.3")]
    [InlineData("\"key\";bytes=:YQ")]
    [InlineData("\"key\";bytes=:A:")]
    [InlineData("\"key\";bytes=:====:")]
    [InlineData("\"key\";bytes=:Y=Q=:")]
    [InlineData("\"key\";bytes=:YQ===:")]
    [InlineData("\"key\";bytes=:Y Q==:")]
    [InlineData("\"key\";bytes=:YQ-_:")]
    [InlineData("\"key\";bytes=:YQ==:\r")]
    [InlineData("\"key\";list=(1 2)")]
    public void Invalid_inputs_return_false(string? input)
    {
        IdempotencyKeyParser.TryParse(input, "Idempotency-Key", out var parsed, out var error).Should().BeFalse();
        parsed.Should().BeEmpty();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryParse_RawLimit_AtAndAboveBoundary_RejectsOnlyOversizedValue()
    {
        IdempotencyKeyParser.TryParse($"\"{new string('a', 4094)}\"", "Idempotency-Key", out var parsed, out _)
            .Should().BeTrue();
        parsed.Length.Should().Be(4094);

        IdempotencyKeyParser.TryParse($"\"{new string('a', 4095)}\"", "Idempotency-Key", out parsed, out var error)
            .Should().BeFalse();
        parsed.Should().BeEmpty();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryParse_OversizedInput_ReturnsFalseQuickly()
    {
        var oversized = $"\"{new string('a', 8 * 1024)}\"";
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