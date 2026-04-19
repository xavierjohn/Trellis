using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Tests for optimistic concurrency support: <see cref="AggregateETagConvention"/>
/// and <see cref="AggregateETagInterceptor"/>.
/// </summary>
public class AggregateETagTests : IDisposable
{
    private readonly ConcurrencyTestDbContext _context;
    private readonly SqliteConnection _connection;

    public AggregateETagTests() =>
        (_context, _connection) = ConcurrencyTestDbContext.CreateInMemory();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region AggregateETagConvention — concurrency token configuration

    [Fact]
    public void Convention_MarksETagAsConcurrencyToken()
    {
        var entityType = _context.Model.FindEntityType(typeof(TestAggregate))!;
        var etagProperty = entityType.FindProperty(nameof(IAggregate.ETag))!;
        etagProperty.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void Convention_DoesNotAffectNonAggregateEntities()
    {
        var entityType = _context.Model.FindEntityType(typeof(TestCustomer))!;
        var etagProperty = entityType.FindProperty(nameof(IAggregate.ETag));
        etagProperty.Should().BeNull();
    }

    #endregion

    #region AggregateETagInterceptor — auto-generation

    [Fact]
    public async Task Interceptor_NewAggregate_GeneratesETag()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-1", "Initial");
        aggregate.ETag.Should().BeEmpty("new aggregates start with empty ETag");

        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);

        aggregate.ETag.Should().NotBeNullOrEmpty("ETag should be generated on first save");
    }

    [Fact]
    public async Task Interceptor_ModifiedAggregate_GeneratesNewETag()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-2", "Initial");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);
        var firstETag = aggregate.ETag;
        firstETag.Should().NotBeNullOrEmpty();

        aggregate.Rename("Updated");
        await _context.SaveChangesResultAsync(ct);

        aggregate.ETag.Should().NotBe(firstETag, "ETag should change on modification");
    }

    [Fact]
    public async Task Interceptor_MultipleModifications_GeneratesUniqueETags()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-3", "V0");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);
        var etag0 = aggregate.ETag;

        aggregate.Rename("V1");
        await _context.SaveChangesResultAsync(ct);
        var etag1 = aggregate.ETag;

        aggregate.Rename("V2");
        await _context.SaveChangesResultAsync(ct);
        var etag2 = aggregate.ETag;

        new[] { etag0, etag1, etag2 }.Should().OnlyHaveUniqueItems("each save should produce a unique ETag");
    }

    [Fact]
    public async Task Interceptor_UnmodifiedAggregate_ETagStaysSame()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-4", "Stable");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);
        var savedETag = aggregate.ETag;

        // No-op save — no modifications
        await _context.SaveChangesResultAsync(ct);

        aggregate.ETag.Should().Be(savedETag, "ETag should not change without modification");
    }

    [Fact]
    public async Task Interceptor_AcceptAllChangesOnSuccessFalse_SupportsSubsequentSaves()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("agg-5", "Initial");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);
        var initialETag = aggregate.ETag;

        aggregate.Rename("Updated");
        var result1 = await _context.SaveChangesResultAsync(acceptAllChangesOnSuccess: false, ct);
        result1.IsSuccess.Should().BeTrue("first save should succeed");
        var firstSaveETag = aggregate.ETag;
        firstSaveETag.Should().NotBe(initialETag);

        aggregate.Rename("Updated again");
        var result2 = await _context.SaveChangesResultAsync(ct);
        result2.IsSuccess.Should().BeTrue("second save should succeed — OriginalValue was synced by SavedChanges hook");
        aggregate.ETag.Should().NotBe(firstSaveETag, "ETag should change again");
    }

    #endregion

    #region Child entity changes promote aggregate ETag

    [Fact]
    public async Task Interceptor_ChildEntityAdded_AggregateETagChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("child-1", "Parent");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);
        var savedETag = aggregate.ETag;

        aggregate.AddChild("Child A");
        var result = await _context.SaveChangesResultAsync(ct);

        result.IsSuccess.Should().BeTrue();
        aggregate.ETag.Should().NotBe(savedETag, "ETag should change when child entities are added");
    }

    #endregion

    #region End-to-end concurrency conflict

    [Fact]
    public async Task ConcurrencyConflict_SecondSave_ReturnsConflictError()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("conflict-1", "Original");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultAsync(ct);

        var (context2, disposable2) = ConcurrencyTestDbContext.CreateFromConnection(_connection);
        using (disposable2)
        {
            var loaded = await context2.TestAggregates.FirstAsync(a => a.Id == "conflict-1", ct);
            loaded.Rename("Modified by other process");
            await context2.SaveChangesResultAsync(ct);
        }

        aggregate.Rename("Modified by original process");
        var result = await _context.SaveChangesResultAsync(ct);

        result.IsSuccess.Should().BeFalse();
        result.UnwrapError().Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task ConcurrencyConflict_SaveChangesResultUnitAsync_ReturnsConflictError()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregate = TestAggregate.Create("conflict-2", "Original");
        _context.TestAggregates.Add(aggregate);
        await _context.SaveChangesResultUnitAsync(ct);

        var (context2, disposable2) = ConcurrencyTestDbContext.CreateFromConnection(_connection);
        using (disposable2)
        {
            var loaded = await context2.TestAggregates.FirstAsync(a => a.Id == "conflict-2", ct);
            loaded.Rename("Modified by other process");
            await context2.SaveChangesResultUnitAsync(ct);
        }

        aggregate.Rename("Modified by original process");
        var result = await _context.SaveChangesResultUnitAsync(ct);

        result.IsSuccess.Should().BeFalse();
        result.UnwrapError().Should().BeOfType<ConflictError>();
    }

    #endregion
}

#region Test Aggregate

internal class TestAggregate : Aggregate<string>
{
    public string Name { get; private set; }
    private readonly List<TestChildEntity> _children = [];
    public IReadOnlyList<TestChildEntity> Children => _children.AsReadOnly();

    private TestAggregate(string id, string name) : base(id) => Name = name;

    private TestAggregate() : base(default!) => Name = null!;

    public static TestAggregate Create(string id, string name) => new(id, name);

    public void Rename(string name) => Name = name;

    public void AddChild(string childName) =>
        _children.Add(new TestChildEntity { Id = Guid.NewGuid().ToString(), Name = childName });
}

internal class TestChildEntity
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string TestAggregateId { get; set; } = null!;
}

#endregion

#region Test DbContext with interceptors

internal class ConcurrencyTestDbContext : DbContext
{
    public DbSet<TestAggregate> TestAggregates => Set<TestAggregate>();
    public DbSet<TestCustomer> Customers => Set<TestCustomer>();
    public DbSet<TestChildEntity> TestChildEntities => Set<TestChildEntity>();

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
            b.HasMany(a => a.Children).WithOne().HasForeignKey(c => c.TestAggregateId);
        });

        modelBuilder.Entity<TestChildEntity>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
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
