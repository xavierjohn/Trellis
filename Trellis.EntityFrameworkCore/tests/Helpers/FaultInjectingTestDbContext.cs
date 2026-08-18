namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Standalone DbContext mirroring <see cref="TestDbContext"/> that lets tests inject an
/// override for <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>. When
/// <see cref="OnSaveChanges"/> returns <see langword="null"/> the base implementation runs.
/// This is the seam for simulating concurrency exceptions, unknown
/// <see cref="DbUpdateException"/> shapes, and arbitrary non-database exceptions without
/// depending on a particular provider.
/// </summary>
internal sealed class FaultInjectingTestDbContext : DbContext
{
    public DbSet<TestCustomer> Customers => Set<TestCustomer>();
    public DbSet<TestOrder> Orders => Set<TestOrder>();

    public Func<int, Exception?>? OnSaveChanges { get; set; }

    private int _saveCallCount;

    public FaultInjectingTestDbContext(DbContextOptions<FaultInjectingTestDbContext> options)
        : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestCustomer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
            b.Property(c => c.Email).HasMaxLength(254).IsRequired();
            b.HasIndex(c => c.Email).IsUnique();
            b.Property(c => c.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<TestOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId);
            b.Property(o => o.Amount).IsRequired();
            b.Property(o => o.Status).IsRequired();
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _saveCallCount++;
        var ex = OnSaveChanges?.Invoke(_saveCallCount);
        if (ex is not null)
            throw ex;
        return base.SaveChangesAsync(cancellationToken);
    }

    public static (FaultInjectingTestDbContext Context, SqliteConnection Connection) CreateInMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<FaultInjectingTestDbContext>()
            .UseSqlite(connection)
            .IgnoreManyServiceProvidersCreatedWarning()
            .Options;

        var context = new FaultInjectingTestDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }
}