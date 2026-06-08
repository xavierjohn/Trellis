namespace Trellis.FluentValidation.Tests;

using Trellis.FluentValidation;

public class JsonPointerNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Email", "/email")]
    [InlineData("Price.Amount", "/price/amount")]
    [InlineData("Address.PostCode", "/address/postCode")]
    [InlineData("Address.Street.Line1", "/address/street/line1")]
    [InlineData("Items[0]", "/items/0")]
    [InlineData("Items[0].Sku", "/items/0/sku")]
    [InlineData("Lines[12].Address.Zip", "/lines/12/address/zip")]
    [InlineData("Tags[abc]", "/tags/abc")]
    [InlineData("/already/a/pointer", "/already/a/pointer")]
    public void ToJsonPointer_normalizes_property_paths_to_camel_case(string? input, string expected)
        => JsonPointerNormalizer.ToJsonPointer(input).Should().Be(expected);

    [Theory]
    [InlineData("a~b", "/a~0b")]
    [InlineData("a/b", "/a~1b")]
    [InlineData("Field~Name", "/field~0Name")]
    [InlineData("Path/With/Slash", "/path~1With~1Slash")]
    [InlineData("Items[a~b]", "/items/a~0b")]
    [InlineData("Items[a/b]", "/items/a~1b")]
    public void ToJsonPointer_escapes_reserved_characters_per_rfc6901(string input, string expected)
        => JsonPointerNormalizer.ToJsonPointer(input).Should().Be(expected);
}