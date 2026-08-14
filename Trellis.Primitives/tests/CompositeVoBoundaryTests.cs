namespace Trellis.Primitives.Tests;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Characterization tests probing the actual support boundary of
/// <see cref="CompositeValueObjectJsonConverter{T}"/> for non-basic interior property shapes
/// (Maybe&lt;T&gt;, plain enums, nullable structs, arrays, DateOnly/TimeOnly, unsigned numerics,
/// nested composite VOs, custom enum-backed scalars).
///
/// Each fixture isolates one shape so the first failure can't mask later cases. Each shape
/// covers serialize and deserialize of a representative non-null value (plus the null-write
/// regression cases for unsupported shapes, and a write/read symmetry pin for the nullable
/// scalar VO interior) — sufficient to characterize whether the boundary is "throws loudly"
/// (acceptable; documented in cookbook Recipe 13) or "silently wrong" (real bug). Missing-
/// property and explicit-JSON-null variants are not exhaustively covered per-shape; the
/// converter's required-property and primitive-null behavior is exercised by the existing
/// <see cref="CompositeValueObjectJsonConverterTests"/> against the supported primitives.
///
/// Outcome of these tests informs P1-Test-1 in BACKLOG.md: do we extend converter support, or
/// document the constraint and consider an analyzer rule (separate ID; not TRLS020 which is
/// DTO-side)?
/// </summary>
public partial class CompositeVoBoundaryTests
{
    // Shared base — every fixture composite VO derives from this.
    public abstract class BoundaryVo : ValueObject
    {
        protected override void GetEqualityComponents(ref EqualityComponents components) { }
    }

