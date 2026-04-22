using Trellis.Testing;

namespace Trellis.Primitives.Tests;

using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="CompositeValueObjectJsonConverter{T}"/> covering all primitive read/write
/// branches, error paths, IScalarValue projection, and TryCreate discovery.
/// </summary>
public class CompositeValueObjectJsonConverterTests
{
    #region Roundtrip — primitive types

    [Fact]
    public void Roundtrip_int_and_string_produces_camelCase_payload()
    {
        var vo = IntStringVo.Create(42, "hello");

        var json = JsonSerializer.Serialize(vo);
        json.Should().Be("{\"number\":42,\"label\":\"hello\"}");

        var roundTripped = JsonSerializer.Deserialize<IntStringVo>(json);
        roundTripped.Should().NotBeNull();
        roundTripped!.Number.Should().Be(42);
        roundTripped.Label.Should().Be("hello");
    }

    [Fact]
    public void Roundtrip_all_supported_primitive_types()
    {
        var dt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var dto = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.FromHours(2));
        var guid = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var vo = AllPrimitivesVo.Create(
            i: -1, l: 9_000_000_000L, s: (short)-300, by: (byte)200,
            d: 1.5d, f: 2.5f, b: true, g: guid, dt: dt, dto: dto, dec: 1.23m);

        var json = JsonSerializer.Serialize(vo);
        var rt = JsonSerializer.Deserialize<AllPrimitivesVo>(json);

