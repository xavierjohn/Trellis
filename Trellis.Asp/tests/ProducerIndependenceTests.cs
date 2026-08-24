namespace Trellis.Asp.Tests;

using System.Text.Json;
using FluentAssertions;
using Trellis;
using Trellis.Asp.ModelBinding;
using Trellis.Asp.Validation;
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

    private static ValidationArgValue? AllowedOf(Error error) =>
        ((Error.InvalidInput)error).Fields.Items[0].Args?["allowed"];

    /// <remarks>
    /// Query binding's detail is the generic "The value is not a recognized option." — it never
    /// named the members at all, so before this the permitted set was unavailable to a client by
    /// any means, localized or not.
    /// </remarks>
    [Fact]
    public void Unknown_enum_name_carries_the_permitted_members_as_args()
    {
        var result = PrimitiveConverter.ConvertToPrimitive<ProducerIndependenceColor>("mauve");

        AllowedOf(result.Error!).Should().Be(ValidationArgValue.ListOf("Green", "Red"));
    }

    /// <remarks>
    /// The two enum codes are chosen by a ternary inside one branch, so covering only the
    /// name case would leave a client told which members exist when it sent <c>"mauve"</c> but
    /// not when it sent <c>99</c> — an asymmetry with no defensible explanation. The remedy set
    /// is the same either way: here are the members you may send.
    /// </remarks>
    [Fact]
    public void An_undefined_numeric_enum_value_carries_the_permitted_members_too()
    {
        var result = PrimitiveConverter.ConvertToPrimitive<ProducerIndependenceColor>("99");

        CodeOf(result.Error!).Should().Be(ValidationCodes.EnumUndefined);
        AllowedOf(result.Error!).Should().Be(ValidationArgValue.ListOf("Green", "Red"));
    }

    [Fact]
    public void Both_enum_failures_report_the_same_permitted_members()
    {
        var byName = PrimitiveConverter.ConvertToPrimitive<ProducerIndependenceColor>("mauve");
        var byNumber = PrimitiveConverter.ConvertToPrimitive<ProducerIndependenceColor>("99");

        AllowedOf(byName.Error!).Should().Be(AllowedOf(byNumber.Error!));
    }

    [Fact]
    public void Byte_out_of_range_carries_its_bounds_as_args()
    {
        var result = PrimitiveConverter.ConvertToPrimitive<byte>("999");

        var violation = ((Error.InvalidInput)result.Error!).Fields.Items[0];
        violation.ReasonCode.Should().Be(ValidationCodes.FormatInteger);
        violation.Args.Should().NotBeNull();
        violation.Args!["min"].Should().Be(new ValidationArgValue.Number(0));
        violation.Args["max"].Should().Be(new ValidationArgValue.Number(255));
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

    /// <summary>
    /// Reads a scalar value object from a JSON document and returns the code the body producer
    /// recorded, so a body failure can be compared against the query-binding failure for the same
    /// input.
    /// </summary>
    private static string CodeFromJsonBody<TValue, TPrimitive>(string json)
        where TValue : class, IScalarValue<TValue, TPrimitive>
        where TPrimitive : IComparable
    {
        using var _ = ValidationErrorsContext.BeginScope();
        var converter = new ValidatingJsonConverter<TValue, TPrimitive>();
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read();
        converter.Read(ref reader, typeof(TValue), new JsonSerializerOptions());

        return ValidationErrorsContext.GetUnprocessableContent()!.Fields.Items[0].ReasonCode;
    }

    [Fact]
    public void A_malformed_integer_reports_the_same_code_in_a_body_as_in_a_query_string() =>
        // The JSON body is the fifth producer, and the one most likely to be exercised. Before this
        // it reported `error.unspecified` while `?ticket=abc` reported `format.integer`, so a client
        // keying on the code had to know which half of the request the value came from.
        CodeFromJsonBody<ProducerIndependenceTicket, int>("\"abc\"")
            .Should().Be(CodeOf(PrimitiveConverter.ConvertToPrimitive<int>("abc").Error!));

    [Fact]
    public void A_null_body_value_reports_value_not_null_rather_than_the_sentinel() =>
        CodeFromJsonBody<ProducerIndependenceTicket, int>("null")
            .Should().Be(ValidationCodes.ValueNotNull);

    [Fact]
    public void A_malformed_guid_reports_the_same_code_in_a_body_as_in_a_query_string() =>
        CodeFromJsonBody<ProducerIndependenceOrderId, Guid>("\"not-a-guid\"")
            .Should().Be(CodeOf(PrimitiveConverter.ConvertToPrimitive<Guid>("not-a-guid").Error!));

    [Fact]
    public void A_blank_body_value_is_not_empty_rather_than_a_format_failure() =>
        // Blank never became the target scalar, but `format.integer` would name a shape the caller
        // never attempted. Query binding already said `value.not-empty`; the body now agrees.
        CodeFromJsonBody<ProducerIndependenceTicket, int>("\"   \"")
            .Should().Be(CodeOf(PrimitiveConverter.ConvertToPrimitive<int>("   ").Error!));

    [Fact]
    public void An_unsigned_integer_reports_format_integer_from_query_binding_too() =>
        // `uint` has no dedicated branch in the binder and falls through to Convert.ChangeType. It
        // still has to report what the JSON reader reports for the same input.
        CodeOf(PrimitiveConverter.ConvertToPrimitive<uint>("abc").Error!)
            .Should().Be(ValidationCodes.FormatInteger);

    [Fact]
    public void A_blank_composite_property_is_not_empty_rather_than_a_format_failure()
    {
        var blankAmount = Record.Exception(() =>
            JsonSerializer.Deserialize<Money>("""{"amount":"   ","currency":"USD"}"""));

        CodeOf(((TrellisJsonValidationException)blankAmount!).InvalidInput!)
            .Should().Be(ValidationCodes.ValueNotEmpty);
    }
}

public partial class ProducerIndependenceTicket : RequiredInt<ProducerIndependenceTicket>;

public partial class ProducerIndependenceOrderId : RequiredGuid<ProducerIndependenceOrderId>;

public enum ProducerIndependenceColor
{
    Red = 1,
    Green = 2,
}
