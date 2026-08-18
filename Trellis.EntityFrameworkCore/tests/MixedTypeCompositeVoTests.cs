namespace Trellis.EntityFrameworkCore.Tests;

using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;
using Trellis.Testing;
using Xunit;

/// <summary>
/// Exploration suite for mixed-type composite value objects — a composite VO that mixes
/// a Trellis scalar VO (<see cref="TestOrderStatus"/>) with C#-native types wrapped in
/// <see cref="Maybe{T}"/> (<c>Maybe&lt;int&gt;</c>, <c>Maybe&lt;string&gt;</c>,
/// <c>Maybe&lt;DateTime[]&gt;</c>).
///
/// The framework today proves <c>string Name + Maybe&lt;PhoneNumber&gt; Phone</c> works
/// (see <c>CompositeValueObjectConventionTests.MaybeScalarInsideRequiredCompositeVo_*</c>).
/// These tests extend that coverage to the broader user shape captured in BACKLOG P1-Test-1
/// and document per-field what is supported and what is not, for both EF Core round-trip
/// and JSON round-trip through <c>CompositeValueObjectJsonConverter&lt;T&gt;</c>.
/// </summary>
public class MixedTypeCompositeVoTests : IDisposable
{
    private MixedTypeVoDbContext? _context;
    private SqliteConnection? _connection;

    private MixedTypeVoDbContext Context
    {
        get
        {
            if (_context is not null)
                return _context;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<MixedTypeVoDbContext>()
                .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
                .Options;
            _context = new MixedTypeVoDbContext(options);
            _context.Database.EnsureCreated();
            return _context;
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }

    // -------- Probe 1: model builds ----------------------------------------------------

    [Fact]
    public void ModelBuilds_ApplyTrellisConventions_AcceptsAllPropertyShapes()
    {
        // Tells us whether ApplyTrellisConventions / MaybeConvention rejects any of the
        // fixture's property shapes (Maybe<int>, Maybe<string>, Maybe<DateTime[]>,
        // RequiredEnum<TestOrderStatus>). Throws ⇒ that shape is not supported on a
        // composite VO interior and the rest of the suite is moot.
        var act = () =>
        {
            using var ctx = Context;
            _ = ctx.Model;
        };

        act.Should().NotThrow();
    }

    // -------- Probe 2: EF Core round-trip ---------------------------------------------

    [Fact]
    public async Task EfRoundTrip_AllFieldsPopulated_RoundTripsCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        var snapshots = new[] { new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc) };

        Context.Entities.Add(new MixedTypeVoEntity
        {
            Id = 1,
            Mixed = TestMixedTypeVo.Create(
                TestOrderStatus.Confirmed,
                Maybe.From(42),
                Maybe.From("hello"),
                Maybe.From(snapshots)),
        });
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.Entities.FindAsync([1], ct);

