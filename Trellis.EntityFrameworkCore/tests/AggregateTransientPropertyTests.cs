namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Tests for <see cref="AggregateTransientPropertyConvention"/>.
/// Validates that transient base-class properties (e.g., <c>IsChanged</c>) are
/// automatically excluded from the EF Core model for aggregate entity types.
/// </summary>
public class AggregateTransientPropertyTests : IDisposable
{
    private readonly TransientPropertyTestDbContext _context;
    private readonly SqliteConnection _connection;

    public AggregateTransientPropertyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TransientPropertyTestDbContext>()
            .UseSqlite(_connection)
            .AddTrellisInterceptors()
            .Options;

        _context = new TransientPropertyTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Convention — IsChanged is excluded from model

    [Fact]
    public void Convention_AggregateType_IsChangedPropertyNotMapped()
    {
        var entityType = _context.Model.FindEntityType(typeof(TestAggregate))!;
        var isChangedProperty = entityType.FindProperty(nameof(IAggregate.IsChanged));

        isChangedProperty.Should().BeNull(
            "IsChanged is a transient property and should be auto-ignored by AggregateTransientPropertyConvention");
    }

    [Fact]
    public void Convention_NonAggregateType_IsNotAffected()
    {
        var entityType = _context.Model.FindEntityType(typeof(TestCustomer))!;

        // TestCustomer is not an aggregate — convention should not touch it.
        // Verify it still has its expected properties.
        entityType.FindProperty(nameof(TestCustomer.Name)).Should().NotBeNull();
        entityType.FindProperty(nameof(TestCustomer.Email)).Should().NotBeNull();
    }

    [Fact]
    public void Convention_AggregateType_OtherPropertiesStillMapped()
    {
        var entityType = _context.Model.FindEntityType(typeof(TestAggregate))!;

        entityType.FindProperty(nameof(TestAggregate.Name)).Should().NotBeNull(
            "domain properties should still be mapped");
        entityType.FindProperty(nameof(IAggregate.ETag)).Should().NotBeNull(
            "ETag should still be mapped (handled by AggregateETagConvention)");
    }

    [Fact]
    public void Convention_DerivedAggregateWithHiddenSettableIsChanged_StillIgnored()
    {
        var entityType = _context.Model.FindEntityType(typeof(HiddenSettableIsChangedAggregate))!;
        var isChangedProperty = entityType.FindProperty(nameof(IAggregate.IsChanged));

        isChangedProperty.Should().BeNull(
            "even when a derived aggregate hides IsChanged with a settable property via 'new', the convention should ignore it");
    }

    #endregion

    #region Round-trip — aggregate persists without IsChanged column

    [Fact]
    public async Task RoundTrip_AggregateWithIsChanged_SavesAndLoadsCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("transient-1", "Test");

        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.TestAggregates.FindAsync(["transient-1"], ct);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Test");
        loaded.IsChanged.Should().BeFalse("freshly loaded aggregate has no uncommitted events");
    }

    [Fact]
    public async Task RoundTrip_HiddenSettableIsChangedAggregate_SavesAndLoadsCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = HiddenSettableIsChangedAggregate.Create("settable-1", "Test");

        _context.HiddenSettableAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.HiddenSettableAggregates.FindAsync(["settable-1"], ct);

        loaded.Should().NotBeNull();
        loaded!.Title.Should().Be("Test");
    }

    #endregion
}

#region Test types

/// <summary>
/// Aggregate that hides the inherited <c>IsChanged</c> with a settable property via <c>new</c>.
/// Without the convention, EF Core would convention-map this as a database column.
/// </summary>
internal class HiddenSettableIsChangedAggregate : Aggregate<string>
{
    public string Title { get; private set; } = null!;

    public new bool IsChanged { get; set; }

    private HiddenSettableIsChangedAggregate() : base(default!) { }

    public static HiddenSettableIsChangedAggregate Create(string id, string title) =>
        new(id, title);

    private HiddenSettableIsChangedAggregate(string id, string title) : base(id) =>
        Title = title;
}

#endregion

#region Test DbContext

internal class TransientPropertyTestDbContext : DbContext
{
    public DbSet<TestAggregate> TestAggregates => Set<TestAggregate>();
    public DbSet<TestCustomer> Customers => Set<TestCustomer>();
    public DbSet<HiddenSettableIsChangedAggregate> HiddenSettableAggregates => Set<HiddenSettableIsChangedAggregate>();

    public TransientPropertyTestDbContext(DbContextOptions<TransientPropertyTestDbContext> options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestAggregate>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<HiddenSettableIsChangedAggregate>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Title).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<TestCustomer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
            b.Property(c => c.Email).HasMaxLength(254).IsRequired();
            b.Property(c => c.CreatedAt).IsRequired();
        });
    }
}

#endregion