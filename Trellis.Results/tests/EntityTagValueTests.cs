namespace Trellis.Results.Tests;

using Trellis.Testing;

public class EntityTagValueTests
{
    #region Factory Methods

    [Fact]
    public void Strong_creates_strong_entity_tag()
    {
        var tag = EntityTagValue.Strong("abc123");

        tag.OpaqueTag.Should().Be("abc123");
        tag.IsWeak.Should().BeFalse();
    }

    [Fact]
    public void Weak_creates_weak_entity_tag()
    {
        var tag = EntityTagValue.Weak("abc123");

        tag.OpaqueTag.Should().Be("abc123");
        tag.IsWeak.Should().BeTrue();
    }

    #endregion

    #region TryParse

    [Fact]
    public void TryParse_strong_tag()
    {
        var result = EntityTagValue.TryParse("\"abc123\"");

        var tag = result.Should().BeSuccess().Which;
        tag.OpaqueTag.Should().Be("abc123");
        tag.IsWeak.Should().BeFalse();
    }

    [Fact]
    public void TryParse_weak_tag()
    {
        var result = EntityTagValue.TryParse("W/\"abc123\"");

        var tag = result.Should().BeSuccess().Which;
        tag.OpaqueTag.Should().Be("abc123");
        tag.IsWeak.Should().BeTrue();
    }

    [Fact]
    public void TryParse_empty_strong_tag()
    {
        var result = EntityTagValue.TryParse("\"\"");

        var tag = result.Should().BeSuccess().Which;
        tag.OpaqueTag.Should().BeEmpty();
        tag.IsWeak.Should().BeFalse();
    }

    [Fact]
    public void TryParse_empty_weak_tag()
    {
        var result = EntityTagValue.TryParse("W/\"\"");

        var tag = result.Should().BeSuccess().Which;
        tag.OpaqueTag.Should().BeEmpty();
        tag.IsWeak.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("W/abc")]
    [InlineData("\"")]
    public void TryParse_invalid_input_returns_failure(string? input)
    {
        var result = EntityTagValue.TryParse(input);

        result.Should().BeFailure();
    }

    [Fact]
    public void TryParse_round_trips_strong_tag()
    {
        var original = EntityTagValue.Strong("v42");
        var result = EntityTagValue.TryParse(original.ToHeaderValue());

        result.Should().BeSuccess().Which.Should().Be(original);
    }

    [Fact]
    public void TryParse_round_trips_weak_tag()
    {
        var original = EntityTagValue.Weak("v42");
        var result = EntityTagValue.TryParse(original.ToHeaderValue());

        result.Should().BeSuccess().Which.Should().Be(original);
    }

    #endregion

    #region StrongEquals

    [Fact]
    public void StrongEquals_both_strong_same_tag_returns_true()
    {
        var a = EntityTagValue.Strong("v1");
        var b = EntityTagValue.Strong("v1");

        a.StrongEquals(b).Should().BeTrue();
    }

    [Fact]
    public void StrongEquals_one_weak_returns_false()
    {
        var strong = EntityTagValue.Strong("v1");
        var weak = EntityTagValue.Weak("v1");

        strong.StrongEquals(weak).Should().BeFalse();
        weak.StrongEquals(strong).Should().BeFalse();
    }

    [Fact]
    public void StrongEquals_different_tags_returns_false()
    {
        var a = EntityTagValue.Strong("v1");
        var b = EntityTagValue.Strong("v2");

        a.StrongEquals(b).Should().BeFalse();
    }

    #endregion

    #region WeakEquals

    [Fact]
    public void WeakEquals_same_tag_both_strong_returns_true()
    {
        var a = EntityTagValue.Strong("v1");
        var b = EntityTagValue.Strong("v1");

        a.WeakEquals(b).Should().BeTrue();
    }

    [Fact]
    public void WeakEquals_same_tag_one_weak_returns_true()
    {
        var strong = EntityTagValue.Strong("v1");
        var weak = EntityTagValue.Weak("v1");

        strong.WeakEquals(weak).Should().BeTrue();
        weak.WeakEquals(strong).Should().BeTrue();
    }

