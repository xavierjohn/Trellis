namespace Trellis.Testing.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Tests for <see cref="ServiceCollectionDbProviderExtensions.ReplaceDbProvider{TContext}"/>.
/// </summary>
public class ServiceCollectionDbProviderExtensionsTests
{
    #region ReplaceDbProvider

    [Fact]
    public void ReplaceDbProvider_NoExistingRegistration_RegistersContext()
    {
        var services = new ServiceCollection();
        using var connection = CreateSqliteConnection();

        services.ReplaceDbProvider<TestAppDbContext>(options =>
            options.UseSqlite(connection));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestAppDbContext>();
        context.Should().NotBeNull();
    }

    [Fact]
    public void ReplaceDbProvider_WithExistingRegistration_ReplacesProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestAppDbContext>(options =>
            options.UseInMemoryDatabase($"original-{Guid.NewGuid()}"));

        using var connection = CreateSqliteConnection();
        services.ReplaceDbProvider<TestAppDbContext>(options =>
            options.UseSqlite(connection));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestAppDbContext>();
        context.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
    }

    [Fact]
    public void ReplaceDbProvider_ReturnsServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();
        using var connection = CreateSqliteConnection();

        var returned = services.ReplaceDbProvider<TestAppDbContext>(options =>
            options.UseSqlite(connection));

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void ReplaceDbProvider_DoesNotAffectOtherDbContexts()
    {
        var services = new ServiceCollection();
        services.AddDbContext<OtherTestDbContext>(options =>
            options.UseInMemoryDatabase($"other-{Guid.NewGuid()}"));

        using var connection = CreateSqliteConnection();
        services.ReplaceDbProvider<TestAppDbContext>(options =>
            options.UseSqlite(connection));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var other = scope.ServiceProvider.GetRequiredService<OtherTestDbContext>();
        other.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.InMemory");
    }

    [Fact]
    public void ReplaceDbProvider_RemovesOldDbContextOptions()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestAppDbContext>(options =>
            options.UseInMemoryDatabase($"original-{Guid.NewGuid()}"));

        using var connection = CreateSqliteConnection();
        services.ReplaceDbProvider<TestAppDbContext>(options =>
            options.UseSqlite(connection));

        var optionsDescriptors = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<TestAppDbContext>))
            .ToList();
        optionsDescriptors.Should().ContainSingle();
    }

    [Fact]
    public void ReplaceDbProvider_SwapsProvider_CanCreateAndQuery()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestAppDbContext>(options =>
            options.UseInMemoryDatabase($"original-{Guid.NewGuid()}"));

        using var connection = CreateSqliteConnection();
        services.ReplaceDbProvider<TestAppDbContext>(options =>
            options.UseSqlite(connection));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestAppDbContext>();
        context.Database.EnsureCreated();

        context.Items.Add(new TestItem { Name = "test-item" });
        context.SaveChanges();

        context.Items.Should().ContainSingle(e => e.Name == "test-item");
    }

    [Fact]
    public void ReplaceDbProvider_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.ReplaceDbProvider<TestAppDbContext>(options =>
            options.UseInMemoryDatabase("test"));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReplaceDbProvider_NullConfigureOptions_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.ReplaceDbProvider<TestAppDbContext>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Helpers

    private static SqliteConnection CreateSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    #endregion

    #region Test Types

    private sealed class TestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestAppDbContext(DbContextOptions<TestAppDbContext> options) : DbContext(options)
    {
        public DbSet<TestItem> Items => Set<TestItem>();
    }

    private sealed class OtherTestDbContext(DbContextOptions<OtherTestDbContext> options) : DbContext(options);

    #endregion
}