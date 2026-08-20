namespace Trellis.Core.Tests.Primitives;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Pins the two-hop carrier that lets a parse failure satisfy two contracts at once.
/// </summary>
/// <remarks>
/// <see cref="IParsable{TSelf}"/> requires a <see cref="FormatException"/>; the ASP boundary reads
/// structure only from a <see cref="TrellisJsonValidationException"/>, which is a
/// <see cref="JsonException"/>. No single type is both, so <c>Parse</c> throws
/// <see cref="TrellisValidationFormatException"/> and the converter rethrows it carrying the same
/// structured failure.
/// </remarks>
public sealed class ParsableJsonConverterCarrierTests
{
    [Fact]
    public void The_carrier_is_a_FormatException_so_IParsable_callers_are_unaffected()
    {
        var ex = Assert.Throws<TrellisValidationFormatException>(
            () => CarrierValue.Parse("bad", CultureInfo.InvariantCulture));

        ex.Should().BeAssignableTo<FormatException>();
        ex.Message.Should().Be("Value must not be 'bad'.", "the flattened message is unchanged");
        ex.InvalidInput!.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/carrier");
    }

    [Fact]
    public void The_converter_republishes_the_structure_as_a_json_validation_exception()
    {
        var ex = Assert.Throws<TrellisJsonValidationException>(
            () => JsonSerializer.Deserialize<CarrierValue>("\"bad\""));

        ex.InvalidInput.Should().NotBeNull("dropping it here is what left a per-field failure rendering as an untyped problem");
        ex.InvalidInput!.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/carrier");
        ex.InnerException.Should().BeOfType<TrellisValidationFormatException>();
    }

    [Fact]
    public void A_plain_format_failure_still_takes_the_general_catch()
    {
        var ex = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PlainValue>("\"bad\""));

        ex.Should().NotBeOfType<TrellisJsonValidationException>(
            "the general catch is retained for genuine non-structured parse failures");
    }
}

[JsonConverter(typeof(ParsableJsonConverter<CarrierValue>))]
public sealed class CarrierValue : IParsable<CarrierValue>
{
    private CarrierValue(string value) => Value = value;

    public string Value { get; }

    public static CarrierValue Parse(string s, IFormatProvider? provider) =>
        s == "bad"
            ? throw new TrellisValidationFormatException(
                "Value must not be 'bad'.",
                Error.InvalidInput.ForField(InputPointer.ForProperty("carrier"), "validation.error", "Value must not be 'bad'."))
            : new CarrierValue(s);

    public static bool TryParse(string? s, IFormatProvider? provider, out CarrierValue result)
    {
        result = new CarrierValue(s ?? string.Empty);
        return s != "bad";
    }

    public override string ToString() => Value;
}

[JsonConverter(typeof(ParsableJsonConverter<PlainValue>))]
public sealed class PlainValue : IParsable<PlainValue>
{
    private PlainValue(string value) => Value = value;

    public string Value { get; }

    public static PlainValue Parse(string s, IFormatProvider? provider) =>
        s == "bad" ? throw new FormatException("Nope.") : new PlainValue(s);

    public static bool TryParse(string? s, IFormatProvider? provider, out PlainValue result)
    {
        result = new PlainValue(s ?? string.Empty);
        return s != "bad";
    }

    public override string ToString() => Value;
}