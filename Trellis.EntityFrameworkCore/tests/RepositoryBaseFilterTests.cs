namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Testing;

/// <summary>
/// Tests that <see cref="RepositoryBase{TAggregate, TId}.RemoveByIdAsync"/> respects EF Core
/// global query filters (soft-delete, multi-tenant). Previously this method used
/// <c>DbSet.FindAsync</c> which bypasses filters — a security defect that allowed cross-tenant
/// or already-soft-deleted rows to be re-deleted.
/// </summary>
public sealed partial class RepositoryBaseFilterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FilterTestDbContext _context;
    private readonly SoftDeletableRepository _repository;

    public RepositoryBaseFilterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<FilterTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new FilterTestDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new SoftDeletableRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// A row excluded by the global query filter (here: soft-deleted) must NOT be findable
    /// for deletion via <c>RemoveByIdAsync</c>. The defect path returned Success and re-staged
    /// a delete on an already-filtered row.
    /// </summary>
    [Fact]
    public async Task RemoveByIdAsync_does_not_find_soft_deleted_row_then_returns_NotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SoftDeletableId.Create(Guid.NewGuid());
        var entity = SoftDeletable.Create(id, "filtered-row");

        // Seed and soft-delete (bypassing the filter via IgnoreQueryFilters so we can set the flag).
        _context.Items.Add(entity);
        await _context.SaveChangesAsync(ct);
        entity.IsDeleted = true;
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var result = await _repository.RemoveByIdAsync(id, ct);

        result.Should().BeFailure();
        result.UnwrapError().Should().BeOfType<Error.NotFound>();
    }

    /// <summary>
    /// A row excluded by a tenant filter belongs to a different tenant and must not be
    /// findable for deletion via <c>RemoveByIdAsync</c> — the multi-tenant isolation case
    /// the original FindAsync-based implementation violated.
    /// </summary>
    [Fact]
    public async Task RemoveByIdAsync_does_not_find_row_excluded_by_tenant_filter_then_returns_NotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var foreignTenantId = Guid.NewGuid();
        var id = SoftDeletableId.Create(Guid.NewGuid());
        var foreignRow = new SoftDeletable(id)
        {
            Name = "owned-by-other-tenant",
            TenantId = foreignTenantId,
            IsDeleted = false,
        };

        _context.Items.Add(foreignRow);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // The repository operates as the local tenant (FilterTestDbContext.CurrentTenantId).
        var result = await _repository.RemoveByIdAsync(id, ct);

        result.Should().BeFailure();
        result.UnwrapError().Should().BeOfType<Error.NotFound>();
    }

    /// <summary>
    /// Sanity: the happy path (row visible under the filter) still stages deletion.
    /// </summary>
    [Fact]
    public async Task RemoveByIdAsync_finds_visible_row_and_stages_deletion()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = SoftDeletableId.Create(Guid.NewGuid());
        var entity = SoftDeletable.Create(id, "visible-row");

        _context.Items.Add(entity);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var result = await _repository.RemoveByIdAsync(id, ct);

        result.Should().BeSuccess();
        var entry = _context.ChangeTracker.Entries<SoftDeletable>()
            .SingleOrDefault(e => e.Entity.Id == id);
        entry.Should().NotBeNull();
        entry!.State.Should().Be(EntityState.Deleted);
    }

    internal partial class SoftDeletableId : RequiredGuid<SoftDeletableId>;

    internal class SoftDeletable : Aggregate<SoftDeletableId>
    {
        public string Name { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
        public Guid TenantId { get; set; }

        private SoftDeletable() : base(default!) { }

        public SoftDeletable(SoftDeletableId id) : base(id) { }

        public static SoftDeletable Create(SoftDeletableId id, string name) =>
            new(id) { Name = name, IsDeleted = false, TenantId = FilterTestDbContext.LocalTenantId };
    }

    internal class FilterTestDbContext : DbContext
    {
        public static readonly Guid LocalTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public DbSet<SoftDeletable> Items => Set<SoftDeletable>();

        public FilterTestDbContext(DbContextOptions<FilterTestDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(SoftDeletableId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<SoftDeletable>(b =>
            {
                b.HasKey(i => i.Id);
                b.Property(i => i.Name).HasMaxLength(200).IsRequired();
                b.Property(i => i.IsDeleted).IsRequired();
                b.Property(i => i.TenantId).IsRequired();
                b.HasQueryFilter(x => !x.IsDeleted && x.TenantId == LocalTenantId);
            });
    }

    internal class SoftDeletableRepository(DbContext context)
        : RepositoryBase<SoftDeletable, SoftDeletableId>(context);
}