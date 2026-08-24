namespace Trellis.Core.Tests.Primitives;

using System.Text.Json;
using FluentAssertions;
using Trellis;

/// <summary>
/// An <c>enum.name-undefined</c> violation names the members it would have accepted, as
/// machine-readable args rather than as English prose.
/// </summary>
/// <remarks>
/// <para>
/// The members were never dropped — <c>RequiredEnum.TryCreate</c> has always joined them into the
/// detail (<c>"'X' is not a valid Y. Valid values: A, B, C"</c>). The defect is that prose is the
/// only place they lived: a client that wants to render "choose one of…" in the caller's language
/// had to parse an English sentence, which is precisely the situation `Args` exists to end.
/// </para>
/// <para>
/// The detail keeps its list. Args is additive here, not a replacement, so a human reading the
/// response still gets a complete sentence.
/// </para>
/// </remarks>
public sealed class RequiredEnumAllowedArgsTests
{
    private static ValidationArgValue? AllowedOf(Error error) =>
        ((Error.InvalidInput)error).Fields.Items[0].Args?["allowed"];

    [Fact]
    public void An_undefined_name_carries_the_permitted_members()
    {
        var result = AllowedArgsShade.TryCreate("mauve");

        result.IsFailure.Should().BeTrue();
        AllowedOf(result.Error!).Should().Be(ValidationArgValue.ListOf("Amber", "Zinc"));
    }

    /// <remarks>
    /// The fixture declares <c>Zinc</c> before <c>Amber</c> deliberately. Registry order is an
    /// implementation detail of where the members happen to sit in the file; a wire contract a
    /// client diffs across producers cannot inherit it.
    /// </remarks>
    [Fact]
    public void The_permitted_members_are_ordinally_sorted_not_declaration_ordered()
    {
        var result = AllowedArgsShade.TryCreate("mauve");

        AllowedOf(result.Error!).Should().Be(ValidationArgValue.ListOf("Amber", "Zinc"));
    }

    /// <remarks>
    /// A blank value reports <c>value.not-empty</c>, not <c>enum.name-undefined</c> — the caller
    /// named no member at all rather than an unrecognized one. Attaching the permitted set there
    /// would make `allowed` mean "some enum was involved" instead of "these are your options",
    /// and a client keying on its presence would be misled.
    /// </remarks>
    [Fact]
    public void A_blank_value_is_not_an_undefined_name_and_carries_no_permitted_members()
    {
        var result = AllowedArgsShade.TryCreate("   ");

        var violation = ((Error.InvalidInput)result.Error!).Fields.Items[0];
        violation.ReasonCode.Should().Be(ValidationCodes.ValueNotEmpty);
        violation.Args.Should().BeNull();
    }

    [Fact]
    public void The_detail_still_spells_the_members_out_for_a_human()
    {
        var result = AllowedArgsShade.TryCreate("mauve");

        ((Error.InvalidInput)result.Error!).Fields.Items[0].Detail
            .Should().Contain("Valid values: Amber, Zinc");
    }

    [Fact]
    public void The_permitted_members_reach_the_wire_as_a_json_array()
    {
        var result = AllowedArgsShade.TryCreate("mauve");

        var json = JsonSerializer.SerializeToElement(
            ((Error.InvalidInput)result.Error!).Fields.Items[0].Args);

        json.GetProperty("allowed").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("allowed").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(["Amber", "Zinc"]);
    }

    /// <remarks>
    /// The JSON converter is a fourth producer, and it curates its own message rather than
    /// propagating the producer's detail (the raw name is attacker-supplied and is sanitized
    /// here). That curation is exactly where a permitted set could have been dropped, so it is
    /// asserted rather than assumed.
    /// </remarks>
    [Fact]
    public void The_json_converter_carries_the_permitted_members_too()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new RequiredEnumJsonConverter<AllowedArgsShade>() },
        };

        var act = () => JsonSerializer.Deserialize<AllowedArgsShade>("\"mauve\"", options);

        var thrown = act.Should().Throw<TrellisJsonValidationException>().Which;
        var violation = thrown.InvalidInput!.Fields.Items[0];
        violation.ReasonCode.Should().Be(ValidationCodes.EnumNameUndefined);
        violation.Args!["allowed"].Should().Be(ValidationArgValue.ListOf("Amber", "Zinc"));
    }

    /// <remarks>
    /// The converter deliberately reports the <em>producer's</em> code, so a blank value arrives
    /// as <c>value.not-empty</c> rather than <c>enum.name-undefined</c>. The permitted set must
    /// follow the code, not the converter: attaching it here would make <c>allowed</c> mean "an
    /// enum was involved" instead of "these are your options".
    /// </remarks>
    [Fact]
    public void The_json_converter_omits_the_permitted_members_when_the_value_was_merely_blank()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new RequiredEnumJsonConverter<AllowedArgsShade>() },
        };

        var act = () => JsonSerializer.Deserialize<AllowedArgsShade>("\"   \"", options);

        var violation = act.Should().Throw<TrellisJsonValidationException>()
            .Which.InvalidInput!.Fields.Items[0];
        violation.ReasonCode.Should().Be(ValidationCodes.ValueNotEmpty);
        violation.Args.Should().BeNull();
    }
}

