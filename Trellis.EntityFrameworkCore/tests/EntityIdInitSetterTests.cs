namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Validates that Entity<T>.Id init setter works with EF Core materialization

/// <summary>
/// Tests that EF Core can correctly materialize <see cref="Entity{TId}"/> subclasses
/// that use the <c>init</c> setter on the <see cref="Entity{TId}.Id"/> property.
/// </summary>
public class EntityIdInitSetterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EntityTestDbContext _context;

    public EntityIdInitSetterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<EntityTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new EntityTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task EntityId_InitSetter_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var entity = new SimpleEntity(id, "Test Name");

        // Act
        _context.SimpleEntities.Add(entity);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.SimpleEntities.FindAsync([id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(id);
        loaded.Name.Should().Be("Test Name");
    }

    [Fact]
    public async Task EntityId_InitSetter_QueryByIdWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _context.SimpleEntities.Add(new SimpleEntity(id, "Query Target"));
        _context.SimpleEntities.Add(new SimpleEntity(Guid.NewGuid(), "Other Entity"));
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        // Act
        var loaded = await _context.SimpleEntities
            .SingleAsync(e => e.Id == id, ct);

        // Assert
        loaded.Id.Should().Be(id);
        loaded.Name.Should().Be("Query Target");
    }
}

/// <summary>
/// Minimal Entity subclass for testing EF Core materialization with init setter.
/// </summary>
internal class SimpleEntity : Entity<Guid>
{
    public string Name { get; init; }

    public SimpleEntity(Guid id, string name) : base(id) =>
        Name = name;
}

/// <summary>
/// Minimal DbContext for Entity init setter tests.
/// </summary>
internal class EntityTestDbContext(DbContextOptions<EntityTestDbContext> options)
    : DbContext(options)
{
    public DbSet<SimpleEntity> SimpleEntities => Set<SimpleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SimpleEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        });
}