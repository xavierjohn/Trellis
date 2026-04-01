namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis;

/// <summary>
/// Tests for <see cref="EntityTimestampInterceptor"/>.
/// </summary>
public class EntityTimestampInterceptorTests : IDisposable
{
    private readonly TimestampTestDbContext _context;
    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _timeProvider;

    public EntityTimestampInterceptorTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero));
        (_context, _connection) = TimestampTestDbContext.CreateInMemory(_timeProvider);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task NewEntity_GetsCreatedAtAndLastModifiedSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new TimestampTestEntity { Id = "e-1", Name = "Test" };
        entity.CreatedAt.Should().Be(default(DateTimeOffset));
        entity.LastModified.Should().Be(default(DateTimeOffset));

        _context.TimestampEntities.Add(entity);
        await _context.SaveChangesAsync(ct);

        entity.CreatedAt.Should().Be(_timeProvider.GetUtcNow());
        entity.LastModified.Should().Be(_timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task ModifiedEntity_OnlyLastModifiedUpdated_CreatedAtPreserved()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new TimestampTestEntity { Id = "e-2", Name = "Original" };
        _context.TimestampEntities.Add(entity);
        await _context.SaveChangesAsync(ct);
        var originalCreatedAt = entity.CreatedAt;
        var firstLastModified = entity.LastModified;

        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));
        entity.Name = "Updated";
        await _context.SaveChangesAsync(ct);

        entity.CreatedAt.Should().Be(originalCreatedAt, "CreatedAt must not change on modification");
        entity.LastModified.Should().Be(_timeProvider.GetUtcNow());
        entity.LastModified.Should().NotBe(firstLastModified);
    }

    [Fact]
    public async Task NonEntity_NotAffected()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new NonEntityPoco { Id = "n-1", Name = "Test" };

        _context.NonEntityPocos.Add(entity);
        await _context.SaveChangesAsync(ct);

        var loaded = await _context.NonEntityPocos.FindAsync([entity.Id], ct);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task FakeTimeProvider_DeterministicTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var entity = new TimestampTestEntity { Id = "e-3", Name = "Deterministic" };

        _context.TimestampEntities.Add(entity);
        await _context.SaveChangesAsync(ct);

        entity.CreatedAt.Should().Be(expected, "interceptor should use the injected TimeProvider");
        entity.LastModified.Should().Be(expected, "interceptor should use the injected TimeProvider");
    }

    [Fact]
    public async Task CreatedAtAndLastModified_AreEqual_OnFirstSave()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new TimestampTestEntity { Id = "e-4", Name = "New" };

        _context.TimestampEntities.Add(entity);
        await _context.SaveChangesAsync(ct);

        entity.CreatedAt.Should().Be(entity.LastModified);
    }
}

#region Test entities

internal class TimestampTestEntity : Entity<string>, IEntity
{
    public string Name { get; set; } = null!;

    public TimestampTestEntity() : base(string.Empty) { }
}

internal class NonEntityPoco
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

#endregion

#region Test DbContext

internal class TimestampTestDbContext : DbContext
{
    public DbSet<TimestampTestEntity> TimestampEntities => Set<TimestampTestEntity>();
    public DbSet<NonEntityPoco> NonEntityPocos => Set<NonEntityPoco>();

    public TimestampTestDbContext(DbContextOptions<TimestampTestDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TimestampTestEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).HasMaxLength(100).IsRequired();
            b.Property(e => e.CreatedAt).IsRequired();
            b.Property(e => e.LastModified).IsRequired();
        });

        modelBuilder.Entity<NonEntityPoco>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).HasMaxLength(100).IsRequired();
        });
    }

    public static (TimestampTestDbContext Context, SqliteConnection Connection) CreateInMemory(
        TimeProvider timeProvider)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var interceptor = new EntityTimestampInterceptor(timeProvider);

        var options = new DbContextOptionsBuilder<TimestampTestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        var context = new TimestampTestDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }
}

#endregion

#region FakeTimeProvider

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

#endregion
