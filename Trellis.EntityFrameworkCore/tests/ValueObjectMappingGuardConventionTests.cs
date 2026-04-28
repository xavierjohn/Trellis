namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="ValueObjectMappingGuardConvention"/>.
/// Verifies that explicit <c>builder.Property(x =&gt; x.Money)</c> or
/// <c>builder.Property(x =&gt; x.Maybe)</c> calls in <c>OnModelCreating</c> are caught at
/// startup time with a clear, actionable error rather than failing deep inside EF Core.
/// </summary>
public partial class ValueObjectMappingGuardConventionTests
{
    [Fact]
    public void ExplicitPropertyOnMoney_ThrowsAtModelFinalization_WithActionableMessage()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MisusedMoneyDbContext>()
            .UseSqlite(connection)
            .Options;
        using var ctx = new MisusedMoneyDbContext(options);

        var act = () => _ = ctx.Model;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Money*scalar*")
            .WithMessage("*MisusedMoneyEntity*")
            .WithMessage("*Price*")
            .WithMessage("*MoneyConvention*")
            .WithMessage("*OnModelCreating*");
    }

    [Fact]
    public void ExplicitPropertyOnMaybe_ThrowsAtModelFinalization_WithActionableMessage()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MisusedMaybeDbContext>()
            .UseSqlite(connection)
            .Options;
        using var ctx = new MisusedMaybeDbContext(options);

        var act = () => _ = ctx.Model;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Maybe*scalar*")
            .WithMessage("*MisusedMaybeEntity*")
            .WithMessage("*OptionalNote*")
            .WithMessage("*MaybeConvention*")
            .WithMessage("*partial*");
    }

    [Fact]
    public void OwnedMoney_NoExplicitProperty_ModelBuildsSuccessfully()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<WellFormedMoneyDbContext>()
            .UseSqlite(connection)
            .Options;
        using var ctx = new WellFormedMoneyDbContext(options);

        var act = () => _ = ctx.Model;

        act.Should().NotThrow();
    }

    [Fact]
    public void PartialMaybe_NoExplicitProperty_ModelBuildsSuccessfully()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<WellFormedMaybeDbContext>()
            .UseSqlite(connection)
            .Options;
        using var ctx = new WellFormedMaybeDbContext(options);

        var act = () => _ = ctx.Model;

        act.Should().NotThrow();
    }

    #region Test entities and contexts — misuse

    private class MisusedMoneyEntity
    {
        public int Id { get; set; }
        public Money Price { get; set; } = null!;
    }

    private class MisusedMoneyDbContext : DbContext
    {
        public DbSet<MisusedMoneyEntity> Items => Set<MisusedMoneyEntity>();

        public MisusedMoneyDbContext(DbContextOptions<MisusedMoneyDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            // Misuse: explicitly mapping Money as a scalar property.
            modelBuilder.Entity<MisusedMoneyEntity>().Property(x => x.Price);
    }

    private partial class MisusedMaybeEntity
    {
        public int Id { get; set; }
        public partial Maybe<string> OptionalNote { get; set; }
    }

    private class MisusedMaybeDbContext : DbContext
    {
        public DbSet<MisusedMaybeEntity> Items => Set<MisusedMaybeEntity>();

        public MisusedMaybeDbContext(DbContextOptions<MisusedMaybeDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            // Misuse: explicitly mapping the Maybe<T> public property as a scalar instead
            // of relying on the source-generated nullable backing field.
            modelBuilder.Entity<MisusedMaybeEntity>().Property(x => x.OptionalNote);
    }

    #endregion

    #region Test entities and contexts — well-formed (positive controls)

    private class WellFormedMoneyEntity
    {
        public int Id { get; set; }
        public Money Price { get; set; } = null!;
    }

    private class WellFormedMoneyDbContext : DbContext
    {
        public DbSet<WellFormedMoneyEntity> Items => Set<WellFormedMoneyEntity>();

        public WellFormedMoneyDbContext(DbContextOptions<WellFormedMoneyDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();
    }

    private partial class WellFormedMaybeEntity
    {
        public int Id { get; set; }
        public partial Maybe<string> OptionalNote { get; set; }
    }

    private class WellFormedMaybeDbContext : DbContext
    {
        public DbSet<WellFormedMaybeEntity> Items => Set<WellFormedMaybeEntity>();

        public WellFormedMaybeDbContext(DbContextOptions<WellFormedMaybeDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();
    }

    #endregion
}