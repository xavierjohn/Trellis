namespace Trellis.Core.Tests;

using Trellis.PrimitiveValueObjectGenerator;
using Xunit;

/// <summary>
/// Unit tests for <see cref="RequiredPartialClassInfo"/>'s <see cref="object.Equals(object?)"/> and
/// <see cref="object.GetHashCode"/> overrides. The class is the cache key into the incremental
/// source-generator pipeline; if its equality fails to incorporate a new property, the generator
/// will silently emit stale code when only that property changes. The <c>HasNotDefault</c> and
/// <c>HasTrim</c> flags added by the POLA realignment must participate in both <c>Equals</c>
/// and <c>GetHashCode</c>.
/// </summary>
public class RequiredPartialClassInfoEqualityTests
{
    private static RequiredPartialClassInfo Make(
        string @namespace = "MyApp",
        string className = "OrderId",
        string classBase = "RequiredGuid",
        string accessibility = "public",
        int? maxLength = null,
        int? minLength = null,
        int? rangeMin = null,
        int? rangeMax = null,
        long? rangeLongMin = null,
        long? rangeLongMax = null,
        double? rangeDoubleMin = null,
        double? rangeDoubleMax = null,
        string[]? nestingParents = null,
        string? typePath = null,
        bool hasNotDefault = false,
        bool hasTrim = false,
        GeneratedMemberDeclaration[]? userDeclaredMembers = null) =>
        new(@namespace, className, classBase, accessibility, maxLength, minLength,
            rangeMin, rangeMax, rangeLongMin, rangeLongMax, rangeDoubleMin, rangeDoubleMax,
            nestingParents, typePath, hasNotDefault, hasTrim, userDeclaredMembers: userDeclaredMembers);