        rt.Should().NotBeNull();
        rt!.I.Should().Be(-1);
        rt.L.Should().Be(9_000_000_000L);
        rt.S.Should().Be((short)-300);
        rt.By.Should().Be((byte)200);
        rt.D.Should().Be(1.5d);
        rt.F.Should().Be(2.5f);
        rt.B.Should().BeTrue();
        rt.G.Should().Be(guid);
        rt.Dt.Should().Be(dt);
        rt.Dto.Should().Be(dto);
        rt.Dec.Should().Be(1.23m);
    }

    [Fact]
    public void Roundtrip_via_real_Money_with_IScalarValue_currency_property()
    {
        var money = Money.Create(99.99m, "USD");

        var json = JsonSerializer.Serialize(money);
        json.Should().Contain("\"amount\":99.99").And.Contain("\"currency\":\"USD\"");

        var rt = JsonSerializer.Deserialize<Money>(json);
        rt.Should().NotBeNull();
        rt!.Amount.Should().Be(99.99m);
        rt.Currency.Value.Should().Be("USD");
    }

    #endregion

    #region Read — error paths

    [Fact]
    public void Read_throws_when_JSON_is_not_an_object()
    {
        Action act = () => JsonSerializer.Deserialize<IntStringVo>("[1,2,3]");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("Expected JSON object*");
    }

    [Fact]
    public void Read_throws_when_required_property_is_missing()
    {
        Action act = () => JsonSerializer.Deserialize<IntStringVo>("{\"number\":1}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*'label' is missing*");
    }

    [Fact]
    public void Read_skips_unknown_properties()
    {
        var rt = JsonSerializer.Deserialize<IntStringVo>(
            "{\"unknown\":[1,{\"x\":2}],\"number\":7,\"extra\":\"y\",\"label\":\"ok\"}");
        rt.Should().NotBeNull();
        rt!.Number.Should().Be(7);
        rt.Label.Should().Be("ok");
    }

    [Fact]
    public void Read_is_case_insensitive_on_property_names()
    {
        var rt = JsonSerializer.Deserialize<IntStringVo>("{\"NUMBER\":3,\"Label\":\"x\"}");
        rt.Should().NotBeNull();
        rt!.Number.Should().Be(3);
    }

    [Fact]
    public void Read_throws_when_int_property_receives_string()
    {
        Action act = () => JsonSerializer.Deserialize<IntStringVo>("{\"number\":\"oops\",\"label\":\"x\"}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*'number' must be an integer*");
    }

    [Fact]
    public void Read_throws_when_string_property_receives_null()
    {
        Action act = () => JsonSerializer.Deserialize<IntStringVo>("{\"number\":1,\"label\":null}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*'label' must be a string*");
    }

    [Fact]
    public void Read_throws_when_guid_value_is_malformed()
    {
        var json = "{\"i\":0,\"l\":0,\"s\":0,\"by\":0,\"d\":0,\"f\":0,\"b\":false," +
                   "\"g\":\"not-a-guid\",\"dt\":\"2024-01-01T00:00:00Z\",\"dto\":\"2024-01-01T00:00:00Z\",\"dec\":0}";
        Action act = () => JsonSerializer.Deserialize<AllPrimitivesVo>(json);
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*'g' is not a valid GUID*");
    }

    [Fact]
    public void Read_throws_TrellisJsonValidationException_when_TryCreate_returns_failure()
    {
        Action act = () => JsonSerializer.Deserialize<ValidatedVo>("{\"number\":-5}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*must be non-negative*");
    }

    #endregion

    #region Write — null and unsupported

    [Fact]
    public void Write_throws_for_null_writer()
    {
        var converter = new CompositeValueObjectJsonConverter<IntStringVo>();
        var vo = IntStringVo.Create(1, "x");

        Action nullWriter = () => converter.Write(null!, vo, new JsonSerializerOptions());
        nullWriter.Should().Throw<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void Write_throws_for_null_value()
    {
        var converter = new CompositeValueObjectJsonConverter<IntStringVo>();
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        Action nullValue = () => converter.Write(writer, null!, new JsonSerializerOptions());
        nullValue.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [Fact]
    public void Write_emits_null_when_nullable_scalar_VO_property_is_null()
    {
        // Optional is a nullable scalar VO (CurrencyCode? — a reference scalar value object).
        // BuildScalarValueAccess emits a null-check that returns null when the VO instance is null,
        // exercising the conditional null-projection branch.
        var vo = NullableScalarVo.Create("abc", null);
        var json = JsonSerializer.Serialize(vo);
        json.Should().Be("{\"label\":\"abc\",\"optional\":null}");
    }

    [Fact]
    public void Write_projects_underlying_string_when_nullable_scalar_VO_property_is_set()
    {
        var vo = NullableScalarVo.Create("abc", CurrencyCode.TryCreate("USD").Unwrap());
        var json = JsonSerializer.Serialize(vo);
        json.Should().Be("{\"label\":\"abc\",\"optional\":\"USD\"}");
    }

    [Fact]
    public void Unsupported_primitive_in_TryCreate_signature_throws_on_first_use()
    {
        // UnsupportedPrimitiveVo has a Uri property — Uri is neither a primitive nor an IScalarValue.
        // Build() succeeds but Write/Read of the primitive will throw.
        Action act = () => JsonSerializer.Serialize(UnsupportedPrimitiveVo.Create(new Uri("https://example.com")));
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("Unsupported primitive type*");
    }

    [Fact]
    public void Read_throws_for_unsupported_primitive_when_JSON_contains_property()
    {
        Action act = () => JsonSerializer.Deserialize<UnsupportedPrimitiveVo>("{\"link\":\"https://example.com\"}");
        act.Should().Throw<TrellisJsonValidationException>()
            .WithMessage("*unsupported primitive*");
    }

    #endregion

    #region TryCreate discovery

    [Fact]
    public void Throws_when_no_matching_TryCreate_exists()
    {
        Action act = () => JsonSerializer.Serialize(new MissingTryCreateVo());
        // Static field init wraps the InvalidOperationException in TypeInitializationException.
        act.Should().Throw<TypeInitializationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*requires a public static 'TryCreate'*");
    }

    [Fact]
    public void Accepts_TryCreate_with_trailing_optional_parameters()
    {
        // OptionalParamsVo has TryCreate(int, string fieldName = null). Deserializing should
        // exercise the trailing-optional branch in BuildInvoker.
        var rt = JsonSerializer.Deserialize<OptionalParamsVo>("{\"value\":7}");
        rt.Should().NotBeNull();
        rt!.Value.Should().Be(7);
    }

    [Fact]
    public void Throws_when_TryCreate_overloads_are_ambiguous()
    {
        Action act = () => JsonSerializer.Serialize(new AmbiguousTryCreateVo(1));
        act.Should().Throw<TypeInitializationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*found multiple ambiguous 'TryCreate' overloads*");
    }

    #endregion

    // --- Test fixtures ---

    public abstract class TestVo : ValueObject
    {
        protected override IEnumerable<IComparable?> GetEqualityComponents() => Array.Empty<IComparable?>();
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<IntStringVo>))]
    public class IntStringVo : TestVo
    {
        public int Number { get; private set; }

        public string Label { get; private set; } = string.Empty;

        private IntStringVo() { }

        private IntStringVo(int n, string l) { Number = n; Label = l; }

        public static IntStringVo Create(int n, string l) => new(n, l);

        public static Result<IntStringVo> TryCreate(int number, string label, string? fieldName = null) =>
            Result.Ok(new IntStringVo(number, label));
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<AllPrimitivesVo>))]
    public class AllPrimitivesVo : TestVo
    {
        public int I { get; private set; }

        public long L { get; private set; }

        public short S { get; private set; }

        public byte By { get; private set; }

        public double D { get; private set; }

        public float F { get; private set; }

        public bool B { get; private set; }

        public Guid G { get; private set; }

        public DateTime Dt { get; private set; }

        public DateTimeOffset Dto { get; private set; }

        public decimal Dec { get; private set; }

        private AllPrimitivesVo() { }

        public static AllPrimitivesVo Create(
            int i, long l, short s, byte by,
            double d, float f, bool b, Guid g,
            DateTime dt, DateTimeOffset dto, decimal dec) =>
            new()
            {
                I = i, L = l, S = s, By = by,
                D = d, F = f, B = b, G = g,
                Dt = dt, Dto = dto, Dec = dec,
            };

        public static Result<AllPrimitivesVo> TryCreate(
            int i, long l, short s, byte by,
            double d, float f, bool b, Guid g,
            DateTime dt, DateTimeOffset dto, decimal dec) =>
            Result.Ok(Create(i, l, s, by, d, f, b, g, dt, dto, dec));
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<ValidatedVo>))]
    public class ValidatedVo : TestVo
    {
        public int Number { get; private set; }

        private ValidatedVo() { }

        private ValidatedVo(int n) => Number = n;

        public static Result<ValidatedVo> TryCreate(int number, string? fieldName = null) =>
            number < 0
                ? Result.Fail<ValidatedVo>(new Error.UnprocessableContent(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty("number"), "validation.error") { Detail = "must be non-negative." })))
                : Result.Ok(new ValidatedVo(number));
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<NullableScalarVo>))]
    public class NullableScalarVo : TestVo
    {
        public string Label { get; private set; } = string.Empty;

        public CurrencyCode? Optional { get; private set; }

        private NullableScalarVo() { }

        private NullableScalarVo(string l, CurrencyCode? o) { Label = l; Optional = o; }

        public static NullableScalarVo Create(string l, CurrencyCode? o) => new(l, o);

        public static Result<NullableScalarVo> TryCreate(string label, string? optional, string? fieldName = null) =>
            optional is null
                ? Result.Ok(new NullableScalarVo(label, null))
                : CurrencyCode.TryCreate(optional).Map(c => new NullableScalarVo(label, c));
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<UnsupportedPrimitiveVo>))]
    public class UnsupportedPrimitiveVo : TestVo
    {
        public Uri Link { get; private set; } = null!;

        private UnsupportedPrimitiveVo() { }

        private UnsupportedPrimitiveVo(Uri u) => Link = u;

        public static UnsupportedPrimitiveVo Create(Uri u) => new(u);

        public static Result<UnsupportedPrimitiveVo> TryCreate(Uri link, string? fieldName = null) =>
            Result.Ok(new UnsupportedPrimitiveVo(link));
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<MissingTryCreateVo>))]
    public class MissingTryCreateVo : TestVo
    {
        public int Value { get; private set; } = 1;
        // No TryCreate — Build should throw.
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<OptionalParamsVo>))]
    public class OptionalParamsVo : TestVo
    {
        public int Value { get; private set; }

        private OptionalParamsVo() { }

        private OptionalParamsVo(int v) => Value = v;

        public static Result<OptionalParamsVo> TryCreate(int value, string? fieldName = null) =>
            Result.Ok(new OptionalParamsVo(value));
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<AmbiguousTryCreateVo>))]
    public class AmbiguousTryCreateVo : TestVo
    {
        public int Value { get; private set; }

        public AmbiguousTryCreateVo() { }

        public AmbiguousTryCreateVo(int v) => Value = v;

        public static Result<AmbiguousTryCreateVo> TryCreate(int value, string? fieldName = null) =>
            Result.Ok(new AmbiguousTryCreateVo(value));

        public static Result<AmbiguousTryCreateVo> TryCreate(int value, int? extra = null) =>
            Result.Ok(new AmbiguousTryCreateVo(value));
    }
}
