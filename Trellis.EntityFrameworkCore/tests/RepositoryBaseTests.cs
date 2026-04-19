using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Trellis.EntityFrameworkCore.Tests.Helpers;

public partial class RepositoryBaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RepoTestDbContext _context;
    private readonly TestItemRepository _repository;

    public RepositoryBaseTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RepoTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new RepoTestDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new TestItemRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region FindByIdAsync

    [Fact]
    public async Task FindByIdAsync_existing_returns_maybe_with_value()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("Test"));
        _context.Items.Add(item);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _repository.FindByIdAsync(id, ct);

        // Assert
        result.Should().HaveValue();
        result.Value.Id.Should().Be(id);
    }

    [Fact]
    public async Task FindByIdAsync_nonexistent_returns_none()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());

        // Act
        var result = await _repository.FindByIdAsync(id, ct);

        // Assert
        result.Should().BeNone();
    }

    #endregion

    #region QueryAsync

    [Fact]
    public async Task QueryAsync_with_matching_specification_returns_matches()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id1 = TestItemId.Create(Guid.NewGuid());
        var id2 = TestItemId.Create(Guid.NewGuid());
        _context.Items.Add(TestItem.Create(id1, TestItemName.Create("Alpha")));
        _context.Items.Add(TestItem.Create(id2, TestItemName.Create("Beta")));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var spec = new TestItemNameSpec(TestItemName.Create("Alpha"));

        // Act
        var results = await _repository.QueryAsync(spec, ct);

        // Assert
        results.Should().ContainSingle();
        results[0].Id.Should().Be(id1);
    }

    [Fact]
    public async Task QueryAsync_no_matches_returns_empty_list()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var spec = new TestItemNameSpec(TestItemName.Create("NonExistent"));

        // Act
        var results = await _repository.QueryAsync(spec, ct);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_null_specification_throws()
    {
        // Act
        var act = () => _repository.QueryAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("specification");
    }

    #endregion

    #region ExistsAsync (by ID)

    [Fact]
    public async Task ExistsAsync_byId_existing_returns_true()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        _context.Items.Add(TestItem.Create(id, TestItemName.Create("Present")));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var exists = await _repository.ExistsAsync(id, ct);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_byId_nonexistent_returns_false()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());

        // Act
        var exists = await _repository.ExistsAsync(id, ct);

        // Assert
        exists.Should().BeFalse();
    }

    #endregion

    #region ExistsAsync (by Specification)

    [Fact]
    public async Task ExistsAsync_bySpec_matching_returns_true()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Items.Add(TestItem.Create(TestItemId.Create(Guid.NewGuid()), TestItemName.Create("Findable")));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var spec = new TestItemNameSpec(TestItemName.Create("Findable"));

        // Act
        var exists = await _repository.ExistsAsync(spec, ct);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_bySpec_no_match_returns_false()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var spec = new TestItemNameSpec(TestItemName.Create("Missing"));

        // Act
        var exists = await _repository.ExistsAsync(spec, ct);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_bySpec_null_specification_throws()
    {
        // Act
        var act = () => _repository.ExistsAsync((Specification<TestItem>)null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("specification");
    }

    #endregion

    #region CountAsync

    [Fact]
    public async Task CountAsync_returns_matching_count()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Items.Add(TestItem.Create(TestItemId.Create(Guid.NewGuid()), TestItemName.Create("Alpha")));
        _context.Items.Add(TestItem.Create(TestItemId.Create(Guid.NewGuid()), TestItemName.Create("Alpha")));
        _context.Items.Add(TestItem.Create(TestItemId.Create(Guid.NewGuid()), TestItemName.Create("Beta")));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var spec = new TestItemNameSpec(TestItemName.Create("Alpha"));

        // Act
        var count = await _repository.CountAsync(spec, ct);

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task CountAsync_no_matches_returns_zero()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var spec = new TestItemNameSpec(TestItemName.Create("Ghost"));

        // Act
        var count = await _repository.CountAsync(spec, ct);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task CountAsync_null_specification_throws()
    {
        // Act
        var act = () => _repository.CountAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("specification");
    }

    #endregion

    #region Add

    [Fact]
    public async Task Add_detached_aggregate_stages_insert()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("New Item"));

        // Act
        _repository.Add(item);

        // Assert — staged but not yet persisted
        _context.Entry(item).State.Should().Be(EntityState.Added);

        // Commit manually to verify persistence
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
        var found = await _context.Items.FindAsync([id], ct);
        found.Should().NotBeNull();
        found!.Name.Should().Be(TestItemName.Create("New Item"));
    }

    [Fact]
    public async Task Add_tracked_aggregate_is_noop()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("Tracked"));
        _context.Items.Add(item);
        await _context.SaveChangesAsync(ct);

        item.Name = TestItemName.Create("Modified");
        var stateBefore = _context.Entry(item).State;

        // Act
        _repository.Add(item);

        // Assert — state remains Modified, not re-Added
        _context.Entry(item).State.Should().Be(stateBefore);
    }

    [Fact]
    public void Add_null_aggregate_throws()
    {
        // Act
        var act = () => _repository.Add(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("aggregate");
    }

    #endregion

    #region Remove

    [Fact]
    public async Task Remove_tracked_aggregate_stages_deletion()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("ToRemove"));
        _context.Items.Add(item);
        await _context.SaveChangesAsync(ct);

        // Act
        _repository.Remove(item);

        // Assert — staged for deletion
        _context.Entry(item).State.Should().Be(EntityState.Deleted);

        // Commit manually to verify persistence
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
        var found = await _context.Items.FindAsync([id], ct);
        found.Should().BeNull();
    }

    [Fact]
    public void Remove_null_aggregate_throws()
    {
        // Act
        var act = () => _repository.Remove(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("aggregate");
    }

    #endregion

    #region RemoveByIdAsync

    [Fact]
    public async Task RemoveByIdAsync_existing_stages_deletion_and_returns_success()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("ToDelete"));
        _context.Items.Add(item);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _repository.RemoveByIdAsync(id, ct);

        // Assert — staged, not committed
        result.Should().BeSuccess();

        // Verify it's in change tracker as Deleted
        var entry = _context.ChangeTracker.Entries<TestItem>().SingleOrDefault(e => e.Entity.Id == id);
        entry.Should().NotBeNull();
        entry!.State.Should().Be(EntityState.Deleted);

        // Commit manually to verify persistence
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
        var found = await _context.Items.FindAsync([id], ct);
        found.Should().BeNull();
    }

    [Fact]
    public async Task RemoveByIdAsync_nonexistent_returns_not_found()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());

        // Act
        var result = await _repository.RemoveByIdAsync(id, ct);

        // Assert
        result.Should().BeFailure();
        result.UnwrapError().Should().BeOfType<Error.NotFound>();
    }

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_null_context_throws()
    {
        // Act
        var act = () => new TestItemRepository(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    #endregion

    #region Test Infrastructure

    internal partial class TestItemId : RequiredGuid<TestItemId>;

    [StringLength(200)]
    internal partial class TestItemName : RequiredString<TestItemName>;

    internal class TestItem : Aggregate<TestItemId>
    {
        public TestItemName Name { get; set; } = null!;

        private TestItem() : base(default!) { }

        public static TestItem Create(TestItemId id, TestItemName name) =>
            new() { Id = id, Name = name };
    }

    internal class RepoTestDbContext : DbContext
    {
        public DbSet<TestItem> Items => Set<TestItem>();

        public RepoTestDbContext(DbContextOptions<RepoTestDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestItemId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TestItem>(b =>
            {
                b.HasKey(i => i.Id);
                b.Property(i => i.Name).HasMaxLength(200).IsRequired();
            });
    }

    internal class TestItemRepository(DbContext context) : RepositoryBase<TestItem, TestItemId>(context);

    internal class TestItemNameSpec(TestItemName name) : Specification<TestItem>
    {
        public override Expression<Func<TestItem, bool>> ToExpression() =>
            item => item.Name == name;
    }

    #endregion
}
