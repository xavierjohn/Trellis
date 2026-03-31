namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis;

/// <summary>
/// Tests for <see cref="LastModifiedInterceptor"/>.
/// </summary>
public class LastModifiedInterceptorTests : IDisposable
{
    private readonly LastModifiedTestDbContext _context;
    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _timeProvider;

    public LastModifiedInterceptorTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero));
        (_context, _connection) = LastModifiedTestDbContext.CreateInMemory(_timeProvider);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task NewEntity_GetsLastModifiedSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new TrackableEntity { Id = "e-1", Name = "Test" };
        entity.LastModified.Should().Be(default(DateTimeOffset));

        _context.TrackableEntities.Add(entity);
        await _context.SaveChangesAsync(ct);

        entity.LastModified.Should().Be(_timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task ModifiedEntity_GetsLastModifiedUpdated()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new TrackableEntity { Id = "e-2", Name = "Original" };
        _context.TrackableEntities.Add(entity);
        await _context.SaveChangesAsync(ct);
        var firstTimestamp = entity.LastModified;

        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));
        entity.Name = "Updated";
        await _context.SaveChangesAsync(ct);

        entity.LastModified.Should().Be(_timeProvider.GetUtcNow());
        entity.LastModified.Should().NotBe(firstTimestamp);
    }

    [Fact]
    public async Task NonTrackableEntity_NotAffected()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new NonTrackableEntity { Id = "n-1", Name = "Test" };

        _context.NonTrackableEntities.Add(entity);
        await _context.SaveChangesAsync(ct);

        var loaded = await _context.NonTrackableEntities.FindAsync([entity.Id], ct);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task FakeTimeProvider_DeterministicTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var entity = new TrackableEntity { Id = "e-3", Name = "Deterministic" };

        _context.TrackableEntities.Add(entity);
        await _context.SaveChangesAsync(ct);

        entity.LastModified.Should().Be(expected, "interceptor should use the injected TimeProvider");
    }
}

#region Test entities

internal class TrackableEntity : ITrackLastModified
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTimeOffset LastModified { get; set; }
}

internal class NonTrackableEntity
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

#endregion

#region Test DbContext

internal class LastModifiedTestDbContext : DbContext
{
    public DbSet<TrackableEntity> TrackableEntities => Set<TrackableEntity>();
    public DbSet<NonTrackableEntity> NonTrackableEntities => Set<NonTrackableEntity>();

    public LastModifiedTestDbContext(DbContextOptions<LastModifiedTestDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackableEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).HasMaxLength(100).IsRequired();
            b.Property(e => e.LastModified).IsRequired();
        });

        modelBuilder.Entity<NonTrackableEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).HasMaxLength(100).IsRequired();
        });
    }

    public static (LastModifiedTestDbContext Context, SqliteConnection Connection) CreateInMemory(
        TimeProvider timeProvider)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var interceptor = new LastModifiedInterceptor(timeProvider);

        var options = new DbContextOptionsBuilder<LastModifiedTestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        var context = new LastModifiedTestDbContext(options);
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
