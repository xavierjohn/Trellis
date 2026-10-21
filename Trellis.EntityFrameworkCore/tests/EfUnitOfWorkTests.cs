using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
            .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
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
    public void MarkUncommittable_NullReason_Throws() =>
        FluentActions.Invoking(() => _unitOfWork.MarkUncommittable(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("reason");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MarkUncommittable_AnyScopeDepth_PreservesFirstReasonAndNeverWrites(int depth)
    {
        var ct = TestContext.Current.CancellationToken;
        var item = TestItem.Create(TestItemId.Create(Guid.NewGuid()), TestItemName.Create("Must not commit"));
        _context.Items.Add(item);
        var rejection = new Error.Conflict("audit.rejected");

        using (var owner = depth > 0 ? _unitOfWork.BeginScope() : null)
        using (var nested = depth > 1 ? _unitOfWork.BeginScope() : null)
        {
            _unitOfWork.MarkUncommittable(rejection);
            _unitOfWork.MarkUncommittable(new Error.Unexpected(FaultCodes.UnitOfWorkUncommittable));
            (await _unitOfWork.CommitAsync(ct)).Should().BeFailure().Which.Should().BeSameAs(rejection);
        }

        (await _unitOfWork.CommitAsync(ct)).Should().BeFailure().Which.Should().BeSameAs(rejection);
        using (_unitOfWork.BeginScope())
            (await _unitOfWork.CommitAsync(ct)).Should().BeFailure().Which.Should().BeSameAs(rejection);
        _context.Entry(item).State.Should().Be(EntityState.Added);
        (await _context.Items.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task MarkUncommittable_FreshContextAndUnitOfWork_AreNotRejected()
    {
        _unitOfWork.MarkUncommittable(new Error.Conflict("audit.rejected"));
        var options = new DbContextOptionsBuilder<RepoTestDbContext>()
            .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning().Options;
        await using var freshContext = new RepoTestDbContext(options);
        var freshUnitOfWork = new EfUnitOfWork<RepoTestDbContext>(freshContext);
        freshContext.Items.Add(TestItem.Create(TestItemId.Create(Guid.NewGuid()), TestItemName.Create("Fresh unit")));

        (await freshUnitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();

        (await _unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().HaveErrorCode("audit.rejected");
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
        result.UnwrapError().Should().BeOfType<Error.Conflict>();
    }
}