public sealed class AllowedArgsShade :
    RequiredEnum<AllowedArgsShade>,
    IScalarValue<AllowedArgsShade, string>
{
    public static readonly AllowedArgsShade Zinc = new();
    public static readonly AllowedArgsShade Amber = new();

    /// <remarks>
    /// <para>
    /// 248 country names cost roughly 3 KB of args and another 2.8 KB of prose on every rejection,
    /// and a request with several invalid enum fields multiplies both — a small request provoking a
    /// large response is an amplification vector, not merely waste.
    /// </para>
    /// <para>
    /// Both halves are dropped whole rather than shortened. A truncated list reads as exhaustive,
    /// so it would tell a client that a member it omitted is not permitted.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_enum_wider_than_the_cap_reports_a_count_instead_of_a_list()
    {
        var result = WideShade.TryCreate("mauve");

        var violation = ((Error.InvalidInput)result.Error!).Fields.Items[0];
        violation.ReasonCode.Should().Be(ValidationCodes.EnumNameUndefined);
        violation.Args!.Should().NotContainKey("allowed");
        violation.Args["allowedCount"].Should().Be(new ValidationArgValue.Number(65));
    }

    [Fact]
    public void An_enum_wider_than_the_cap_keeps_its_detail_short_too()
    {
        var result = WideShade.TryCreate("mauve");

        var detail = ((Error.InvalidInput)result.Error!).Fields.Items[0].Detail;
        detail.Should().Be("'mauve' is not a valid WideShade.");
        detail.Should().NotContain("Valid values",
            "capping the args while the prose still spells out every member would leave the larger half of the payload in place");
    }

    [Fact]
    public void The_json_converter_applies_the_same_cap_to_both_halves()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new RequiredEnumJsonConverter<WideShade>() },
        };

        var act = () => JsonSerializer.Deserialize<WideShade>("\"mauve\"", options);

        var thrown = act.Should().Throw<TrellisJsonValidationException>().Which;
        var violation = thrown.InvalidInput!.Fields.Items[0];
        violation.Args!.Should().NotContainKey("allowed");
        violation.Args["allowedCount"].Should().Be(new ValidationArgValue.Number(65));
        thrown.Message.Should().NotContain("Valid values");
    }
}

/// <summary>65 members — one past <see cref="ValidationArgs.MaxAllowedMembers"/>.</summary>
public sealed class WideShade : RequiredEnum<WideShade>, IScalarValue<WideShade, string>
{
    public static readonly WideShade M01 = new();
    public static readonly WideShade M02 = new();
    public static readonly WideShade M03 = new();
    public static readonly WideShade M04 = new();
    public static readonly WideShade M05 = new();
    public static readonly WideShade M06 = new();
    public static readonly WideShade M07 = new();
    public static readonly WideShade M08 = new();
    public static readonly WideShade M09 = new();
    public static readonly WideShade M10 = new();
    public static readonly WideShade M11 = new();
    public static readonly WideShade M12 = new();
    public static readonly WideShade M13 = new();
    public static readonly WideShade M14 = new();
    public static readonly WideShade M15 = new();
    public static readonly WideShade M16 = new();
    public static readonly WideShade M17 = new();
    public static readonly WideShade M18 = new();
    public static readonly WideShade M19 = new();
    public static readonly WideShade M20 = new();
    public static readonly WideShade M21 = new();
    public static readonly WideShade M22 = new();
    public static readonly WideShade M23 = new();
    public static readonly WideShade M24 = new();
    public static readonly WideShade M25 = new();
    public static readonly WideShade M26 = new();
    public static readonly WideShade M27 = new();
    public static readonly WideShade M28 = new();
    public static readonly WideShade M29 = new();
    public static readonly WideShade M30 = new();
    public static readonly WideShade M31 = new();
    public static readonly WideShade M32 = new();
    public static readonly WideShade M33 = new();
    public static readonly WideShade M34 = new();
    public static readonly WideShade M35 = new();
    public static readonly WideShade M36 = new();
    public static readonly WideShade M37 = new();
    public static readonly WideShade M38 = new();
    public static readonly WideShade M39 = new();
    public static readonly WideShade M40 = new();
    public static readonly WideShade M41 = new();
    public static readonly WideShade M42 = new();
    public static readonly WideShade M43 = new();
    public static readonly WideShade M44 = new();
    public static readonly WideShade M45 = new();
    public static readonly WideShade M46 = new();
    public static readonly WideShade M47 = new();
    public static readonly WideShade M48 = new();
    public static readonly WideShade M49 = new();
    public static readonly WideShade M50 = new();
    public static readonly WideShade M51 = new();
    public static readonly WideShade M52 = new();
    public static readonly WideShade M53 = new();
    public static readonly WideShade M54 = new();
    public static readonly WideShade M55 = new();
    public static readonly WideShade M56 = new();
    public static readonly WideShade M57 = new();
    public static readonly WideShade M58 = new();
    public static readonly WideShade M59 = new();
    public static readonly WideShade M60 = new();
    public static readonly WideShade M61 = new();
    public static readonly WideShade M62 = new();
    public static readonly WideShade M63 = new();
    public static readonly WideShade M64 = new();
    public static readonly WideShade M65 = new();
}