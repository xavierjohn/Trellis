namespace SampleDataAccess;

using Microsoft.EntityFrameworkCore;
using SampleUserLibrary;
using Trellis.EntityFrameworkCore;

/// <summary>
/// EF Core DbContext demonstrating Trellis integration:
/// - ApplyTrellisConventions for automatic value object converters
/// - AddTrellisInterceptors for ETag generation and timestamps
/// - Aggregate/Entity persistence with strongly-typed IDs
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(ProductId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureProduct(modelBuilder);
        ConfigureOrder(modelBuilder);
        ConfigureOrderLine(modelBuilder);
    }

    private static void ConfigureProduct(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Product>(builder =>
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
            builder.Property(p => p.Price).IsRequired();
            builder.Property(p => p.StockQuantity).IsRequired();
        });

    private static void ConfigureOrder(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>(builder =>
        {
            builder.HasKey(o => o.Id);
            builder.Property(o => o.CustomerId).IsRequired();
            builder.Property(o => o.State).IsRequired();

            // Ignore computed property
            builder.Ignore(o => o.Total);

            builder.HasMany(o => o.Lines)
                .WithOne()
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureOrderLine(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OrderLine>(builder =>
        {
            builder.HasKey(l => l.Id);
            builder.Property(l => l.OrderId).IsRequired();
            builder.Property(l => l.ProductId).IsRequired();
            builder.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
            builder.Property(l => l.UnitPrice).IsRequired();
            builder.Property(l => l.Quantity).IsRequired();

            // Ignore computed property
            builder.Ignore(l => l.LineTotal);
        });
}