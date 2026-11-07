namespace Trellis.Http.Abstractions.Tests;

using Trellis.Testing;

/// <summary>
/// RFC 9110 section 8.8.1 defines <c>etagc = %x21 / %x23-7E / obs-text</c>, with obs-text being 0x80-0xFF.
/// Each boundary is checked on both sides, through the factories (which throw) and through
/// <see cref="EntityTagValue.TryParse"/> (which returns <c>EtagMalformed</c>).
/// The wildcard is a precondition token, never a tag, so it must never compare equal to any tag.
/// </summary>
public class EntityTagValueBoundaryTests
{
    public static TheoryData<int> AcceptedCodePoints => new() { 0x21, 0x23, 0x7E, 0x80, 0xFF };

    public static TheoryData<int> RejectedCodePoints => new() { 0x20, 0x22, 0x7F, 0x100 };

    private static string Tag(int codePoint) => $"a{(char)codePoint}b";

    private static void ShouldBeMalformed(Result<EntityTagValue> result)
    {
        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        error.Rules.Items.Should().ContainSingle().Which.ReasonCode.Should().Be(ValidationCodes.EtagMalformed);
    }

    #region Opaque tag character boundaries

    [Theory]
    [MemberData(nameof(AcceptedCodePoints))]
    public void Strong_and_Weak_accept_the_boundary_characters_of_etagc(int codePoint)
    {
        var strong = EntityTagValue.Strong(Tag(codePoint));
        var weak = EntityTagValue.Weak(Tag(codePoint));

        strong.OpaqueTag.Should().Be(Tag(codePoint));
        strong.IsWeak.Should().BeFalse();
        weak.OpaqueTag.Should().Be(Tag(codePoint));
        weak.IsWeak.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(RejectedCodePoints))]
    public void Strong_and_Weak_reject_the_characters_just_outside_etagc(int codePoint)
    {
        var strong = () => EntityTagValue.Strong(Tag(codePoint));
        var weak = () => EntityTagValue.Weak(Tag(codePoint));

        strong.ShouldRejectArgument("opaqueTag");
        weak.ShouldRejectArgument("opaqueTag");
    }

    [Theory]
    [MemberData(nameof(AcceptedCodePoints))]
    public void TryParse_accepts_the_boundary_characters_in_both_forms_and_round_trips_them(int codePoint)
    {
        var strongHeader = $"\"{Tag(codePoint)}\"";
        var weakHeader = $"W/\"{Tag(codePoint)}\"";

        var strong = EntityTagValue.TryParse(strongHeader).Should().BeSuccess().Which;
        var weak = EntityTagValue.TryParse(weakHeader).Should().BeSuccess().Which;

        strong.Should().Be(EntityTagValue.Strong(Tag(codePoint)));
        strong.ToHeaderValue().Should().Be(strongHeader);
        weak.Should().Be(EntityTagValue.Weak(Tag(codePoint)));
        weak.ToHeaderValue().Should().Be(weakHeader);
    }

    [Theory]
    [MemberData(nameof(RejectedCodePoints))]
    public void TryParse_rejects_the_characters_just_outside_etagc_in_both_forms(int codePoint)
    {
        ShouldBeMalformed(EntityTagValue.TryParse($"\"{Tag(codePoint)}\""));
        ShouldBeMalformed(EntityTagValue.TryParse($"W/\"{Tag(codePoint)}\""));
    }

    #endregion

    #region Header shape

    [Fact]
    public void TryParse_rejects_a_weak_prefix_that_has_no_closing_quote_of_its_own() =>
        ShouldBeMalformed(EntityTagValue.TryParse("W/\""));

    [Fact]
    public void TryParse_of_a_quoted_star_is_the_literal_tag_not_the_wildcard()
    {
        var tag = EntityTagValue.TryParse("\"*\"").Should().BeSuccess().Which;

        tag.IsWildcard.Should().BeFalse();
        tag.Should().Be(EntityTagValue.Strong("*"));
    }

    #endregion

    #region The wildcard never equals a tag

    [Fact]
    public void The_wildcard_is_never_equal_to_a_tag_by_strong_or_weak_comparison_in_either_direction()
    {
        var wildcard = EntityTagValue.Wildcard();
        EntityTagValue[] others = [wildcard, EntityTagValue.Strong("*"), EntityTagValue.Weak("*")];

        foreach (var other in others)
        {
            wildcard.StrongEquals(other).Should().BeFalse($"the wildcard must not strongly equal {other.ToHeaderValue()}");
            wildcard.WeakEquals(other).Should().BeFalse($"the wildcard must not weakly equal {other.ToHeaderValue()}");
            other.StrongEquals(wildcard).Should().BeFalse($"{other.ToHeaderValue()} must not strongly equal the wildcard");
            other.WeakEquals(wildcard).Should().BeFalse($"{other.ToHeaderValue()} must not weakly equal the wildcard");
        }
    }

    [Fact]
    public void A_literal_star_tag_compares_like_any_other_tag()
    {
        EntityTagValue.Strong("*").StrongEquals(EntityTagValue.Strong("*")).Should().BeTrue();
        EntityTagValue.Strong("*").WeakEquals(EntityTagValue.Weak("*")).Should().BeTrue();
        EntityTagValue.Weak("*").StrongEquals(EntityTagValue.Weak("*")).Should().BeFalse();
    }

    #endregion
}
