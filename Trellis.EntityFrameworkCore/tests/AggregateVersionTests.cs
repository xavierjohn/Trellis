namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Tests for optimistic concurrency support: <see cref="AggregateVersionConvention"/>
/// and <see cref="AggregateVersionInterceptor"/>.
/// </summary>
public class AggregateVersionTests : IDisposable
{
    private readonly ConcurrencyTestDbContext _context;
    private readonly SqliteConnection _connection;

    public AggregateVersionTests() =>
        (_context, _connection) = ConcurrencyTestDbContext.CreateInMemory();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region AggregateVersionConvention — concurrency token configuration

    [Fact]
    public void Convention_MarksVersionAsConcurrencyToken()
    {
        // Arrange
        var entityType = _context.Model.FindEntityType(typeof(TestAggregate))!;

        // Act
        var versionProperty = entityType.FindProperty(nameof(IAggregate.Version))!;

        // Assert
        versionProperty.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void Convention_DoesNotAffectNonAggregateEntities()
    {
        // Arrange — TestCustomer is not an aggregate
        var entityType = _context.Model.FindEntityType(typeof(TestCustomer))!;

        // Act
        var versionProperty = entityType.FindProperty(nameof(IAggregate.Version));

        // Assert — no Version property on non-aggregate
        versionProperty.Should().BeNull();
    }

    #endregion

    #region AggregateVersionInterceptor — auto-increment

    [Fact]
    public async Task Interceptor_NewAggregate_VersionIsZero()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-1", "Initial");
        _context.TestAggregates.Add(aggregate);

        // Act
        await _context.SaveChangesResultAsync(ct);

        // Assert
        aggregate.Version.Should().Be(0, "new aggregates start at version 0");
    }

    [Fact]
    public async Task Interceptor_ModifiedAggregate_VersionIncrements()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-2", "Initial");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);
        aggregate.Version.Should().Be(0);

        // Act — modify and save
        aggregate.Rename("Updated");
        await _context.SaveChangesResultAsync(ct);

        // Assert
        aggregate.Version.Should().Be(1, "version should increment on first modification");
    }

    [Fact]
    public async Task Interceptor_MultipleModifications_VersionIncrementsEachTime()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-3", "V0");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);

        // Act — modify multiple times
        aggregate.Rename("V1");
        await _context.SaveChangesResultAsync(ct);

        aggregate.Rename("V2");
        await _context.SaveChangesResultAsync(ct);

        aggregate.Rename("V3");
        await _context.SaveChangesResultAsync(ct);

        // Assert
        aggregate.Version.Should().Be(3);
    }

    [Fact]
    public async Task Interceptor_UnmodifiedAggregate_VersionStaysSame()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-4", "Stable");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);

        // Act — save without modification (no-op save)
        await _context.SaveChangesResultAsync(ct);

        // Assert
        aggregate.Version.Should().Be(0, "version should not change without modification");
    }

    [Fact]
    public async Task Interceptor_AcceptAllChangesOnSuccessFalse_DoesNotDoubleIncrement()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-5", "Initial");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);

        // Act — modify, save with acceptAllChangesOnSuccess: false
        aggregate.Rename("Updated");
        var result1 = await _context.SaveChangesResultAsync(acceptAllChangesOnSuccess: false, ct);
        result1.IsSuccess.Should().BeTrue("first save should succeed");
        aggregate.Version.Should().Be(1, "version should increment once");

        // Second save — the SavedChanges hook syncs OriginalValue so this works correctly.
        // The interceptor increments Version from 1 to 2, and EF generates WHERE Version = 1.
        aggregate.Rename("Updated again");
        var result2 = await _context.SaveChangesResultAsync(ct);
        result2.IsSuccess.Should().BeTrue("second save should succeed — OriginalValue was synced by SavedChanges hook");
        aggregate.Version.Should().Be(2, "version should increment again for the second modification");
    }

    #endregion

    #region End-to-end concurrency conflict

    [Fact]
    public async Task ConcurrencyConflict_SecondSave_ReturnsConflictError()
    {
        // Arrange — create and save an aggregate
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("conflict-1", "Original");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);

        // Simulate another process modifying the same aggregate
        var (context2, disposable2) = ConcurrencyTestDbContext.CreateFromConnection(_connection);
        using (disposable2)
        {
            var loaded = await context2.TestAggregates.FirstAsync(a => a.Id == "conflict-1", ct);
            loaded.Rename("Modified by other process");
            await context2.SaveChangesResultAsync(ct);
        }

        // Act — try to save from the original context (stale version)
        aggregate.Rename("Modified by original process");
        var result = await _context.SaveChangesResultAsync(ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task ConcurrencyConflict_SaveChangesResultUnitAsync_ReturnsConflictError()
    {
        // Arrange — create and save an aggregate
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("conflict-2", "Original");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultUnitAsync(ct);

        // Simulate another process modifying the same aggregate
        var (context2, disposable2) = ConcurrencyTestDbContext.CreateFromConnection(_connection);
        using (disposable2)
        {
            var loaded = await context2.TestAggregates.FirstAsync(a => a.Id == "conflict-2", ct);
            loaded.Rename("Modified by other process");
            await context2.SaveChangesResultUnitAsync(ct);
        }

        // Act — try to save from the original context (stale version)
        aggregate.Rename("Modified by original process");
        var result = await _context.SaveChangesResultUnitAsync(ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ConflictError>();
    }

    #endregion
}

#region Test Aggregate

/// <summary>
/// Minimal aggregate for concurrency tests.
/// </summary>
internal class TestAggregate : Aggregate<string>
{
    public string Name { get; private set; }

    private TestAggregate(string id, string name) : base(id) => Name = name;

    private TestAggregate() : base(default!) => Name = null!; // EF Core materialization

    public static TestAggregate Create(string id, string name) => new(id, name);

    public void Rename(string name) => Name = name;
}

#endregion

#region Test DbContext with interceptors

/// <summary>
/// DbContext that includes <see cref="AggregateVersionInterceptor"/> for concurrency tests.
/// </summary>
internal class ConcurrencyTestDbContext : DbContext
{
    public DbSet<TestAggregate> TestAggregates => Set<TestAggregate>();
    public DbSet<TestCustomer> Customers => Set<TestCustomer>();

    public ConcurrencyTestDbContext(DbContextOptions<ConcurrencyTestDbContext> options) : base(options)
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

        modelBuilder.Entity<TestCustomer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
            b.Property(c => c.Email).HasMaxLength(254).IsRequired();
            b.Property(c => c.CreatedAt).IsRequired();
        });
    }

    public static (ConcurrencyTestDbContext Context, SqliteConnection Connection) CreateInMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ConcurrencyTestDbContext>()
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .Options;

        var context = new ConcurrencyTestDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }

    /// <summary>
    /// Creates a second context sharing the same SQLite connection (for concurrency conflict tests).
    /// </summary>
    public static (ConcurrencyTestDbContext Context, IDisposable Noop) CreateFromConnection(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ConcurrencyTestDbContext>()
            .UseSqlite(connection)
            .AddTrellisInterceptors()
            .Options;

        var context = new ConcurrencyTestDbContext(options);
        return (context, context);
    }
}

#endregion