    [Fact]
    public void WeakEquals_different_tags_returns_false()
    {
        var a = EntityTagValue.Strong("v1");
        var b = EntityTagValue.Weak("v2");

        a.WeakEquals(b).Should().BeFalse();
    }

    #endregion

    #region ToHeaderValue and ToString

    [Fact]
    public void ToHeaderValue_strong_tag()
    {
        var tag = EntityTagValue.Strong("abc123");

        tag.ToHeaderValue().Should().Be("\"abc123\"");
    }

    [Fact]
    public void ToHeaderValue_weak_tag()
    {
        var tag = EntityTagValue.Weak("abc123");

        tag.ToHeaderValue().Should().Be("W/\"abc123\"");
    }

    [Fact]
    public void ToString_matches_ToHeaderValue()
    {
        var strong = EntityTagValue.Strong("v1");
        var weak = EntityTagValue.Weak("v1");

        strong.ToString().Should().Be(strong.ToHeaderValue());
        weak.ToString().Should().Be(weak.ToHeaderValue());
    }

    #endregion

    #region Record Equality

    [Fact]
    public void Record_equality_same_tag_and_weakness()
    {
        var a = EntityTagValue.Strong("abc");
        var b = EntityTagValue.Strong("abc");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Record_equality_different_weakness()
    {
        var strong = EntityTagValue.Strong("abc");
        var weak = EntityTagValue.Weak("abc");

        strong.Should().NotBe(weak);
        (strong == weak).Should().BeFalse();
    }

    [Fact]
    public void Record_equality_different_tags()
    {
        var a = EntityTagValue.Strong("abc");
        var b = EntityTagValue.Strong("xyz");

        a.Should().NotBe(b);
    }

    #endregion

    #region Opaque Tag Validation

    [Theory]
    [InlineData("abc123")]
    [InlineData("v1.2.3")]
    [InlineData("!#$%&'()*+,-./:;<=>?@[]^_`{|}~")]
    [InlineData("")]
    public void Strong_with_valid_characters_succeeds(string opaqueTag)
    {
        var tag = EntityTagValue.Strong(opaqueTag);

        tag.OpaqueTag.Should().Be(opaqueTag);
    }

    [Fact]
    public void Strong_with_double_quote_throws_ArgumentException()
    {
        var act = () => EntityTagValue.Strong("abc\"def");

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("opaqueTag");
    }

    [Fact]
    public void Strong_with_backslash_throws_ArgumentException()
    {
        var act = () => EntityTagValue.Strong("abc\\def");

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("opaqueTag");
    }

    [Fact]
    public void Strong_with_control_character_throws_ArgumentException()
    {
        var controlChar = (char)0x01;
        var act = () => EntityTagValue.Strong($"abc{controlChar}def");

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("opaqueTag");
    }

    #endregion

    #region Wildcard

    [Fact]
    public void Wildcard_creates_wildcard_entity_tag()
    {
        var tag = EntityTagValue.Wildcard();

        tag.OpaqueTag.Should().Be("*");
        tag.IsWeak.Should().BeFalse();
        tag.IsWildcard.Should().BeTrue();
    }

    [Fact]
    public void Wildcard_ToHeaderValue_returns_unquoted_asterisk()
    {
        var tag = EntityTagValue.Wildcard();

        tag.ToHeaderValue().Should().Be("*");
    }

    [Fact]
    public void Wildcard_is_distinct_from_Strong_star()
    {
        var wildcard = EntityTagValue.Wildcard();
        var literal = EntityTagValue.Strong("*");

        wildcard.Should().NotBe(literal);
        wildcard.IsWildcard.Should().BeTrue();
        literal.IsWildcard.Should().BeFalse();
        literal.ToHeaderValue().Should().Be("\"*\"");
    }

    [Fact]
    public void Strong_star_is_not_wildcard()
    {
        var tag = EntityTagValue.Strong("*");

        tag.OpaqueTag.Should().Be("*");
        tag.IsWeak.Should().BeFalse();
        tag.IsWildcard.Should().BeFalse();
    }

    [Fact]
    public void Strong_and_Weak_are_not_wildcard()
    {
        EntityTagValue.Strong("abc").IsWildcard.Should().BeFalse();
        EntityTagValue.Weak("abc").IsWildcard.Should().BeFalse();
    }

    #endregion
}
