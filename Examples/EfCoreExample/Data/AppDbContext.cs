using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

namespace EfCoreExample.Data;

using EfCoreExample.Entities;
using EfCoreExample.Enums;
using EfCoreExample.ValueObjects;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core DbContext demonstrating seamless integration with Trellis value objects.
/// Uses source-generated Trellis conventions to register value converters for all
/// reachable Trellis types automatically.
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureCustomer(modelBuilder);
        ConfigureProduct(modelBuilder);
        ConfigureOrder(modelBuilder);
        ConfigureOrderLine(modelBuilder);
    }

    private static void ConfigureCustomer(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Customer>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).HasMaxLength(36).IsRequired();
            builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
            builder.Property(c => c.Email).HasMaxLength(254).IsRequired();
            builder.Property(c => c.CreatedAt).IsRequired();
        });

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
            builder.Property(o => o.Id).HasMaxLength(36).IsRequired();
            builder.Property(o => o.CustomerId).HasMaxLength(36).IsRequired();
            builder.Property(o => o.State).IsRequired();
            builder.Property(o => o.CreatedAt).IsRequired();

            // Ignore computed property
            builder.Ignore(o => o.Total);

            // Configure one-to-many relationship with OrderLines
            builder.HasMany(o => o.Lines)
                .WithOne()
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureOrderLine(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OrderLine>(builder =>
        {
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Id).ValueGeneratedOnAdd();
            builder.Property(l => l.OrderId).HasMaxLength(36).IsRequired();
            builder.Property(l => l.ProductId).IsRequired();
            builder.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
            builder.Property(l => l.UnitPrice).IsRequired();
            builder.Property(l => l.Quantity).IsRequired();

            // Ignore computed property
            builder.Ignore(l => l.LineTotal);
        });
}