    // ---------------------------------------------------------------------
    // Shape 1 — Maybe<int> interior
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<MaybeIntVo>))]
    public sealed class MaybeIntVo : BoundaryVo
    {
        public Maybe<int> Count { get; private set; }
        private MaybeIntVo() { }
        private MaybeIntVo(Maybe<int> count) => Count = count;
        public static MaybeIntVo Create(Maybe<int> c) => new(c);
        public static Result<MaybeIntVo> TryCreate(Maybe<int> count, string? fieldName = null) =>
            Result.Ok(new MaybeIntVo(count));
    }

    [Fact]
    public void Maybe_int_interior_throws_on_serialize()
    {
        var vo = MaybeIntVo.Create(Maybe<int>.From(42));
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Maybe_int_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<MaybeIntVo>("{\"count\":42}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 2 — Maybe<string> interior
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<MaybeStringVo>))]
    public sealed class MaybeStringVo : BoundaryVo
    {
        public Maybe<string> Label { get; private set; }
        private MaybeStringVo() { }
        private MaybeStringVo(Maybe<string> l) => Label = l;
        public static MaybeStringVo Create(Maybe<string> l) => new(l);
        public static Result<MaybeStringVo> TryCreate(Maybe<string> label, string? fieldName = null) =>
            Result.Ok(new MaybeStringVo(label));
    }

    [Fact]
    public void Maybe_string_interior_throws_on_serialize()
    {
        var vo = MaybeStringVo.Create(Maybe<string>.From("hi"));
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Maybe_string_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<MaybeStringVo>("{\"label\":\"hi\"}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 3 — Plain enum interior (not wrapped in RequiredEnum<>)
    // ---------------------------------------------------------------------

    public enum TestColor { Red, Green, Blue }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<PlainEnumVo>))]
    public sealed class PlainEnumVo : BoundaryVo
    {
        public TestColor Color { get; private set; }
        private PlainEnumVo() { }
        private PlainEnumVo(TestColor c) => Color = c;
        public static PlainEnumVo Create(TestColor c) => new(c);
        public static Result<PlainEnumVo> TryCreate(TestColor color, string? fieldName = null) =>
            Result.Ok(new PlainEnumVo(color));
    }

    [Fact]
    public void Plain_enum_interior_throws_on_serialize()
    {
        var vo = PlainEnumVo.Create(TestColor.Red);
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Plain_enum_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<PlainEnumVo>("{\"color\":\"Red\"}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 4 — Nullable<int> (int?) interior
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<NullableIntVo>))]
    public sealed class NullableIntVo : BoundaryVo
    {
        public int? Count { get; private set; }
        private NullableIntVo() { }
        private NullableIntVo(int? c) => Count = c;
        public static NullableIntVo Create(int? c) => new(c);
        public static Result<NullableIntVo> TryCreate(int? count, string? fieldName = null) =>
            Result.Ok(new NullableIntVo(count));
    }

    [Fact]
    public void Nullable_int_interior_throws_on_serialize()
    {
        var vo = NullableIntVo.Create(42);
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Nullable_int_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<NullableIntVo>("{\"count\":42}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Nullable_int_interior_with_null_value_throws_on_serialize()
    {
        // F1 from rubber-duck round 2: WritePrimitive used to write `null` before validating
        // the primitive type, so an unsupported shape with a null payload would serialize
        // silently while deserialize of the same shape threw — asymmetric write/read where
        // the converter publishes JSON it cannot itself parse back. Both directions must
        // fail loudly with the same "unsupported primitive" diagnostic.
        var vo = NullableIntVo.Create(null);
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void String_array_interior_with_null_value_throws_on_serialize()
    {
        // Same asymmetry hits null arrays — `string[]` is unsupported, but a null array
        // would serialize as JSON `null` if the write path checked null before the
        // primitive-type validation. Validate-first guarantees the failure is symmetric.
        var vo = StringArrayVo.Create(null!);
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Nested_composite_VO_interior_with_null_value_throws_on_serialize()
    {
        // Nested composite VOs are also unsupported. Reference-type null on this path would
        // bypass the throw if the write path checked null first. Validate-first prevents
        // that silent-write path.
        var vo = NestedCompositeVo.Create(null!);
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 5 — DateOnly interior
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<DateOnlyVo>))]
    public sealed class DateOnlyVo : BoundaryVo
    {
        public DateOnly Date { get; private set; }
        private DateOnlyVo() { }
        private DateOnlyVo(DateOnly d) => Date = d;
        public static DateOnlyVo Create(DateOnly d) => new(d);
        public static Result<DateOnlyVo> TryCreate(DateOnly date, string? fieldName = null) =>
            Result.Ok(new DateOnlyVo(date));
    }

    [Fact]
    public void DateOnly_interior_throws_on_serialize()
    {
        var vo = DateOnlyVo.Create(new DateOnly(2026, 1, 15));
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void DateOnly_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<DateOnlyVo>("{\"date\":\"2026-01-15\"}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 6 — TimeOnly interior
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<TimeOnlyVo>))]
    public sealed class TimeOnlyVo : BoundaryVo
    {
        public TimeOnly Time { get; private set; }
        private TimeOnlyVo() { }
        private TimeOnlyVo(TimeOnly t) => Time = t;
        public static TimeOnlyVo Create(TimeOnly t) => new(t);
        public static Result<TimeOnlyVo> TryCreate(TimeOnly time, string? fieldName = null) =>
            Result.Ok(new TimeOnlyVo(time));
    }

    [Fact]
    public void TimeOnly_interior_throws_on_serialize()
    {
        var vo = TimeOnlyVo.Create(new TimeOnly(10, 30));
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void TimeOnly_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<TimeOnlyVo>("{\"time\":\"10:30:00\"}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 7 — uint interior
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<UintVo>))]
    public sealed class UintVo : BoundaryVo
    {
        public uint Count { get; private set; }
        private UintVo() { }
        private UintVo(uint c) => Count = c;
        public static UintVo Create(uint c) => new(c);
        public static Result<UintVo> TryCreate(uint count, string? fieldName = null) =>
            Result.Ok(new UintVo(count));
    }

    [Fact]
    public void Uint_interior_throws_on_serialize()
    {
        var vo = UintVo.Create(42u);
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Uint_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<UintVo>("{\"count\":42}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 8 — string[] interior
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<StringArrayVo>))]
    public sealed class StringArrayVo : BoundaryVo
    {
        public string[] Tags { get; private set; } = [];
        private StringArrayVo() { }
        private StringArrayVo(string[] t) => Tags = t;
        public static StringArrayVo Create(string[] t) => new(t);
        public static Result<StringArrayVo> TryCreate(string[] tags, string? fieldName = null) =>
            Result.Ok(new StringArrayVo(tags));
    }

    [Fact]
    public void String_array_interior_throws_on_serialize()
    {
        var vo = StringArrayVo.Create(["a", "b"]);
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void String_array_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<StringArrayVo>("{\"tags\":[\"a\",\"b\"]}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 9 — RequiredEnum<TSelf> interior (should be SUPPORTED;
    // RequiredEnum implements IScalarValue<TSelf, string> so primitive = string)
    // ---------------------------------------------------------------------

    public sealed partial class TestStatus : RequiredEnum<TestStatus>
    {
        public static readonly TestStatus Active = new();
        public static readonly TestStatus Archived = new();
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<RequiredEnumVo>))]
    public sealed class RequiredEnumVo : BoundaryVo
    {
        public TestStatus Status { get; private set; } = null!;
        private RequiredEnumVo() { }
        private RequiredEnumVo(TestStatus s) => Status = s;
        public static RequiredEnumVo Create(TestStatus s) => new(s);
        public static Result<RequiredEnumVo> TryCreate(string status, string? fieldName = null) =>
            TestStatus.TryCreate(status).Map(s => new RequiredEnumVo(s));
    }

    [Fact]
    public void RequiredEnum_interior_round_trips_successfully()
    {
        // Confirms the expectation correction from the rubber-duck critique: RequiredEnum<T>
        // is IScalarValue<T, string>, so the composite converter sees `string` as the primitive
        // and round-trips cleanly. The (mistaken) original P1-Test-1 hypothesis was that
        // RequiredEnum<TColor> as a composite-VO interior would break.
        var active = RequiredEnumVo.Create(TestStatus.Active);
        var archived = RequiredEnumVo.Create(TestStatus.Archived);

        var activeJson = JsonSerializer.Serialize(active);
        var archivedJson = JsonSerializer.Serialize(archived);
        activeJson.Should().Be("{\"status\":\"Active\"}");
        archivedJson.Should().Be("{\"status\":\"Archived\"}");

        var rtActive = JsonSerializer.Deserialize<RequiredEnumVo>(activeJson);
        var rtArchived = JsonSerializer.Deserialize<RequiredEnumVo>(archivedJson);
        rtActive.Should().NotBeNull();
        rtArchived.Should().NotBeNull();
        rtActive!.Status.Should().Be(TestStatus.Active);
        rtArchived!.Status.Should().Be(TestStatus.Archived);
    }

    // ---------------------------------------------------------------------
    // Shape 10 — Nested composite VO interior
    // (Outer VO whose property is itself a CompositeValueObjectJsonConverter-attributed VO)
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<InnerCompositeVo>))]
    public sealed class InnerCompositeVo : BoundaryVo
    {
        public int Number { get; private set; }
        public string Label { get; private set; } = string.Empty;
        private InnerCompositeVo() { }
        private InnerCompositeVo(int n, string l) { Number = n; Label = l; }
        public static InnerCompositeVo Create(int n, string l) => new(n, l);
        public static Result<InnerCompositeVo> TryCreate(int number, string label, string? fieldName = null) =>
            Result.Ok(new InnerCompositeVo(number, label));
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<NestedCompositeVo>))]
    public sealed class NestedCompositeVo : BoundaryVo
    {
        public InnerCompositeVo Inner { get; private set; } = null!;
        private NestedCompositeVo() { }
        private NestedCompositeVo(InnerCompositeVo i) => Inner = i;
        public static NestedCompositeVo Create(InnerCompositeVo i) => new(i);
        public static Result<NestedCompositeVo> TryCreate(InnerCompositeVo inner, string? fieldName = null) =>
            Result.Ok(new NestedCompositeVo(inner));
    }

    [Fact]
    public void Nested_composite_VO_interior_throws_on_serialize()
    {
        var vo = NestedCompositeVo.Create(InnerCompositeVo.Create(1, "hi"));
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Nested_composite_VO_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<NestedCompositeVo>(
            "{\"inner\":{\"number\":1,\"label\":\"hi\"}}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Shape 11 — Maybe<TScalar> interior (Maybe wrapping a scalar VO)
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<MaybeScalarVoFixture>))]
    public sealed class MaybeScalarVoFixture : BoundaryVo
    {
        public Maybe<TestStatus> Status { get; private set; }
        private MaybeScalarVoFixture() { }
        private MaybeScalarVoFixture(Maybe<TestStatus> s) => Status = s;
        public static MaybeScalarVoFixture Create(Maybe<TestStatus> s) => new(s);
        public static Result<MaybeScalarVoFixture> TryCreate(Maybe<TestStatus> status, string? fieldName = null) =>
            Result.Ok(new MaybeScalarVoFixture(status));
    }

    [Fact]
    public void Maybe_ScalarVO_interior_throws_on_serialize()
    {
        var vo = MaybeScalarVoFixture.Create(Maybe<TestStatus>.From(TestStatus.Active));
        Action act = () => JsonSerializer.Serialize(vo);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    [Fact]
    public void Maybe_ScalarVO_interior_throws_on_deserialize()
    {
        Action act = () => JsonSerializer.Deserialize<MaybeScalarVoFixture>("{\"status\":\"Active\"}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*nsupported primitive*");
    }

    // ---------------------------------------------------------------------
    // Known asymmetry — nullable scalar VO interior is write/read-asymmetric.
    // Documented as a closed limitation in Recipe 13; fix requires propagating
    // TryCreate parameter nullability through the converter's metadata (separate
    // follow-up). The test below pins the current behavior so a future fix is
    // visible (the test must be updated when the asymmetry is closed).
    // ---------------------------------------------------------------------

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<NullableScalarBoundaryVo>))]
    public sealed class NullableScalarBoundaryVo : BoundaryVo
    {
        public string Label { get; private set; } = string.Empty;
        public TestStatus? Optional { get; private set; }
        private NullableScalarBoundaryVo() { }
        private NullableScalarBoundaryVo(string l, TestStatus? o) { Label = l; Optional = o; }
        public static NullableScalarBoundaryVo Create(string l, TestStatus? o) => new(l, o);
        public static Result<NullableScalarBoundaryVo> TryCreate(string label, string? optional, string? fieldName = null) =>
            optional is null
                ? Result.Ok(new NullableScalarBoundaryVo(label, null))
                : TestStatus.TryCreate(optional).Map(s => new NullableScalarBoundaryVo(label, s));
    }

    [Fact]
    public void Nullable_scalar_VO_interior_is_write_read_asymmetric_for_null_value()
    {
        // Known asymmetry on a SUPPORTED shape: writing a null `TestStatus? Optional`
        // produces JSON `"optional":null` via the scalar projection, but ReadPrimitive's
        // string branch unconditionally throws on `JsonTokenType.Null` (the "required
        // string" guard for properties like IntStringVo.Label). The converter cannot
        // distinguish "required scalar TryCreate(string)" from "nullable scalar
        // TryCreate(string?)" because TryCreate parameter nullability is not captured
        // in CompositeMetadata. Recipe 13 documents the trap; recommended workaround
        // is the wire-shape DTO pattern (Recipe 14). When/if the fix lands (propagating
        // NullabilityInfoContext through Build), this test should be updated to assert
        // the round-trip succeeds.
        var vo = NullableScalarBoundaryVo.Create("abc", null);

        var json = JsonSerializer.Serialize(vo);
        json.Should().Be("{\"label\":\"abc\",\"optional\":null}",
            "scalar projection produces JSON null for a null nullable-scalar-VO property");

        Action readBack = () => JsonSerializer.Deserialize<NullableScalarBoundaryVo>(json);
        readBack.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*must be a string*",
                "ReadPrimitive's string branch rejects JsonTokenType.Null unconditionally — this is the known asymmetry");
    }

    [Fact]
    public void Nullable_scalar_VO_interior_round_trips_when_value_is_present()
    {
        // The asymmetry only bites for null values. When Optional is set, the underlying
        // string flows through cleanly on both sides.
        var vo = NullableScalarBoundaryVo.Create("abc", TestStatus.Active);

        var json = JsonSerializer.Serialize(vo);
        json.Should().Be("{\"label\":\"abc\",\"optional\":\"Active\"}");

        var rt = JsonSerializer.Deserialize<NullableScalarBoundaryVo>(json);
        rt.Should().NotBeNull();
        rt!.Label.Should().Be("abc");
        rt.Optional.Should().Be(TestStatus.Active);
    }
}
