namespace Trellis.Asp.Tests;

using System.Text.Json;
using FluentAssertions;
using Trellis;
using Trellis.Asp.ModelBinding;
using Trellis.Primitives;
using Xunit;

/// <summary>
/// Invariant 10 — producer independence.
/// </summary>
/// <remarks>
/// <para>
/// The vocabulary is only worth freezing if the same failure reports the same code no matter which
/// part of the framework noticed it. A client that must write <c>code == "format.integer" ||
/// validatorName == "int" || ...</c> has gained nothing over parsing the English message, which is
/// the situation this work exists to end.
/// </para>
/// <para>
/// These tests are deliberately cross-package: query-string binding lives in
/// <see cref="PrimitiveConverter"/> (Trellis.Asp), the hand-written primitives live in
/// Trellis.Primitives, and <c>TryCreate</c> on a <c>RequiredInt</c> is emitted by the source
/// generator in Trellis.Core. Three independent code paths, asserted to agree. A single-package
/// test would pass while the invariant was broken.
/// </para>
/// </remarks>
public class ProducerIndependenceTests
{
    private static string CodeOf(Error error) =>
        ((Error.InvalidInput)error).Fields.Items[0].ReasonCode;

    [Fact]
    public void Malformed_integer_reports_format_integer_from_every_producer()
    {
        var fromQueryBinding = PrimitiveConverter.ConvertToPrimitive<int>("abc");
        var fromGeneratedTryCreate = ProducerIndependenceTicket.TryCreate("abc");
        var fromHandWrittenPrimitive = Age.TryCreate("abc");

        fromQueryBinding.IsFailure.Should().BeTrue();
        fromGeneratedTryCreate.IsFailure.Should().BeTrue();
        fromHandWrittenPrimitive.IsFailure.Should().BeTrue();

        new[]
        {
            CodeOf(fromQueryBinding.Error!),
            CodeOf(fromGeneratedTryCreate.Error!),
            CodeOf(fromHandWrittenPrimitive.Error!),
        }.Should().AllBe(ValidationCodes.FormatInteger);
    }

    [Fact]
    public void Out_of_range_for_type_is_also_format_integer_not_a_number_code()
    {
        // int.TryParse cannot distinguish "abc" from a value too large to fit, so the producer has
        // no basis for a finer code and must not invent one. Pinning this stops a future change
        // from splitting the two apart silently, which would break a client keyed on the code.
        var tooLarge = PrimitiveConverter.ConvertToPrimitive<int>("99999999999");

        tooLarge.IsFailure.Should().BeTrue();
        CodeOf(tooLarge.Error!).Should().Be(ValidationCodes.FormatInteger);
    }

    [Fact]
    public void Absent_value_reports_value_not_null_from_every_producer()
    {
        var fromQueryBinding = PrimitiveConverter.ConvertToPrimitive<int>(null);
        var fromGeneratedTryCreate = ProducerIndependenceTicket.TryCreate((string?)null);
        var fromHandWrittenPrimitive = Age.TryCreate((string?)null);

        new[]
        {
            CodeOf(fromQueryBinding.Error!),
            CodeOf(fromGeneratedTryCreate.Error!),
            CodeOf(fromHandWrittenPrimitive.Error!),
        }.Should().AllBe(ValidationCodes.ValueNotNull);
    }

    [Fact]
    public void Blank_value_is_value_not_empty_and_stays_distinct_from_absent()
    {
        var blank = Age.TryCreate("   ");
        var absent = Age.TryCreate((string?)null);

        CodeOf(blank.Error!).Should().Be(ValidationCodes.ValueNotEmpty);
        CodeOf(absent.Error!).Should().Be(ValidationCodes.ValueNotNull);
    }

