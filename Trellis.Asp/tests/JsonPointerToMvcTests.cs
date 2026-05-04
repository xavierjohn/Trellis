namespace Trellis.Asp.Tests;

using Trellis.Asp;

/// <summary>
/// Direct unit tests for <see cref="JsonPointerToMvc.Translate(string)"/>.
///
/// <para>
/// End-to-end coverage already exists in <see cref="ResponseFailureWriterTests"/> via the
/// writer's <c>GroupBy</c> path; these tests pin the algorithm itself so coverage tooling
/// attributes hits to the translator file regardless of where the writer's lambda is
/// inlined or evaluated.
/// </para>
/// </summary>
public sealed class JsonPointerToMvcTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("/email", "email")]
    [InlineData("/items/0/name", "items[0].name")]
    [InlineData("/Lines/0/Memo", "Lines[0].Memo")]
    [InlineData("/Metadata/Reference", "Metadata.Reference")]
    [InlineData("/0", "[0]")]
    [InlineData("/0/inner", "[0].inner")]
    [InlineData("/items/01", "items.01")]
    [InlineData("/items/-/name", "items.-.name")]
    [InlineData("/a~1b", "a/b")]
    [InlineData("/a~0b", "a~b")]
    [InlineData("/a~01", "a~1")]
    [InlineData("/items/12345/name", "items[12345].name")]
    public void Translate_returns_expected_mvc_field_key(string pointerPath, string expected)
    {
        var actual = JsonPointerToMvc.Translate(pointerPath);

        actual.Should().Be(expected);
    }

    [Fact]
    public void Translate_returns_empty_for_null_input()
    {
        var actual = JsonPointerToMvc.Translate(null!);

        actual.Should().Be(string.Empty);
    }

    [Fact]
    public void Translate_keeps_input_unchanged_when_no_leading_slash()
    {
        // Defensive: InputPointer guarantees a leading '/', but the translator
        // must not crash if a caller bypasses that contract.
        var actual = JsonPointerToMvc.Translate("email");

        actual.Should().Be("email");
    }
}
