namespace Trellis.Asp.Tests;

using System;
using System.Text.Json;
using FluentAssertions;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Tests for <see cref="MaybePrimitiveJsonConverterFactory"/> and the underlying
/// <see cref="MaybePrimitiveJsonConverter{T}"/>. Closes the asymmetry where
/// <see cref="MaybeScalarValueJsonConverterFactory"/> shipped support for
/// <c>Maybe&lt;TScalar&gt;</c> (typed value objects) but not <c>Maybe&lt;TPrimitive&gt;</c>
/// (raw STJ-native primitives like <c>long</c>, <c>int</c>, <c>string</c>, <c>DateTime</c>,
/// etc.).
///
/// Each test isolates one primitive shape, configures <see cref="JsonSerializerOptions"/>
/// with the factory, and asserts round-trip of Some / None plus the JSON-null-read and
/// JSON-property-omitted variants where applicable.
/// </summary>
public class MaybePrimitiveJsonConverterTests
{
    private static JsonSerializerOptions Opts()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new MaybePrimitiveJsonConverterFactory());
        return o;
    }

    // ---------------------------------------------------------------------
    // long
    // ---------------------------------------------------------------------

    public sealed record LongDto(Maybe<long> Count);

    [Fact]
    public void Maybe_long_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new LongDto(Maybe.From(42L)), Opts());
        json.Should().Be("""{"Count":42}""");

        var rt = JsonSerializer.Deserialize<LongDto>(json, Opts());
        rt.Should().NotBeNull();
        rt!.Count.HasValue.Should().BeTrue();
        rt.Count.Value.Should().Be(42L);
    }

    [Fact]
    public void Maybe_long_None_serializes_as_null()
    {
        var json = JsonSerializer.Serialize(new LongDto(Maybe<long>.None), Opts());
        json.Should().Be("""{"Count":null}""");
    }

    [Fact]
    public void Maybe_long_null_JSON_reads_as_None()
    {
        var rt = JsonSerializer.Deserialize<LongDto>("""{"Count":null}""", Opts());
        rt.Should().NotBeNull();
        rt!.Count.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Maybe_long_omitted_property_reads_as_None()
    {
        var rt = JsonSerializer.Deserialize<LongDto>("{}", Opts());
        rt.Should().NotBeNull();
        rt!.Count.HasNoValue.Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // int
    // ---------------------------------------------------------------------

    public sealed record IntDto(Maybe<int> Value);

    [Fact]
    public void Maybe_int_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new IntDto(Maybe.From(7)), Opts());
        json.Should().Be("""{"Value":7}""");

        var rt = JsonSerializer.Deserialize<IntDto>(json, Opts());
        rt!.Value.Value.Should().Be(7);
    }

    [Fact]
    public void Maybe_int_None_serializes_as_null()
    {
        var json = JsonSerializer.Serialize(new IntDto(Maybe<int>.None), Opts());
        json.Should().Be("""{"Value":null}""");
    }

    // ---------------------------------------------------------------------
    // string
    // ---------------------------------------------------------------------

    public sealed record StringDto(Maybe<string> Label);

    [Fact]
    public void Maybe_string_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new StringDto(Maybe.From("hello")), Opts());
        json.Should().Be("""{"Label":"hello"}""");

        var rt = JsonSerializer.Deserialize<StringDto>(json, Opts());
        rt!.Label.Value.Should().Be("hello");
    }

    [Fact]
    public void Maybe_string_None_serializes_as_null()
    {
        var json = JsonSerializer.Serialize(new StringDto(Maybe<string>.None), Opts());
        json.Should().Be("""{"Label":null}""");
    }

    // ---------------------------------------------------------------------
    // decimal
    // ---------------------------------------------------------------------

    public sealed record DecimalDto(Maybe<decimal> Amount);

    [Fact]
    public void Maybe_decimal_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new DecimalDto(Maybe.From(12.5m)), Opts());
        json.Should().Be("""{"Amount":12.5}""");

        var rt = JsonSerializer.Deserialize<DecimalDto>(json, Opts());
        rt!.Amount.Value.Should().Be(12.5m);
    }

    // ---------------------------------------------------------------------
    // double, float
    // ---------------------------------------------------------------------

    public sealed record DoubleDto(Maybe<double> Score);

    [Fact]
    public void Maybe_double_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new DoubleDto(Maybe.From(3.14)), Opts());
        var rt = JsonSerializer.Deserialize<DoubleDto>(json, Opts());
        rt!.Score.Value.Should().Be(3.14);
    }

    public sealed record FloatDto(Maybe<float> Ratio);

    [Fact]
    public void Maybe_float_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new FloatDto(Maybe.From(0.5f)), Opts());
        var rt = JsonSerializer.Deserialize<FloatDto>(json, Opts());
        rt!.Ratio.Value.Should().Be(0.5f);
    }

    // ---------------------------------------------------------------------
    // short, byte
    // ---------------------------------------------------------------------

    public sealed record ShortDto(Maybe<short> Quantity);

    [Fact]
    public void Maybe_short_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new ShortDto(Maybe.From((short)100)), Opts());
        var rt = JsonSerializer.Deserialize<ShortDto>(json, Opts());
        rt!.Quantity.Value.Should().Be((short)100);
    }

    public sealed record ByteDto(Maybe<byte> Flag);

    [Fact]
    public void Maybe_byte_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new ByteDto(Maybe.From((byte)200)), Opts());
        var rt = JsonSerializer.Deserialize<ByteDto>(json, Opts());
        rt!.Flag.Value.Should().Be((byte)200);
    }

    // ---------------------------------------------------------------------
    // bool
    // ---------------------------------------------------------------------

    public sealed record BoolDto(Maybe<bool> Enabled);

    [Fact]
    public void Maybe_bool_round_trips_Some()
    {
        var json = JsonSerializer.Serialize(new BoolDto(Maybe.From(true)), Opts());
        json.Should().Be("""{"Enabled":true}""");

        var rt = JsonSerializer.Deserialize<BoolDto>(json, Opts());
        rt!.Enabled.Value.Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // Guid
    // ---------------------------------------------------------------------

    public sealed record GuidDto(Maybe<Guid> Id);

    [Fact]
    public void Maybe_Guid_round_trips_Some()
    {
        var g = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var json = JsonSerializer.Serialize(new GuidDto(Maybe.From(g)), Opts());
        var rt = JsonSerializer.Deserialize<GuidDto>(json, Opts());
        rt!.Id.Value.Should().Be(g);
    }

    // ---------------------------------------------------------------------
    // DateTime, DateTimeOffset
    // ---------------------------------------------------------------------

    public sealed record DateTimeDto(Maybe<DateTime> When);

    [Fact]
    public void Maybe_DateTime_round_trips_Some()
    {
        var dt = new DateTime(2026, 5, 15, 10, 30, 0, DateTimeKind.Utc);
        var json = JsonSerializer.Serialize(new DateTimeDto(Maybe.From(dt)), Opts());
        var rt = JsonSerializer.Deserialize<DateTimeDto>(json, Opts());
        rt!.When.Value.Should().Be(dt);
    }

    public sealed record DateTimeOffsetDto(Maybe<DateTimeOffset> When);

    [Fact]
    public void Maybe_DateTimeOffset_round_trips_Some()
    {
        var dto = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.FromHours(-7));
        var json = JsonSerializer.Serialize(new DateTimeOffsetDto(Maybe.From(dto)), Opts());
        var rt = JsonSerializer.Deserialize<DateTimeOffsetDto>(json, Opts());
        rt!.When.Value.Should().Be(dto);
    }

    // ---------------------------------------------------------------------
    // Composition with other JSON shapes — Maybe<TPrimitive> works alongside
    // non-Maybe properties and Maybe<TScalar> properties via the scalar factory.
    // ---------------------------------------------------------------------

    public sealed record MixedDto(string Id, Maybe<long> Count, Maybe<string> Label);

    [Fact]
    public void Maybe_TPrimitive_composes_with_non_Maybe_properties()
    {
        var dto = new MixedDto("abc", Maybe.From(7L), Maybe<string>.None);
        var json = JsonSerializer.Serialize(dto, Opts());
        json.Should().Contain("\"Id\":\"abc\"")
            .And.Contain("\"Count\":7")
            .And.Contain("\"Label\":null");

        var rt = JsonSerializer.Deserialize<MixedDto>(json, Opts());
        rt!.Id.Should().Be("abc");
        rt.Count.Value.Should().Be(7L);
        rt.Label.HasNoValue.Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // Factory CanConvert boundary — unsupported primitives must NOT be claimed
    // by this factory (they should fall through to STJ default / fail elsewhere).
    // ---------------------------------------------------------------------

    [Fact]
    public void Factory_CanConvert_returns_true_for_supported_primitives()
    {
        var f = new MaybePrimitiveJsonConverterFactory();
        f.CanConvert(typeof(Maybe<string>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<decimal>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<int>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<long>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<short>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<byte>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<double>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<float>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<bool>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<Guid>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<DateTime>)).Should().BeTrue();
        f.CanConvert(typeof(Maybe<DateTimeOffset>)).Should().BeTrue();
    }

    [Fact]
    public void Factory_CanConvert_returns_false_for_unsupported_shapes()
    {
        var f = new MaybePrimitiveJsonConverterFactory();
        f.CanConvert(typeof(Maybe<DateOnly>)).Should().BeFalse("DateOnly is not in the allowed list");
        f.CanConvert(typeof(Maybe<TimeOnly>)).Should().BeFalse("TimeOnly is not in the allowed list");
        f.CanConvert(typeof(Maybe<uint>)).Should().BeFalse("unsigned numerics are not in the allowed list");
        f.CanConvert(typeof(Maybe<ulong>)).Should().BeFalse("unsigned numerics are not in the allowed list");
        f.CanConvert(typeof(Maybe<int[]>)).Should().BeFalse("arrays are not in the allowed list");
        f.CanConvert(typeof(Maybe<object>)).Should().BeFalse("object is not in the allowed list");
        f.CanConvert(typeof(long)).Should().BeFalse("a bare primitive is not Maybe<T>");
        f.CanConvert(typeof(string)).Should().BeFalse("a bare primitive is not Maybe<T>");
    }

    [Fact]
    public void Factory_does_not_claim_Maybe_of_scalar_VO()
    {
        // Maybe<TScalar> where TScalar : IScalarValue<,> is the existing scalar factory's
        // territory. The new primitive factory must NOT claim those — its allowed list is
        // closed to STJ-native primitives only, so the two factories don't compete.
        var f = new MaybePrimitiveJsonConverterFactory();
        f.CanConvert(typeof(Maybe<MaybeScalarValueJsonConverterTests.Email>))
            .Should().BeFalse("scalar-VO shapes belong to MaybeScalarValueJsonConverterFactory");
    }
}