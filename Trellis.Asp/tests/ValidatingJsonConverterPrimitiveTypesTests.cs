namespace Trellis.Asp.Tests;

using System;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Trellis;
using Trellis.Asp.Validation;
using Trellis.Testing;
using Xunit;

/// <summary>
/// Tests for ValidatingJsonConverter to ensure all primitive types are serialized correctly.
/// This tests the WritePrimitiveValue switch statement with all 12 type branches.
/// </summary>
public class ValidatingJsonConverterPrimitiveTypesTests
{
    #region Test Value Objects for Each Primitive Type

    // String
    public class StringVO : ScalarValueObject<StringVO, string>, IScalarValue<StringVO, string>
    {
        private StringVO(string value) : base(value) { }
        public static Result<StringVO> TryCreate(string? value, string? fieldName = null) =>
            string.IsNullOrEmpty(value) ? Result.Fail<StringVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Required" }))) : Result.Ok(new StringVO(value));
    }

    // Guid
    public class GuidVO : ScalarValueObject<GuidVO, Guid>, IScalarValue<GuidVO, Guid>
    {
        private GuidVO(Guid value) : base(value) { }
        public static Result<GuidVO> TryCreate(Guid value, string? fieldName = null) =>
            value == Guid.Empty ? Result.Fail<GuidVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Required" }))) : Result.Ok(new GuidVO(value));
        public static Result<GuidVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // Int
    public class IntVO : ScalarValueObject<IntVO, int>, IScalarValue<IntVO, int>
    {
        private IntVO(int value) : base(value) { }
        public static Result<IntVO> TryCreate(int value, string? fieldName = null) =>
            value < 0 ? Result.Fail<IntVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Negative" }))) : Result.Ok(new IntVO(value));
        public static Result<IntVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // Long
    public class LongVO : ScalarValueObject<LongVO, long>, IScalarValue<LongVO, long>
    {
        private LongVO(long value) : base(value) { }
        public static Result<LongVO> TryCreate(long value, string? fieldName = null) =>
            value < 0 ? Result.Fail<LongVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Negative" }))) : Result.Ok(new LongVO(value));
        public static Result<LongVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // Double
    public class DoubleVO : ScalarValueObject<DoubleVO, double>, IScalarValue<DoubleVO, double>
    {
        private DoubleVO(double value) : base(value) { }
        public static Result<DoubleVO> TryCreate(double value, string? fieldName = null) =>
            double.IsNaN(value) ? Result.Fail<DoubleVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "NaN" }))) : Result.Ok(new DoubleVO(value));
        public static Result<DoubleVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // Float
    public class FloatVO : ScalarValueObject<FloatVO, float>, IScalarValue<FloatVO, float>
    {
        private FloatVO(float value) : base(value) { }
        public static Result<FloatVO> TryCreate(float value, string? fieldName = null) =>
            float.IsNaN(value) ? Result.Fail<FloatVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "NaN" }))) : Result.Ok(new FloatVO(value));
        public static Result<FloatVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // Decimal
    public class DecimalVO : ScalarValueObject<DecimalVO, decimal>, IScalarValue<DecimalVO, decimal>
    {
        private DecimalVO(decimal value) : base(value) { }
        public static Result<DecimalVO> TryCreate(decimal value, string? fieldName = null) =>
            value < 0 ? Result.Fail<DecimalVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Negative" }))) : Result.Ok(new DecimalVO(value));
        public static Result<DecimalVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // Bool
    public class BoolVO : ScalarValueObject<BoolVO, bool>, IScalarValue<BoolVO, bool>
    {
        private BoolVO(bool value) : base(value) { }
        public static Result<BoolVO> TryCreate(bool value, string? fieldName = null) =>
            Result.Ok(new BoolVO(value));
        public static Result<BoolVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // DateTime
    public class DateTimeVO : ScalarValueObject<DateTimeVO, DateTime>, IScalarValue<DateTimeVO, DateTime>
    {
        private DateTimeVO(DateTime value) : base(value) { }
        public static Result<DateTimeVO> TryCreate(DateTime value, string? fieldName = null) =>
            value == DateTime.MinValue ? Result.Fail<DateTimeVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "MinValue" }))) : Result.Ok(new DateTimeVO(value));
        public static Result<DateTimeVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // DateTimeOffset
    public class DateTimeOffsetVO : ScalarValueObject<DateTimeOffsetVO, DateTimeOffset>, IScalarValue<DateTimeOffsetVO, DateTimeOffset>
    {
        private DateTimeOffsetVO(DateTimeOffset value) : base(value) { }
        public static Result<DateTimeOffsetVO> TryCreate(DateTimeOffset value, string? fieldName = null) =>
            value == DateTimeOffset.MinValue ? Result.Fail<DateTimeOffsetVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "MinValue" }))) : Result.Ok(new DateTimeOffsetVO(value));
        public static Result<DateTimeOffsetVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // DateOnly (.NET 6+)
    public class DateOnlyVO : ScalarValueObject<DateOnlyVO, DateOnly>, IScalarValue<DateOnlyVO, DateOnly>
    {
        private DateOnlyVO(DateOnly value) : base(value) { }
        public static Result<DateOnlyVO> TryCreate(DateOnly value, string? fieldName = null) =>
            value == DateOnly.MinValue ? Result.Fail<DateOnlyVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "MinValue" }))) : Result.Ok(new DateOnlyVO(value));
        public static Result<DateOnlyVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    // TimeOnly (.NET 6+)
    public class TimeOnlyVO : ScalarValueObject<TimeOnlyVO, TimeOnly>, IScalarValue<TimeOnlyVO, TimeOnly>
    {
        private TimeOnlyVO(TimeOnly value) : base(value) { }
        public static Result<TimeOnlyVO> TryCreate(TimeOnly value, string? fieldName = null) =>
            value == TimeOnly.MinValue ? Result.Fail<TimeOnlyVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "MinValue" }))) : Result.Ok(new TimeOnlyVO(value));
        public static Result<TimeOnlyVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class TimeSpanVO : ScalarValueObject<TimeSpanVO, TimeSpan>, IScalarValue<TimeSpanVO, TimeSpan>
    {
        private TimeSpanVO(TimeSpan value) : base(value) { }
        public static Result<TimeSpanVO> TryCreate(TimeSpan value, string? fieldName = null) =>
            value < TimeSpan.Zero ? Result.Fail<TimeSpanVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Negative" }))) : Result.Ok(new TimeSpanVO(value));
        public static Result<TimeSpanVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class ShortVO : ScalarValueObject<ShortVO, short>, IScalarValue<ShortVO, short>
    {
        private ShortVO(short value) : base(value) { }
        public static Result<ShortVO> TryCreate(short value, string? fieldName = null) =>
            value < 0 ? Result.Fail<ShortVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Negative" }))) : Result.Ok(new ShortVO(value));
        public static Result<ShortVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class ByteVO : ScalarValueObject<ByteVO, byte>, IScalarValue<ByteVO, byte>
    {
        private ByteVO(byte value) : base(value) { }
        public static Result<ByteVO> TryCreate(byte value, string? fieldName = null) =>
            Result.Ok(new ByteVO(value));
        public static Result<ByteVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class SByteVO : ScalarValueObject<SByteVO, sbyte>, IScalarValue<SByteVO, sbyte>
    {
        private SByteVO(sbyte value) : base(value) { }
        public static Result<SByteVO> TryCreate(sbyte value, string? fieldName = null) =>
            value < 0 ? Result.Fail<SByteVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Negative" }))) : Result.Ok(new SByteVO(value));
        public static Result<SByteVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class UShortVO : ScalarValueObject<UShortVO, ushort>, IScalarValue<UShortVO, ushort>
    {
        private UShortVO(ushort value) : base(value) { }
        public static Result<UShortVO> TryCreate(ushort value, string? fieldName = null) =>
            Result.Ok(new UShortVO(value));
        public static Result<UShortVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class UIntVO : ScalarValueObject<UIntVO, uint>, IScalarValue<UIntVO, uint>
    {
        private UIntVO(uint value) : base(value) { }
        public static Result<UIntVO> TryCreate(uint value, string? fieldName = null) =>
            Result.Ok(new UIntVO(value));
        public static Result<UIntVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class ULongVO : ScalarValueObject<ULongVO, ulong>, IScalarValue<ULongVO, ulong>
    {
        private ULongVO(ulong value) : base(value) { }
        public static Result<ULongVO> TryCreate(ulong value, string? fieldName = null) =>
            Result.Ok(new ULongVO(value));
        public static Result<ULongVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public readonly record struct CustomPrimitive(string Value) : IComparable
    {
        public static bool operator <(CustomPrimitive left, CustomPrimitive right) =>
            string.Compare(left.Value, right.Value, StringComparison.Ordinal) < 0;

        public static bool operator <=(CustomPrimitive left, CustomPrimitive right) =>
            string.Compare(left.Value, right.Value, StringComparison.Ordinal) <= 0;

        public static bool operator >(CustomPrimitive left, CustomPrimitive right) =>
            string.Compare(left.Value, right.Value, StringComparison.Ordinal) > 0;

        public static bool operator >=(CustomPrimitive left, CustomPrimitive right) =>
            string.Compare(left.Value, right.Value, StringComparison.Ordinal) >= 0;

        public int CompareTo(object? obj) =>
            obj is CustomPrimitive other
                ? string.Compare(Value, other.Value, StringComparison.Ordinal)
                : 1;

        public override string ToString() => Value;
    }

    public class CustomPrimitiveVO : ScalarValueObject<CustomPrimitiveVO, CustomPrimitive>, IScalarValue<CustomPrimitiveVO, CustomPrimitive>
    {
        private CustomPrimitiveVO(CustomPrimitive value) : base(value) { }
        public static Result<CustomPrimitiveVO> TryCreate(CustomPrimitive value, string? fieldName = null) =>
            Result.Ok(new CustomPrimitiveVO(value));
        public static Result<CustomPrimitiveVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region String Tests

    [Fact]
    public void Write_String_WritesCorrectly()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<StringVO, string>();
        var vo = StringVO.TryCreate("Hello World", null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"Hello World\"");
    }

    [Fact]
    public void RoundTrip_String_PreservesValue()
    {
        var vo = StringVO.TryCreate("Test", null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<StringVO, string>());
        roundTripped!.Value.Should().Be("Test");
    }

    #endregion

    #region Guid Tests

    [Fact]
    public void Write_Guid_WritesCorrectly()
    {
        // Arrange
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var converter = new ValidatingJsonConverter<GuidVO, Guid>();
        var vo = GuidVO.TryCreate(guid, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be($"\"{guid}\"");
    }

    [Fact]
    public void RoundTrip_Guid_PreservesValue()
    {
        var guid = Guid.NewGuid();
        var vo = GuidVO.TryCreate(guid, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<GuidVO, Guid>());
        roundTripped!.Value.Should().Be(guid);
    }

    #endregion

    #region Int Tests

    [Fact]
    public void Write_Int_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<IntVO, int>();
        var vo = IntVO.TryCreate(42, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("42");
    }

    [Fact]
    public void RoundTrip_Int_PreservesValue()
    {
        var vo = IntVO.TryCreate(999, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<IntVO, int>());
        roundTripped!.Value.Should().Be(999);
    }

    [Fact]
    public void Write_Int_Zero()
    {
        var converter = new ValidatingJsonConverter<IntVO, int>();
        var vo = IntVO.TryCreate(0, null).Unwrap();
        var json = Serialize(vo, converter);
        json.Should().Be("0");
    }

    [Fact]
    public void Write_Int_MaxValue()
    {
        var converter = new ValidatingJsonConverter<IntVO, int>();
        var vo = IntVO.TryCreate(int.MaxValue, null).Unwrap();
        var json = Serialize(vo, converter);
        json.Should().Be(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    #endregion

    #region Long Tests

    [Fact]
    public void Write_Long_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<LongVO, long>();
        var vo = LongVO.TryCreate(9223372036854775807L, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("9223372036854775807");
    }

    [Fact]
    public void RoundTrip_Long_PreservesValue()
    {
        var vo = LongVO.TryCreate(123456789012345L, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<LongVO, long>());
        roundTripped!.Value.Should().Be(123456789012345L);
    }

    #endregion

    #region Double Tests

    [Fact]
    public void Write_Double_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<DoubleVO, double>();
        var vo = DoubleVO.TryCreate(3.14159, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("3.14159");
    }

    [Fact]
    public void RoundTrip_Double_PreservesValue()
    {
        var vo = DoubleVO.TryCreate(2.71828, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<DoubleVO, double>());
        roundTripped!.Value.Should().BeApproximately(2.71828, 0.00001);
    }

    // Note: JSON doesn't support Infinity/NaN by default
    // Value objects should validate against such values in TryCreate

    #endregion

    #region Float Tests

    [Fact]
    public void Write_Float_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<FloatVO, float>();
        var vo = FloatVO.TryCreate(1.23f, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("1.23");
    }

    [Fact]
    public void RoundTrip_Float_PreservesValue()
    {
        var vo = FloatVO.TryCreate(9.99f, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<FloatVO, float>());
        roundTripped!.Value.Should().BeApproximately(9.99f, 0.01f);
    }

    #endregion

    #region Decimal Tests

    [Fact]
    public void Write_Decimal_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<DecimalVO, decimal>();
        var vo = DecimalVO.TryCreate(99.99m, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("99.99");
    }

    [Fact]
    public void RoundTrip_Decimal_PreservesValue()
    {
        var vo = DecimalVO.TryCreate(123.456m, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<DecimalVO, decimal>());
        roundTripped!.Value.Should().Be(123.456m);
    }

    [Fact]
    public void Write_Decimal_HighPrecision()
    {
        var converter = new ValidatingJsonConverter<DecimalVO, decimal>();
        var vo = DecimalVO.TryCreate(0.123456789012345678901234567890m, null).Unwrap();
        var json = Serialize(vo, converter);
        // JSON serialization preserves significant digits but may round the last few digits
        json.Should().StartWith("0.123456789012345678901234");
    }

    #endregion

    #region Bool Tests

    [Fact]
    public void Write_Bool_True()
    {
        var converter = new ValidatingJsonConverter<BoolVO, bool>();
        var vo = BoolVO.TryCreate(true, null).Unwrap();
        var json = Serialize(vo, converter);
        json.Should().Be("true");
    }

    [Fact]
    public void Write_Bool_False()
    {
        var converter = new ValidatingJsonConverter<BoolVO, bool>();
        var vo = BoolVO.TryCreate(false, null).Unwrap();
        var json = Serialize(vo, converter);
        json.Should().Be("false");
    }

    [Fact]
    public void RoundTrip_Bool_PreservesValue()
    {
        var vo = BoolVO.TryCreate(true, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<BoolVO, bool>());
        roundTripped!.Value.Should().BeTrue();
    }

    #endregion

    #region DateTime Tests

    [Fact]
    public void Write_DateTime_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<DateTimeVO, DateTime>();
        var dt = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc);
        var vo = DateTimeVO.TryCreate(dt, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Contain("2024-01-15");
    }

    [Fact]
    public void RoundTrip_DateTime_PreservesValue()
    {
        var dt = new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc);
        var vo = DateTimeVO.TryCreate(dt, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<DateTimeVO, DateTime>());
        // Note: DateTime round-trip may lose some precision, so we check for closeness
        roundTripped!.Value.Should().BeCloseTo(dt, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region DateTimeOffset Tests

    [Fact]
    public void Write_DateTimeOffset_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<DateTimeOffsetVO, DateTimeOffset>();
        var dto = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.FromHours(-5));
        var vo = DateTimeOffsetVO.TryCreate(dto, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Contain("2024-01-15");
        json.Should().Contain("05:00"); // Offset
    }

    [Fact]
    public void RoundTrip_DateTimeOffset_PreservesValue()
    {
        var dto = new DateTimeOffset(2024, 12, 25, 18, 0, 0, TimeSpan.FromHours(2));
        var vo = DateTimeOffsetVO.TryCreate(dto, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<DateTimeOffsetVO, DateTimeOffset>());
        roundTripped!.Value.Should().Be(dto);
    }

    #endregion

    #region DateOnly Tests

    [Fact]
    public void Write_DateOnly_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<DateOnlyVO, DateOnly>();
        var date = new DateOnly(2024, 3, 15);
        var vo = DateOnlyVO.TryCreate(date, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"2024-03-15\"");
    }

    [Fact]
    public void RoundTrip_DateOnly_PreservesValue()
    {
        var date = new DateOnly(2024, 7, 4);
        var vo = DateOnlyVO.TryCreate(date, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<DateOnlyVO, DateOnly>());
        roundTripped!.Value.Should().Be(date);
    }

    [Fact]
    public void Read_InvalidDateOnly_CollectsInvalidValueError()
    {
        var converter = new ValidatingJsonConverter<DateOnlyVO, DateOnly>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"not-a-date\""));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            var result = converter.Read(ref reader, typeof(DateOnlyVO), new JsonSerializerOptions());

            result.Should().BeNull();
            ValidationErrorsContext.GetUnprocessableContent()!
                .Fields
                .Items
                .Should().ContainSingle(v =>
                    v.Field.Path == "/dateOnlyVO"
                    && v.Detail == "'dateOnlyVO' is not a valid DateOnly.");
        }
    }

    #endregion

    #region TimeOnly Tests

    [Fact]
    public void Write_TimeOnly_WritesCorrectly()
    {
        var converter = new ValidatingJsonConverter<TimeOnlyVO, TimeOnly>();
        var time = new TimeOnly(14, 30, 45);
        var vo = TimeOnlyVO.TryCreate(time, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"14:30:45.0000000\"");
    }

    [Fact]
    public void RoundTrip_TimeOnly_PreservesValue()
    {
        var time = new TimeOnly(9, 15, 30);
        var vo = TimeOnlyVO.TryCreate(time, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<TimeOnlyVO, TimeOnly>());
        roundTripped!.Value.Should().Be(time);
    }

    [Fact]
    public void Read_InvalidTimeOnly_CollectsInvalidValueError()
    {
        var converter = new ValidatingJsonConverter<TimeOnlyVO, TimeOnly>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"not-a-time\""));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            var result = converter.Read(ref reader, typeof(TimeOnlyVO), new JsonSerializerOptions());

            result.Should().BeNull();
            ValidationErrorsContext.GetUnprocessableContent()!
                .Fields
                .Items
                .Should().ContainSingle(v =>
                    v.Field.Path == "/timeOnlyVO"
                    && v.Detail == "'timeOnlyVO' is not a valid TimeOnly.");
        }
    }

    #endregion

    #region TimeSpan Tests

    [Fact]
    public void Write_TimeSpan_WritesRoundTripFormat()
    {
        var converter = new ValidatingJsonConverter<TimeSpanVO, TimeSpan>();
        var value = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3);
        var vo = TimeSpanVO.TryCreate(value, null).Unwrap();

        var json = Serialize(vo, converter);

        json.Should().Be("\"01:02:03\"");
    }

    [Fact]
    public void RoundTrip_TimeSpan_PreservesValue()
    {
        var value = TimeSpan.FromDays(1) + TimeSpan.FromMilliseconds(456);
        var vo = TimeSpanVO.TryCreate(value, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<TimeSpanVO, TimeSpan>());

        roundTripped!.Value.Should().Be(value);
    }

    [Fact]
    public void Read_InvalidTimeSpan_CollectsInvalidValueError()
    {
        var converter = new ValidatingJsonConverter<TimeSpanVO, TimeSpan>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"not-a-duration\""));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            var result = converter.Read(ref reader, typeof(TimeSpanVO), new JsonSerializerOptions());

            result.Should().BeNull();
            ValidationErrorsContext.GetUnprocessableContent()!
                .Fields
                .Items
                .Should().ContainSingle(v =>
                    v.Field.Path == "/timeSpanVO"
                    && v.Detail == "'timeSpanVO' is not a valid TimeSpan.");
        }
    }

    #endregion

    #region Short Tests

    [Fact]
    public void Write_Short_WritesNumber()
    {
        var converter = new ValidatingJsonConverter<ShortVO, short>();
        var vo = ShortVO.TryCreate(123, null).Unwrap();

        var json = Serialize(vo, converter);

        json.Should().Be("123");
    }

    [Fact]
    public void RoundTrip_Short_PreservesValue()
    {
        var vo = ShortVO.TryCreate(321, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<ShortVO, short>());

        roundTripped!.Value.Should().Be((short)321);
    }

    #endregion

    #region Byte Tests

    [Fact]
    public void Write_Byte_WritesNumber()
    {
        var converter = new ValidatingJsonConverter<ByteVO, byte>();
        var vo = ByteVO.TryCreate(200, null).Unwrap();

        var json = Serialize(vo, converter);

        json.Should().Be("200");
    }

    [Fact]
    public void RoundTrip_Byte_PreservesValue()
    {
        var vo = ByteVO.TryCreate(201, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<ByteVO, byte>());

        roundTripped!.Value.Should().Be((byte)201);
    }

    #endregion

    #region SByte Tests

    [Fact]
    public void Write_SByte_WritesNumber()
    {
        var converter = new ValidatingJsonConverter<SByteVO, sbyte>();
        var vo = SByteVO.TryCreate(100, null).Unwrap();

        var json = Serialize(vo, converter);

        json.Should().Be("100");
    }

    [Fact]
    public void RoundTrip_SByte_PreservesValue()
    {
        var vo = SByteVO.TryCreate(101, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<SByteVO, sbyte>());

        roundTripped!.Value.Should().Be((sbyte)101);
    }

    #endregion

    #region UShort Tests

    [Fact]
    public void Write_UShort_WritesNumber()
    {
        var converter = new ValidatingJsonConverter<UShortVO, ushort>();
        var vo = UShortVO.TryCreate(65000, null).Unwrap();

        var json = Serialize(vo, converter);

        json.Should().Be("65000");
    }

    [Fact]
    public void RoundTrip_UShort_PreservesValue()
    {
        var vo = UShortVO.TryCreate(65001, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<UShortVO, ushort>());

        roundTripped!.Value.Should().Be((ushort)65001);
    }

    #endregion

    #region UInt Tests

    [Fact]
    public void Write_UInt_WritesNumber()
    {
        var converter = new ValidatingJsonConverter<UIntVO, uint>();
        var vo = UIntVO.TryCreate(4000000000u, null).Unwrap();

        var json = Serialize(vo, converter);

        json.Should().Be("4000000000");
    }

    [Fact]
    public void RoundTrip_UInt_PreservesValue()
    {
        var vo = UIntVO.TryCreate(4000000001u, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<UIntVO, uint>());

        roundTripped!.Value.Should().Be(4000000001u);
    }

    #endregion

    #region ULong Tests

    [Fact]
    public void Write_ULong_WritesNumber()
    {
        var converter = new ValidatingJsonConverter<ULongVO, ulong>();
        var vo = ULongVO.TryCreate(18446744073709551614ul, null).Unwrap();

        var json = Serialize(vo, converter);

        json.Should().Be("18446744073709551614");
    }

    [Fact]
    public void RoundTrip_ULong_PreservesValue()
    {
        var vo = ULongVO.TryCreate(18446744073709551615ul, null).Unwrap();
        var roundTripped = RoundTrip(vo, new ValidatingJsonConverter<ULongVO, ulong>());

        roundTripped!.Value.Should().Be(18446744073709551615ul);
    }

    #endregion

    #region Unsupported Primitive Tests

    [Fact]
    public void Read_UnsupportedPrimitive_CollectsValidationError()
    {
        var converter = new ValidatingJsonConverter<CustomPrimitiveVO, CustomPrimitive>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"abc\""));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            var result = converter.Read(ref reader, typeof(CustomPrimitiveVO), new JsonSerializerOptions());

            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            ValidationErrorsContext.GetUnprocessableContent()!
                .Fields
                .Items
                .Should().ContainSingle(v =>
                    v.Field.Path == "/customPrimitiveVO"
                    && v.Detail == "Primitive type 'CustomPrimitive' is not supported by the Trellis validation JSON converter. Provide a custom JsonConverter.");
        }
    }

    #endregion

    #region Helper Methods

    private static string Serialize<TValueObject, TPrimitive>(TValueObject? vo, ValidatingJsonConverter<TValueObject, TPrimitive> converter)
        where TValueObject : class, IScalarValue<TValueObject, TPrimitive>
        where TPrimitive : IComparable
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        converter.Write(writer, vo, new JsonSerializerOptions());
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static TValueObject? RoundTrip<TValueObject, TPrimitive>(TValueObject vo, ValidatingJsonConverter<TValueObject, TPrimitive> converter)
        where TValueObject : class, IScalarValue<TValueObject, TPrimitive>
        where TPrimitive : IComparable
    {
        var json = Serialize(vo, converter);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            return converter.Read(ref reader, typeof(TValueObject), new JsonSerializerOptions());
        }
    }

    #endregion
}