    [Fact]
    public void Identical_infos_are_equal_and_have_same_hash()
    {
        var a = Make();
        var b = Make();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Differing_HasNotDefault_makes_infos_unequal()
    {
        var lenient = Make(hasNotDefault: false);
        var strict = Make(hasNotDefault: true);
        lenient.Equals(strict).Should().BeFalse();
        lenient.GetHashCode().Should().NotBe(strict.GetHashCode());
    }

    [Fact]
    public void Differing_HasTrim_makes_infos_unequal()
    {
        var noTrim = Make(classBase: "RequiredString", hasTrim: false);
        var withTrim = Make(classBase: "RequiredString", hasTrim: true);
        noTrim.Equals(withTrim).Should().BeFalse();
        noTrim.GetHashCode().Should().NotBe(withTrim.GetHashCode());
    }

    [Fact]
    public void Adding_NotDefault_to_an_existing_class_changes_equality()
    {
        // The realistic incremental-generator scenario: the same partial class gains a
        // [NotDefault] attribute. The pipeline must see this as a distinct info value or
        // it will keep the stale generated output.
        var before = Make(classBase: "RequiredGuid", hasNotDefault: false, hasTrim: false);
        var after = Make(classBase: "RequiredGuid", hasNotDefault: true, hasTrim: false);
        before.Equals(after).Should().BeFalse();
        before.GetHashCode().Should().NotBe(after.GetHashCode());
    }

    [Fact]
    public void Adding_Trim_to_an_existing_class_changes_equality()
    {
        var before = Make(classBase: "RequiredString", hasNotDefault: true, hasTrim: false);
        var after = Make(classBase: "RequiredString", hasNotDefault: true, hasTrim: true);
        before.Equals(after).Should().BeFalse();
        before.GetHashCode().Should().NotBe(after.GetHashCode());
    }

    [Fact]
    public void Differing_ClassName_makes_infos_unequal() =>
        Make(className: "OrderId").Equals(Make(className: "CustomerId")).Should().BeFalse();

    [Fact]
    public void Differing_ClassBase_makes_infos_unequal() =>
        Make(classBase: "RequiredGuid").Equals(Make(classBase: "RequiredString")).Should().BeFalse();

    [Fact]
    public void Differing_Namespace_makes_infos_unequal() =>
        Make(@namespace: "MyApp").Equals(Make(@namespace: "OtherApp")).Should().BeFalse();

    [Fact]
    public void Differing_MaxLength_makes_infos_unequal() =>
        Make(classBase: "RequiredString", maxLength: 10).Equals(Make(classBase: "RequiredString", maxLength: 20)).Should().BeFalse();

    [Fact]
    public void Differing_Accessibility_makes_infos_unequal() =>
        Make(accessibility: "public").Equals(Make(accessibility: "internal")).Should().BeFalse();

    [Fact]
    public void Differing_MinLength_makes_infos_unequal()
    {
        var a = Make(classBase: "RequiredString", maxLength: 50, minLength: 1);
        var b = Make(classBase: "RequiredString", maxLength: 50, minLength: 5);
        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void Differing_RangeMin_makes_infos_unequal()
    {
        var a = Make(classBase: "RequiredInt", rangeMin: 1, rangeMax: 100);
        var b = Make(classBase: "RequiredInt", rangeMin: 2, rangeMax: 100);
        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void Differing_RangeMax_makes_infos_unequal()
    {
        var a = Make(classBase: "RequiredInt", rangeMin: 1, rangeMax: 100);
        var b = Make(classBase: "RequiredInt", rangeMin: 1, rangeMax: 200);
        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void Differing_RangeLongMin_makes_infos_unequal()
    {
        var a = Make(classBase: "RequiredLong", rangeLongMin: 1L, rangeLongMax: 100L);
        var b = Make(classBase: "RequiredLong", rangeLongMin: 2L, rangeLongMax: 100L);
        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void Differing_RangeLongMax_makes_infos_unequal()
    {
        var a = Make(classBase: "RequiredLong", rangeLongMin: 1L, rangeLongMax: 100L);
        var b = Make(classBase: "RequiredLong", rangeLongMin: 1L, rangeLongMax: 200L);
        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void Differing_RangeDoubleMin_makes_infos_unequal()
    {
        var a = Make(classBase: "RequiredDecimal", rangeDoubleMin: 0.5, rangeDoubleMax: 99.5);
        var b = Make(classBase: "RequiredDecimal", rangeDoubleMin: 0.6, rangeDoubleMax: 99.5);
        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void Differing_RangeDoubleMax_makes_infos_unequal()
    {
        var a = Make(classBase: "RequiredDecimal", rangeDoubleMin: 0.5, rangeDoubleMax: 99.5);
        var b = Make(classBase: "RequiredDecimal", rangeDoubleMin: 0.5, rangeDoubleMax: 100.0);
        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void Differing_TypePath_makes_infos_unequal()
    {
        var a = Make(typePath: "MyApp.Inner.OrderId");
        var b = Make(typePath: "MyApp.Outer.OrderId");
        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void Differing_NestingParents_makes_infos_unequal()
    {
        // NestingParents is the only array-typed field; uses SequenceEqual in Equals.
        // Both length-differs and element-differs must be detected.
        var noParents = Make(nestingParents: []);
        var oneParent = Make(nestingParents: ["public partial class Outer"]);
        noParents.Equals(oneParent).Should().BeFalse();

        var elementA = Make(nestingParents: ["public partial class Outer"]);
        var elementB = Make(nestingParents: ["public partial class Container"]);
        elementA.Equals(elementB).Should().BeFalse();

        // Same content -> equal (content-based, not reference-based, equality)
        var ref1 = Make(nestingParents: ["public partial class Outer", "public partial class Mid"]);
        var ref2 = Make(nestingParents: ["public partial class Outer", "public partial class Mid"]);
        ref1.Equals(ref2).Should().BeTrue();
    }

    [Fact]
    public void Differing_UserDeclaredMembers_makes_infos_unequal()
    {
        var clean = Make(userDeclaredMembers: []);
        var withTryParse = Make(userDeclaredMembers:
            [new GeneratedMemberDeclaration("TryParse", "TryParse(string, IFormatProvider, out OrderId)", matchesByNameOnly: false, location: null)]);

        clean.Equals(withTryParse).Should().BeFalse();
        clean.GetHashCode().Should().NotBe(withTryParse.GetHashCode());
    }

    [Fact]
    public void Equals_with_null_is_false() => Make().Equals(null).Should().BeFalse();

    [Fact]
    public void Equals_with_reference_is_true()
    {
        var info = Make();
        info.Equals(info).Should().BeTrue();
    }

    [Fact]
    public void All_flags_combination_round_trips_through_equality()
    {
        // Spot-check the 2x2 matrix of HasNotDefault x HasTrim.
        var combos = new[]
        {
            (nd: false, t: false),
            (nd: true,  t: false),
            (nd: false, t: true),
            (nd: true,  t: true),
        };

        for (int i = 0; i < combos.Length; i++)
        {
            for (int j = 0; j < combos.Length; j++)
            {
                var a = Make(classBase: "RequiredString", hasNotDefault: combos[i].nd, hasTrim: combos[i].t);
                var b = Make(classBase: "RequiredString", hasNotDefault: combos[j].nd, hasTrim: combos[j].t);
                if (i == j)
                    a.Equals(b).Should().BeTrue();
                else
                    a.Equals(b).Should().BeFalse();
            }
        }
    }
}