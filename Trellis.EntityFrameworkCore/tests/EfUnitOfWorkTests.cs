namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Testing;
using static RepositoryBaseTests;

public class EfUnitOfWorkTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RepoTestDbContext _context;
    private readonly EfUnitOfWork<RepoTestDbContext> _unitOfWork;

    public EfUnitOfWorkTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RepoTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new RepoTestDbContext(options);
        _context.Database.EnsureCreated();
        _unitOfWork = new EfUnitOfWork<RepoTestDbContext>(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CommitAsync_persists_staged_additions()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("Committed"));
        _context.Items.Add(item);

        // Act
        var result = await _unitOfWork.CommitAsync(ct);

        // Assert
        result.Should().BeSuccess();
        _context.ChangeTracker.Clear();
        var found = await _context.Items.FindAsync([id], ct);
        found.Should().NotBeNull();
        found!.Name.Should().Be(TestItemName.Create("Committed"));
    }

    [Fact]
    public async Task CommitAsync_persists_staged_deletions()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("ToDelete"));
        _context.Items.Add(item);
        await _context.SaveChangesAsync(ct);

        _context.Items.Remove(item);

        // Act
        var result = await _unitOfWork.CommitAsync(ct);

        // Assert
        result.Should().BeSuccess();
        _context.ChangeTracker.Clear();
        var found = await _context.Items.FindAsync([id], ct);
        found.Should().BeNull();
    }

    [Fact]
    public async Task CommitAsync_persists_staged_modifications()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("Original"));
        _context.Items.Add(item);
        await _context.SaveChangesAsync(ct);

        item.Name = TestItemName.Create("Updated");

        // Act
        var result = await _unitOfWork.CommitAsync(ct);

        // Assert
        result.Should().BeSuccess();
        _context.ChangeTracker.Clear();
        var found = await _context.Items.FindAsync([id], ct);
        found!.Name.Should().Be(TestItemName.Create("Updated"));
    }

    [Fact]
    public async Task CommitAsync_with_no_changes_returns_success()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _unitOfWork.CommitAsync(ct);

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public async Task CommitAsync_duplicate_key_returns_conflict_error()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        _context.Items.Add(TestItem.Create(id, TestItemName.Create("First")));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Stage a duplicate by adding entity with same PK
        _context.Items.Add(TestItem.Create(id, TestItemName.Create("Duplicate")));

        // Act
        var result = await _unitOfWork.CommitAsync(ct);

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ConflictError>();
    }
}
