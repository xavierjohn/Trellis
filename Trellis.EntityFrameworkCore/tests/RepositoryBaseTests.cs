namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Testing;

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

    #region SaveAsync

    [Fact]
    public async Task SaveAsync_new_aggregate_inserts_and_returns_success()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("New Item"));

        // Act
        var result = await _repository.SaveAsync(item, ct);

        // Assert
        result.Should().BeSuccess();

        _context.ChangeTracker.Clear();
        var found = await _context.Items.FindAsync([id], ct);
        found.Should().NotBeNull();
        found!.Name.Should().Be(TestItemName.Create("New Item"));
    }

    [Fact]
    public async Task SaveAsync_tracked_aggregate_updates_and_returns_success()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("Original"));
        _context.Items.Add(item);
        await _context.SaveChangesAsync(ct);

        item.Name = TestItemName.Create("Updated");

        // Act
        var result = await _repository.SaveAsync(item, ct);

        // Assert
        result.Should().BeSuccess();

        _context.ChangeTracker.Clear();
        var found = await _context.Items.FindAsync([id], ct);
        found!.Name.Should().Be(TestItemName.Create("Updated"));
    }

    [Fact]
    public async Task SaveAsync_null_aggregate_throws()
    {
        // Act
        var act = () => _repository.SaveAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("aggregate");
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