    [Fact]
    public void Blank_value_reports_value_not_empty_from_every_producer()
    {
        // The tempting shortcut is to let a blank string fall through to the type parser, which
        // fails and reports `format.integer`. That names the wrong cause: whitespace can never
        // parse into any scalar, so "malformed integer" is true of every type and useful for none.
        var fromQueryBinding = PrimitiveConverter.ConvertToPrimitive<int>("   ");
        var fromGeneratedTryCreate = ProducerIndependenceTicket.TryCreate("   ");
        var fromHandWrittenPrimitive = Age.TryCreate("   ");

        new[]
        {
            CodeOf(fromQueryBinding.Error!),
            CodeOf(fromGeneratedTryCreate.Error!),
            CodeOf(fromHandWrittenPrimitive.Error!),
        }.Should().AllBe(ValidationCodes.ValueNotEmpty);
    }

    [Fact]
    public void Empty_string_into_a_non_string_target_is_not_reported_as_absent()
    {
        // An empty query value arrived; it is present and blank, not missing. Reporting
        // `value.not-null` would tell the caller to supply a parameter they already supplied.
        var fromQueryBinding = PrimitiveConverter.ConvertToPrimitive<Guid>("");
        var fromGeneratedTryCreate = ProducerIndependenceOrderId.TryCreate("");

        CodeOf(fromQueryBinding.Error!).Should().Be(ValidationCodes.ValueNotEmpty);
        CodeOf(fromGeneratedTryCreate.Error!).Should().Be(ValidationCodes.ValueNotEmpty);
    }

    [Fact]
    public void Malformed_guid_reports_format_guid_from_every_producer()
    {
        var fromQueryBinding = PrimitiveConverter.ConvertToPrimitive<Guid>("not-a-guid");
        var fromGeneratedTryCreate = ProducerIndependenceOrderId.TryCreate("not-a-guid");

        CodeOf(fromQueryBinding.Error!).Should().Be(ValidationCodes.FormatGuid);
        CodeOf(fromGeneratedTryCreate.Error!).Should().Be(ValidationCodes.FormatGuid);
    }

    [Fact]
    public void Unknown_enum_name_is_distinct_from_a_defined_value_that_is_out_of_range()
    {
        var unknownName = PrimitiveConverter.ConvertToPrimitive<ProducerIndependenceColor>("mauve");
        var undefinedNumeric = PrimitiveConverter.ConvertToPrimitive<ProducerIndependenceColor>("99");

        CodeOf(unknownName.Error!).Should().Be(ValidationCodes.EnumNameUndefined);
        CodeOf(undefinedNumeric.Error!).Should().Be(ValidationCodes.EnumUndefined);
    }

    [Fact]
    public void Byte_out_of_range_carries_its_bounds_as_args()
    {
        var result = PrimitiveConverter.ConvertToPrimitive<byte>("999");

        var violation = ((Error.InvalidInput)result.Error!).Fields.Items[0];
        violation.ReasonCode.Should().Be(ValidationCodes.FormatInteger);
        violation.Args.Should().NotBeNull();
        violation.Args!["min"].Should().Be("0");
        violation.Args["max"].Should().Be("255");
    }

    [Fact]
    public void Composite_value_object_null_property_is_a_nullability_failure_not_a_format_one()
    {
        // A property present as JSON null and a property holding the wrong token type both surface
        // as InvalidOperationException from the reader, so the two are easy to conflate. They mean
        // different things to a client, and carry different codes.
        var nullProperty = Record.Exception(() =>
            JsonSerializer.Deserialize<Money>("""{"amount":null,"currency":"USD"}"""));
        var wrongToken = Record.Exception(() =>
            JsonSerializer.Deserialize<Money>("""{"amount":"lots","currency":"USD"}"""));

        CodeOf(((TrellisJsonValidationException)nullProperty!).InvalidInput!)
            .Should().Be(ValidationCodes.ValueNotNull);
        CodeOf(((TrellisJsonValidationException)wrongToken!).InvalidInput!)
            .Should().Be(ValidationCodes.FormatConversion);
    }
}

public partial class ProducerIndependenceTicket : RequiredInt<ProducerIndependenceTicket>;

public partial class ProducerIndependenceOrderId : RequiredGuid<ProducerIndependenceOrderId>;

public enum ProducerIndependenceColor
{
    Red = 1,
    Green = 2,
}
