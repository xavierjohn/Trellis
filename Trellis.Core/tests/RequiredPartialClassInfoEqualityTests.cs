namespace Trellis.Core.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Trellis.PrimitiveValueObjectGenerator;
using Xunit;

/// <summary>
/// Unit tests for <see cref="RequiredPartialClassInfo"/>'s <see cref="object.Equals(object?)"/> and
/// <see cref="object.GetHashCode"/> overrides. The class is the cache key into the incremental
/// source-generator pipeline; if its equality fails to incorporate a new property, the generator
/// will silently emit stale code when only that property changes. The <c>HasNotDefault</c> and
/// <c>HasTrim</c> flags added by the POLA realignment must participate in both <c>Equals</c>
/// and <c>GetHashCode</c>. The remaining flags and reason codes are covered one change at a time by
/// <see cref="Changing_one_emission_affecting_field_changes_equality_and_hash"/>, the other fields by the
/// <c>Differing_*</c> tests, and the two drift guards fail when a constructor parameter or field is added
/// without a test.
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
        bool hasPositive = false,
        bool hasNonNegative = false,
        bool hasNegative = false,
        bool hasNonPositive = false,
        bool hasExplicitRange = false,
        bool hasUserJsonConverter = false,
        string? lengthCode = null,
        string? rangeCode = null,
        string? notDefaultCode = null,
        bool validateAdditionalHasCode = false,
        bool declaredBothValidateAdditional = false,
        bool declaredThreeArgValidateAdditionalOnly = false,
        GeneratedMemberDeclaration[]? userDeclaredMembers = null) =>
        new(@namespace, className, classBase, accessibility, maxLength, minLength,
            rangeMin, rangeMax, rangeLongMin, rangeLongMax, rangeDoubleMin, rangeDoubleMax,
            nestingParents, typePath, hasNotDefault, hasTrim, hasPositive, hasNonNegative, hasNegative,
            hasNonPositive, hasExplicitRange, hasUserJsonConverter, lengthCode, rangeCode, notDefaultCode,
            validateAdditionalHasCode, declaredBothValidateAdditional, declaredThreeArgValidateAdditionalOnly,
            userDeclaredMembers);

    private static RequiredPartialClassInfo MakeWithOnly(string field) => field switch
    {
        nameof(RequiredPartialClassInfo.HasPositive) => Make(hasPositive: true),
        nameof(RequiredPartialClassInfo.HasNonNegative) => Make(hasNonNegative: true),
        nameof(RequiredPartialClassInfo.HasNegative) => Make(hasNegative: true),
        nameof(RequiredPartialClassInfo.HasNonPositive) => Make(hasNonPositive: true),
        nameof(RequiredPartialClassInfo.HasExplicitRange) => Make(hasExplicitRange: true),
        nameof(RequiredPartialClassInfo.HasUserJsonConverter) => Make(hasUserJsonConverter: true),
        nameof(RequiredPartialClassInfo.LengthCode) => Make(lengthCode: "custom.length"),
        nameof(RequiredPartialClassInfo.RangeCode) => Make(rangeCode: "custom.range"),
        nameof(RequiredPartialClassInfo.NotDefaultCode) => Make(notDefaultCode: "custom.not_default"),
        nameof(RequiredPartialClassInfo.ValidateAdditionalHasCode) => Make(validateAdditionalHasCode: true),
        nameof(RequiredPartialClassInfo.DeclaredBothValidateAdditional) => Make(declaredBothValidateAdditional: true),
        nameof(RequiredPartialClassInfo.DeclaredThreeArgValidateAdditionalOnly) => Make(declaredThreeArgValidateAdditionalOnly: true),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown field."),
    };

    private static RequiredPartialClassInfo MakeFullyPopulated() =>
        Make(
            classBase: "RequiredString",
            maxLength: 50,
            minLength: 1,
            rangeMin: 2,
            rangeMax: 99,
            rangeLongMin: 3L,
            rangeLongMax: 999L,
            rangeDoubleMin: 0.5,
            rangeDoubleMax: 99.5,
            nestingParents: ["public partial class Outer"],
            typePath: "MyApp.Outer.OrderId",
            hasNotDefault: true,
            hasTrim: true,
            hasPositive: true,
            hasNonNegative: true,
            hasNegative: true,
            hasNonPositive: true,
            hasExplicitRange: true,
            hasUserJsonConverter: true,
            lengthCode: "custom.length",
            rangeCode: "custom.range",
            notDefaultCode: "custom.not_default",
            validateAdditionalHasCode: true,
            declaredBothValidateAdditional: true,
            declaredThreeArgValidateAdditionalOnly: true,
            userDeclaredMembers:
            [
                new GeneratedMemberDeclaration("TryParse", "TryParse(string, IFormatProvider, out OrderId)", matchesByNameOnly: false, InSource("a.cs", "class C { }", 0, 5)),
            ]);

    private static Location InSource(string path, string text, int start, int length) =>
        Location.Create(CSharpSyntaxTree.ParseText(text, path: path), new TextSpan(start, length));

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

    [Theory]
    [InlineData(nameof(RequiredPartialClassInfo.HasPositive))]
    [InlineData(nameof(RequiredPartialClassInfo.HasNonNegative))]
    [InlineData(nameof(RequiredPartialClassInfo.HasNegative))]
    [InlineData(nameof(RequiredPartialClassInfo.HasNonPositive))]
    [InlineData(nameof(RequiredPartialClassInfo.HasExplicitRange))]
    [InlineData(nameof(RequiredPartialClassInfo.HasUserJsonConverter))]
    [InlineData(nameof(RequiredPartialClassInfo.LengthCode))]
    [InlineData(nameof(RequiredPartialClassInfo.RangeCode))]
    [InlineData(nameof(RequiredPartialClassInfo.NotDefaultCode))]
    [InlineData(nameof(RequiredPartialClassInfo.ValidateAdditionalHasCode))]
    [InlineData(nameof(RequiredPartialClassInfo.DeclaredBothValidateAdditional))]
    [InlineData(nameof(RequiredPartialClassInfo.DeclaredThreeArgValidateAdditionalOnly))]
    public void Changing_one_emission_affecting_field_changes_equality_and_hash(string field)
    {
        var baseline = Make();
        var changed = MakeWithOnly(field);

        baseline.Equals(changed).Should().BeFalse($"{field} affects emitted code");
        changed.Equals(baseline).Should().BeFalse($"{field} must be compared symmetrically");
        baseline.GetHashCode().Should().NotBe(changed.GetHashCode(), $"{field} must participate in the hash");
    }

    [Fact]
    public void Fully_populated_infos_built_independently_are_equal_and_hash_equally()
    {
        var a = MakeFullyPopulated();
        var b = MakeFullyPopulated();

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Differing_UserDeclaredMember_signature_makes_infos_unequal()
    {
        var a = Make(userDeclaredMembers: [new GeneratedMemberDeclaration("TryParse", "TryParse(string)", matchesByNameOnly: false, location: null)]);
        var b = Make(userDeclaredMembers: [new GeneratedMemberDeclaration("TryParse", "TryParse(ReadOnlySpan<char>)", matchesByNameOnly: false, location: null)]);

        a.Equals(b).Should().BeFalse();
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void GeneratedMemberDeclaration_with_same_values_and_location_is_equal_and_hashes_equally()
    {
        var a = new GeneratedMemberDeclaration("Parse", "Parse(string)", matchesByNameOnly: false, InSource("a.cs", "class C { }", 0, 5));
        var b = new GeneratedMemberDeclaration("Parse", "Parse(string)", matchesByNameOnly: false, InSource("a.cs", "class C { }", 0, 5));

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Theory]
    [InlineData(nameof(GeneratedMemberDeclaration.Name))]
    [InlineData(nameof(GeneratedMemberDeclaration.Signature))]
    [InlineData(nameof(GeneratedMemberDeclaration.MatchesByNameOnly))]
    [InlineData("FilePath")]
    [InlineData("SpanStart")]
    [InlineData("SpanLength")]
    [InlineData("LocationAbsent")]
    public void GeneratedMemberDeclaration_changing_one_field_changes_equality_and_hash(string field)
    {
        var baseline = new GeneratedMemberDeclaration("Parse", "Parse(string)", matchesByNameOnly: false, InSource("a.cs", "class C { }", 0, 5));
        var changed = field switch
        {
            nameof(GeneratedMemberDeclaration.Name) => new GeneratedMemberDeclaration("TryParse", "Parse(string)", false, InSource("a.cs", "class C { }", 0, 5)),
            nameof(GeneratedMemberDeclaration.Signature) => new GeneratedMemberDeclaration("Parse", "Parse(int)", false, InSource("a.cs", "class C { }", 0, 5)),
            nameof(GeneratedMemberDeclaration.MatchesByNameOnly) => new GeneratedMemberDeclaration("Parse", "Parse(string)", true, InSource("a.cs", "class C { }", 0, 5)),
            "FilePath" => new GeneratedMemberDeclaration("Parse", "Parse(string)", false, InSource("b.cs", "class C { }", 0, 5)),
            "SpanStart" => new GeneratedMemberDeclaration("Parse", "Parse(string)", false, InSource("a.cs", "class C { }", 1, 5)),
            "SpanLength" => new GeneratedMemberDeclaration("Parse", "Parse(string)", false, InSource("a.cs", "class C { }", 0, 6)),
            "LocationAbsent" => new GeneratedMemberDeclaration("Parse", "Parse(string)", false, null),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown field."),
        };

        baseline.Equals(changed).Should().BeFalse($"{field} identifies the declaration");
        baseline.GetHashCode().Should().NotBe(changed.GetHashCode(), $"{field} must participate in the hash");
    }

    [Fact]
    public void Every_RequiredPartialClassInfo_constructor_parameter_has_an_equality_test()
    {
        // Tripwire: a new parameter must get an Equals/GetHashCode test (and a Make/MakeWithOnly entry), then be listed here.
        string[] covered =
        [
            "nameSpace", "className", "classBase", "accessibility", "maxLength", "minLength", "rangeMin", "rangeMax",
            "rangeLongMin", "rangeLongMax", "rangeDoubleMin", "rangeDoubleMax", "nestingParents", "typePath",
            "hasNotDefault", "hasTrim", "hasPositive", "hasNonNegative", "hasNegative", "hasNonPositive",
            "hasExplicitRange", "hasUserJsonConverter", "lengthCode", "rangeCode", "notDefaultCode",
            "validateAdditionalHasCode", "declaredBothValidateAdditional", "declaredThreeArgValidateAdditionalOnly",
            "userDeclaredMembers",
        ];

        var parameters = typeof(RequiredPartialClassInfo).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name!);

        parameters.Should().BeEquivalentTo(covered,
            "every constructor parameter feeds the incremental generator cache key and needs an equality test");
    }

    [Fact]
    public void Every_GeneratedMemberDeclaration_field_is_compared_or_deliberately_excluded()
    {
        // Location is excluded on purpose: equality uses the path and span values derived from it.
        string[] covered = ["Name", "Signature", "MatchesByNameOnly", "Location", "_filePath", "_spanStart", "_spanLength"];

        var fields = typeof(GeneratedMemberDeclaration)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.Name);

        fields.Should().BeEquivalentTo(covered,
            "a new field must be compared in Equals/GetHashCode and covered by the one-field-at-a-time test");
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