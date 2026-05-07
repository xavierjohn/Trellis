using ConditionalRequestExample.Domain;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore;

namespace ConditionalRequestExample.Data;

public class ProductDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventionsFor<ProductDbContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(builder =>
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
            builder.Property(p => p.Price).IsRequired();
        });
    }
}
