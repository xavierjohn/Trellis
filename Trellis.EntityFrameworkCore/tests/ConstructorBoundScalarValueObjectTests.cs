namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Mapping of a constructor-bound (get-only) scalar value object property on an aggregate.
/// EF Core discovers a settable value-object property via its setter, but a get-only property bound
/// only through the constructor must still be discovered and converted. Each test maps the same
/// <see cref="Widget"/> aggregate — whose non-key <see cref="ScopeId"/> is constructor-bound —
/// through a different Trellis convention-registration path.
/// </summary>
public sealed class ConstructorBoundScalarValueObjectTests
{
    [Fact]
    public Task GeneratedPath_maps_ctor_bound_scalar_value_object() =>
        AssertWidgetRoundTripsAsync(c => new GeneratedPathContext(c), TestContext.Current.CancellationToken);

    [Fact]
    public Task RuntimeScan_maps_ctor_bound_scalar_value_object() =>
        AssertWidgetRoundTripsAsync(c => new RuntimeScanContext(c), TestContext.Current.CancellationToken);

    [Fact]
    public Task ScalarConverters_with_core_conventions_map_ctor_bound_scalar_value_object() =>
        AssertWidgetRoundTripsAsync(c => new ScalarConverterContext(c), TestContext.Current.CancellationToken);

    [Fact]
    public Task ExplicitHasConversion_maps_ctor_bound_scalar_value_object() =>
        AssertWidgetRoundTripsAsync(c => new WorkaroundContext(c), TestContext.Current.CancellationToken);

    // Control: a settable value-object property already maps (EF discovers it through the setter).
    [Fact]
    public async Task Settable_scalar_value_object_property_maps()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var context = new SettableScopeContext(connection);
        await context.Database.EnsureCreatedAsync(ct);

        var scope = ScopeId.NewUniqueV7();
        context.Set<SettableScopedRecord>().Add(new SettableScopedRecord { Id = 1, Scope = scope });
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        var loaded = await context.Set<SettableScopedRecord>().SingleAsync(ct);
        loaded.Scope.Should().Be(scope);
    }

    private static async Task AssertWidgetRoundTripsAsync<TContext>(
        Func<SqliteConnection, TContext> createContext, CancellationToken ct)
        where TContext : DbContext
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var context = createContext(connection);
        await context.Database.EnsureCreatedAsync(ct);

        var scope = ScopeId.NewUniqueV7();
        context.Set<Widget>().Add(Widget.Create(scope));
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        var loaded = await context.Set<Widget>().SingleAsync(ct);
        loaded.ScopeId.Should().Be(scope);
    }
}

// ── Fixtures ─────────────────────────────────────────────────────────────────

internal sealed partial class ScopeId : RequiredGuid<ScopeId>;

internal sealed partial class WidgetId : RequiredGuid<WidgetId>;

// Aggregate whose non-key ScopeId is bound only through the constructor (get-only property).
internal sealed class Widget : Aggregate<WidgetId>
{
    private Widget(WidgetId id, ScopeId scopeId) : base(id) => ScopeId = scopeId;

    public ScopeId ScopeId { get; }

    public static Widget Create(ScopeId scope) => new(WidgetId.NewUniqueV7(), scope);
}

internal sealed class SettableScopedRecord
{
    public int Id { get; set; }

    public ScopeId Scope { get; set; } = null!;
}

internal static class CtorBoundContextOptions
{
    public static DbContextOptions<TContext> For<TContext>(SqliteConnection connection)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .IgnoreManyServiceProvidersCreatedWarning()
            .Options;
}

// Generated path: the source-generated ApplyTrellisConventionsFor<TContext>().
internal sealed class GeneratedPathContext : DbContext
{
    public GeneratedPathContext(SqliteConnection connection)
        : base(CtorBoundContextOptions.For<GeneratedPathContext>(connection)) { }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.ApplyTrellisConventionsFor<GeneratedPathContext>();

    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.Entity<Widget>(e => { e.ToTable("widgets"); e.HasKey(w => w.Id); });
}

// Runtime reflection scan.
internal sealed class RuntimeScanContext : DbContext
{
    public RuntimeScanContext(SqliteConnection connection)
        : base(CtorBoundContextOptions.For<RuntimeScanContext>(connection)) { }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.ApplyTrellisConventions(typeof(RuntimeScanContext).Assembly);

    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.Entity<Widget>(e => { e.ToTable("widgets"); e.HasKey(w => w.Id); });
}

// Building-block path: explicit scalar converters + core conventions, WITHOUT the assembly scan.
// Proves the constructor-binding fix comes from the convention added by AddTrellisCoreConventions
// (AddTrellisScalarConverter alone only registers the converter).
internal sealed class ScalarConverterContext : DbContext
{
    public ScalarConverterContext(SqliteConnection connection)
        : base(CtorBoundContextOptions.For<ScalarConverterContext>(connection)) { }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.AddTrellisScalarConverter<WidgetId, Guid>()
            .AddTrellisScalarConverter<ScopeId, Guid>()
            .AddTrellisCoreConventions([]);

    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.Entity<Widget>(e => { e.ToTable("widgets"); e.HasKey(w => w.Id); });
}

// Settable control: a plain entity with a settable value-object property.
internal sealed class SettableScopeContext : DbContext
{
    public SettableScopeContext(SqliteConnection connection)
        : base(CtorBoundContextOptions.For<SettableScopeContext>(connection)) { }

    public DbSet<SettableScopedRecord> Records => Set<SettableScopedRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.ApplyTrellisConventions(typeof(SettableScopeContext).Assembly);

    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.Entity<SettableScopedRecord>(e => { e.ToTable("settable_records"); e.HasKey(r => r.Id); });
}

// Known-good workaround: explicit per-property HasConversion in OnModelCreating, registered WITHOUT
// ScalarValueObjectPropertyConvention — only the key's scalar converter is registered, not the full
// Trellis convention set. The explicit mapping below (not the convention) is therefore solely
// responsible for binding the get-only ScopeId: remove that line and the round-trip fails.
internal sealed class WorkaroundContext : DbContext
{
    public WorkaroundContext(SqliteConnection connection)
        : base(CtorBoundContextOptions.For<WorkaroundContext>(connection)) { }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.AddTrellisScalarConverter<WidgetId, Guid>();

    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.Entity<Widget>(e =>
        {
            e.ToTable("widgets");
            e.HasKey(w => w.Id);
            e.Property(w => w.ScopeId).HasConversion(v => v.Value, v => ScopeId.Create(v));
        });
}
