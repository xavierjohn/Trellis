namespace Trellis.FluentValidation.Tests;

using Trellis.FluentValidation;

public class JsonPointerNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Email", "/Email")]
    [InlineData("Address.PostCode", "/Address/PostCode")]
    [InlineData("Address.Street.Line1", "/Address/Street/Line1")]
    [InlineData("Items[0]", "/Items/0")]
    [InlineData("Items[0].Sku", "/Items/0/Sku")]
    [InlineData("Lines[12].Address.Zip", "/Lines/12/Address/Zip")]
    [InlineData("Tags[abc]", "/Tags/abc")]
    [InlineData("/already/a/pointer", "/already/a/pointer")]
    public void ToJsonPointer_normalizes_property_paths(string? input, string expected)
        => JsonPointerNormalizer.ToJsonPointer(input).Should().Be(expected);

    [Theory]
    [InlineData("a~b", "/a~0b")]
    [InlineData("a/b", "/a~1b")]
    [InlineData("Items[a~b]", "/Items/a~0b")]
    [InlineData("Items[a/b]", "/Items/a~1b")]
    public void ToJsonPointer_escapes_reserved_characters_per_rfc6901(string input, string expected)
        => JsonPointerNormalizer.ToJsonPointer(input).Should().Be(expected);
}
