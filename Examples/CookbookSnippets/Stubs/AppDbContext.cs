// Minimal stubs for the cookbook snippets. These types only need to compile;
// they do not provide working behavior.
namespace CookbookSnippets.Stubs;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trellis;
using CookbookSnippets.Recipe01;
using CookbookSnippets.Recipe08;
using Trellis.EntityFrameworkCore;

public sealed class AppDbContext : DbContext
{
    public AppDbContext() { }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Customer> Customers => Set<Customer>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(AppDbContext).Assembly);
}