        loaded.Should().NotBeNull();
        loaded!.Mixed.Status.Should().Be(TestOrderStatus.Confirmed);
        loaded.Mixed.Count.HasValue.Should().BeTrue("Maybe<int> round-trips through MaybeConvention");
        loaded.Mixed.Count.Value.Should().Be(42);
        loaded.Mixed.Label.HasValue.Should().BeTrue("Maybe<string> round-trips through MaybeConvention");
        loaded.Mixed.Label.Value.Should().Be("hello");
        loaded.Mixed.Snapshots.HasValue.Should().BeTrue("Maybe<DateTime[]> round-trips if the underlying provider supports arrays");
        loaded.Mixed.Snapshots.Value.Should().Equal(snapshots);
    }

    [Fact]
    public async Task EfRoundTrip_AllMaybesNone_RoundTripsAsNone()
    {
        var ct = TestContext.Current.CancellationToken;

        Context.Entities.Add(new MixedTypeVoEntity
        {
            Id = 2,
            Mixed = TestMixedTypeVo.Create(
                TestOrderStatus.Draft,
                Maybe<int>.None,
                Maybe<string>.None,
                Maybe<DateTime[]>.None),
        });
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.Entities.FindAsync([2], ct);

        loaded.Should().NotBeNull();
        loaded!.Mixed.Status.Should().Be(TestOrderStatus.Draft);
        loaded.Mixed.Count.Should().BeNone();
        loaded.Mixed.Label.Should().BeNone();
        loaded.Mixed.Snapshots.Should().BeNone();
    }

    // -------- Probe 3: JSON round-trip via CompositeValueObjectJsonConverter ----------
    //
    // Finding: TestMixedTypeVo declares `Maybe<int>`, `Maybe<string>`, and `Maybe<DateTime[]>`
    // properties. The CompositeValueObjectJsonConverter rejects all three at WRITE time
    // with `TrellisJsonValidationException: Unsupported primitive type 'Maybe<TPrimitive>'`,
    // regardless of whether the value is `Some` or `None`. Declaring any one such property
    // renders the entire composite VO non-serializable through this converter.
    //
    // This is fail-loud (good — caller sees an exception, not silent data corruption) but
    // there is no compile-time enforcement: the type compiles, EF Core round-trips it
    // cleanly (see Probe 2), and the JSON failure only manifests on first serialize. A
    // future TRLS rule could mirror TRLS020's DTO-side check on composite VO interiors.

    private static JsonSerializerOptions CompositeOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new CompositeValueObjectJsonConverter<TestMixedTypeVo>());
        return options;
    }

    [Fact]
    public void JsonSerialize_AnyMaybePropertyOnComposite_FailsAtFirstSuchProperty()
    {
        // Documented behavior: a composite VO that declares ANY `Maybe<T>` property fails
        // JSON serialization, regardless of value (Some or None). The serializer trips on
        // the first declared Maybe<> property (here `Count`, a `Maybe<int>`) and the rest
        // of the body is never written. We construct the VO with everything at None to
        // prove the failure is type-driven, not value-driven. The single assertion below
        // captures the rule; tests of the same failure on `Maybe<string>` / `Maybe<DateTime[]>`
        // in isolation would only repeat this — the converter's allowed list is identical for
        // every position.
        var vo = TestMixedTypeVo.Create(
            TestOrderStatus.Draft,
            Maybe<int>.None,
            Maybe<string>.None,
            Maybe<DateTime[]>.None);

        var act = () => JsonSerializer.Serialize(vo, CompositeOptions());

        var ex = act.Should().Throw<TrellisJsonValidationException>(
                "ANY Maybe<T> property on a composite VO trips CompositeValueObjectJsonConverter")
            .Which;
        ex.Message.Should().Contain("Unsupported primitive type");
        ex.Message.Should().Contain("Maybe");
        // Trips on the first declared Maybe<> property (Count → Maybe<int>).
        ex.Message.Should().Contain("'count'");
    }

    [Fact]
    public void JsonDeserialize_SparseBody_AllMaybePropertiesAreRequiredOnWire()
    {
        // Distinct deserialize-side finding: even though Maybe<T> fields are conceptually
        // optional in the domain, the converter treats them as required JSON properties —
        // a body that omits them throws TrellisJsonValidationException with
        // "Required properties missing", NOT the "Unsupported primitive type" error from
        // the write path. So absence in the domain (Maybe<>.None) is NOT the same as
        // absence on the wire when the composite VO converter is in play.
        var act = () => JsonSerializer.Deserialize<TestMixedTypeVo>(
            """{"status":"Draft"}""", CompositeOptions());

        act.Should().Throw<TrellisJsonValidationException>()
            .And.Message.Should().Contain("Required properties missing");
    }

    [Fact]
    public void JsonDeserialize_BodyWithExplicitNulls_HitsUnsupportedPrimitiveType()
    {
        // Once all properties are present (even as nulls), the converter advances past
        // the required-property check and runs into the read-side Maybe<TPrimitive>
        // primitive-type rejection. Note: the read-side message wording differs from
        // the write-side wording.
        // - Write side: "Unsupported primitive type 'Maybe`1[System.Int32]' for JSON property 'count'."
        // - Read side:  "Composite value object 'TestMixedTypeVo' uses unsupported primitive 'Maybe`1' for property 'count'."
        // Both are TrellisJsonValidationException; assertion below uses the common
        // "unsupported primitive" substring (lowercase, present in both).
        const string body = """{"status":"Draft","count":null,"label":null,"snapshots":null}""";

        var act = () => JsonSerializer.Deserialize<TestMixedTypeVo>(body, CompositeOptions());

        var ex = act.Should().Throw<TrellisJsonValidationException>().Which;
        ex.Message.ToLowerInvariant().Should().Contain("unsupported primitive");
        ex.Message.Should().Contain("Maybe");
    }

    // -------- Probe 4: counterpart "supported mixed VO" round-trips cleanly -----------
    //
    // To answer the question "is a mixed VO possible at all?" — yes, when every field is
    // either a raw primitive from the converter's allowed list (string, decimal, int, long,
    // short, byte, double, float, bool, Guid, DateTime, DateTimeOffset) or a Trellis scalar
    // value object that flattens to one of those primitives (RequiredEnum, RequiredGuid,
    // RequiredString, PhoneNumber, EmailAddress, ...). The fixture below mixes both and
    // round-trips cleanly through both EF Core AND the composite JSON converter. Note:
    // any `Maybe<T>` property (including `Maybe<TScalarVO>`) is NOT supported by the
    // composite converter — the converter never delegates property serialization to STJ,
    // so factory-based scalar-Maybe handling does not engage here.

    private static JsonSerializerOptions SupportedMixedOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new CompositeValueObjectJsonConverter<TestSupportedMixedVo>());
        return options;
    }

    [Fact]
    public void SupportedMixed_JsonRoundTrip_AllFieldsPopulated_RoundTripsCleanly()
    {
        var original = TestSupportedMixedVo.Create(
            TestOrderStatus.Shipped,
            label: "alpha",
            count: 7,
            id: Guid.Parse("019e1942-1a4a-796c-856e-95f8996eacb3"),
            createdAt: new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero),
            score: 99.5m,
            isActive: true);

        var json = JsonSerializer.Serialize(original, SupportedMixedOptions());
        var loaded = JsonSerializer.Deserialize<TestSupportedMixedVo>(json, SupportedMixedOptions());

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(TestOrderStatus.Shipped);
        loaded.Label.Should().Be("alpha");
        loaded.Count.Should().Be(7);
        loaded.Id.Should().Be(original.Id);
        loaded.CreatedAt.Should().Be(original.CreatedAt);
        loaded.Score.Should().Be(99.5m);
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SupportedMixed_JsonRoundTrip_BoundaryValues_RoundTripCleanly()
    {
        // Empty string, zero numerics, Guid.Empty, default DateTimeOffset, false bool.
        var original = TestSupportedMixedVo.Create(
            TestOrderStatus.Draft,
            label: string.Empty,
            count: 0,
            id: Guid.Empty,
            createdAt: DateTimeOffset.MinValue,
            score: 0m,
            isActive: false);

        var json = JsonSerializer.Serialize(original, SupportedMixedOptions());
        var loaded = JsonSerializer.Deserialize<TestSupportedMixedVo>(json, SupportedMixedOptions());

        loaded.Should().NotBeNull();
        loaded.Should().Be(original);
    }

    [Fact]
    public async Task SupportedMixed_EfRoundTrip_AllFieldsPopulated_RoundTripsCleanly()
    {
        // Mirrors the JSON happy-path test: same shape and values, but exercised through
        // the EF Core persistence path. Proves the supported-mixed fixture works
        // end-to-end across both wire and storage seams, matching the cookbook's
        // "this is the canonical mixed VO shape" claim.
        var ct = TestContext.Current.CancellationToken;
        var original = TestSupportedMixedVo.Create(
            TestOrderStatus.Shipped,
            label: "alpha",
            count: 7,
            id: Guid.Parse("019e1942-1a4a-796c-856e-95f8996eacb3"),
            createdAt: new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero),
            score: 99.5m,
            isActive: true);

        Context.SupportedEntities.Add(new SupportedMixedVoEntity { Id = 10, Mixed = original });
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.SupportedEntities.FindAsync([10], ct);

        loaded.Should().NotBeNull();
        loaded!.Mixed.Status.Should().Be(TestOrderStatus.Shipped);
        loaded.Mixed.Label.Should().Be("alpha");
        loaded.Mixed.Count.Should().Be(7);
        loaded.Mixed.Id.Should().Be(original.Id);
        loaded.Mixed.CreatedAt.Should().Be(original.CreatedAt);
        loaded.Mixed.Score.Should().Be(99.5m);
        loaded.Mixed.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SupportedMixed_EfRoundTrip_BoundaryValues_RoundTripCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = TestSupportedMixedVo.Create(
            TestOrderStatus.Draft,
            label: string.Empty,
            count: 0,
            id: Guid.Empty,
            createdAt: DateTimeOffset.MinValue,
            score: 0m,
            isActive: false);

        Context.SupportedEntities.Add(new SupportedMixedVoEntity { Id = 11, Mixed = original });
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.SupportedEntities.FindAsync([11], ct);

        loaded.Should().NotBeNull();
        loaded!.Mixed.Should().Be(original);
    }

    // -------- Probe 5: user-written custom converter rescues the broken shape ---------
    //
    // Direct answer to "can the user work around the framework gap": yes. The user
    // writes a `JsonConverter<TestMixedTypeVo>` that handles each `Maybe<TPrimitive>`
    // field explicitly (null for None, inner value for Some) and bypasses the
    // framework's `CompositeValueObjectJsonConverter`. The VO itself stays unchanged.
    // See `Helpers/CustomMixedTypeVoJsonConverter.cs`.

    private static JsonSerializerOptions CustomConverterOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new CustomMixedTypeVoJsonConverter());
        return options;
    }

    [Fact]
    public void CustomConverter_AllFieldsPopulated_RoundTripsThroughJson()
    {
        var snapshots = new[]
        {
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
        };
        var original = TestMixedTypeVo.Create(
            TestOrderStatus.Confirmed,
            Maybe.From(42),
            Maybe.From("hello"),
            Maybe.From(snapshots));

        var json = JsonSerializer.Serialize(original, CustomConverterOptions());
        var loaded = JsonSerializer.Deserialize<TestMixedTypeVo>(json, CustomConverterOptions());

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(TestOrderStatus.Confirmed);
        loaded.Count.Should().HaveValueEqualTo(42);
        loaded.Label.Should().HaveValueEqualTo("hello");
        loaded.Snapshots.HasValue.Should().BeTrue();
        loaded.Snapshots.Value.Should().Equal(snapshots);
    }

    [Fact]
    public void CustomConverter_AllOptionalsNone_RoundTripsCleanly()
    {
        var original = TestMixedTypeVo.Create(
            TestOrderStatus.Draft,
            Maybe<int>.None,
            Maybe<string>.None,
            Maybe<DateTime[]>.None);

        var json = JsonSerializer.Serialize(original, CustomConverterOptions());
        var loaded = JsonSerializer.Deserialize<TestMixedTypeVo>(json, CustomConverterOptions());

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(TestOrderStatus.Draft);
        loaded.Count.Should().BeNone();
        loaded.Label.Should().BeNone();
        loaded.Snapshots.Should().BeNone();
    }

    [Fact]
    public void CustomConverter_MixedSomeAndNone_RoundTripsCleanly()
    {
        // Partial population: some Maybe<> fields Some, others None — the converter
        // must emit JSON null for None entries and parse them back as None on read.
        var original = TestMixedTypeVo.Create(
            TestOrderStatus.Confirmed,
            Maybe.From(7),
            Maybe<string>.None,
            Maybe.From(new[] { new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) }));

        var json = JsonSerializer.Serialize(original, CustomConverterOptions());

        // Wire-shape sanity check: None must be emitted as JSON null.
        json.Should().Contain("\"label\":null");
        json.Should().Contain("\"count\":7");

        var loaded = JsonSerializer.Deserialize<TestMixedTypeVo>(json, CustomConverterOptions());

        loaded.Should().NotBeNull();
        loaded!.Count.Should().HaveValueEqualTo(7);
        loaded.Label.Should().BeNone();
        loaded.Snapshots.HasValue.Should().BeTrue();
    }
}

// ----- DI scaffolding for the supported-mixed fixture ------------------------------

internal sealed class SupportedMixedVoEntity
{
    public int Id { get; set; }
    public TestSupportedMixedVo Mixed { get; set; } = null!;
}

// ----- DI scaffolding ---------------------------------------------------------------

internal sealed class MixedTypeVoEntity
{
    public int Id { get; set; }
    public TestMixedTypeVo Mixed { get; set; } = null!;
}

internal sealed class MixedTypeVoDbContext : DbContext
{
    public DbSet<MixedTypeVoEntity> Entities => Set<MixedTypeVoEntity>();
    public DbSet<SupportedMixedVoEntity> SupportedEntities => Set<SupportedMixedVoEntity>();

    public MixedTypeVoDbContext(DbContextOptions<MixedTypeVoDbContext> options) : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(TestMixedTypeVo).Assembly);
}