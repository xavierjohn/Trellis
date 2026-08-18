namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

/// <summary>
/// Tests for the convention that marks an owned-collection child's domain-assigned primary key
/// <see cref="ValueGenerated.Never"/>. Without it, EF treats the key as store-generated, which
/// breaks persistence of owned children: an integer key becomes an IDENTITY column (explicit
/// inserts throw), and a non-default key on a child added to a loaded parent is read as a
/// modification → <c>UPDATE</c> (zero rows) → a spurious <see cref="DbUpdateConcurrencyException"/>.
/// </summary>
public sealed class OwnedCollectionKeyConventionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OwnedKeyDbContext> _options;

    public OwnedCollectionKeyConventionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OwnedKeyDbContext>().UseSqlite(_connection).Options;
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    private OwnedKeyDbContext NewContext() => new(_options);

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void OwnedCollectionChild_DomainAssignedGuidKey_IsValueGeneratedNever()
    {
        using var ctx = NewContext();
        var key = OwnedChildProperty(ctx, typeof(TimelineParent), nameof(TimelineParent.Events), nameof(TimelineEvent.Id));

        key.ValueGenerated.Should().Be(ValueGenerated.Never);
    }

    [Fact]
    public void OwnedCollectionChild_DomainAssignedLongKey_IsValueGeneratedNever()
    {
        using var ctx = NewContext();
        var key = OwnedChildProperty(ctx, typeof(LedgerParent), nameof(LedgerParent.Entries), nameof(LedgerEntry.Id));

        key.ValueGenerated.Should().Be(ValueGenerated.Never);
    }

    [Fact]
    public void NonOwnedEntity_IntKey_IsLeftStoreGenerated()
    {
        using var ctx = NewContext();
        var key = ctx.Model.FindEntityType(typeof(PlainEntity))!.FindProperty(nameof(PlainEntity.Id))!;

        key.ValueGenerated.Should().Be(ValueGenerated.OnAdd, "the convention only touches owned-collection keys");
    }

    [Fact]
    public void OwnedCollectionChild_ExplicitValueGeneratedOnAdd_IsRespected()
    {
        using var ctx = NewContext();
        var key = OwnedChildProperty(ctx, typeof(OptOutParent), nameof(OptOutParent.Lines), nameof(OptOutLine.Id));

        key.ValueGenerated.Should().Be(
            ValueGenerated.OnAdd,
            "an explicit ValueGeneratedOnAdd() in OnModelCreating opts back into store generation");
    }

    [Fact]
    public void OwnedCollectionChild_SynthesizedShadowKey_IsLeftStoreGenerated()
    {
        using var ctx = NewContext();
        var child = ctx.Model.FindEntityType(typeof(TaggedParent))!
            .FindNavigation(nameof(TaggedParent.Tags))!.TargetEntityType;
        var ownershipForeignKey = child.FindOwnership()!.Properties;
        var shadowKey = child.FindPrimaryKey()!.Properties
            .Single(p => p.IsShadowProperty() && !ownershipForeignKey.Contains(p));

        shadowKey.ValueGenerated.Should().Be(
            ValueGenerated.OnAdd,
            "EF Core's synthesized surrogate key has no domain value to supply and must stay store-generated");
    }

    [Fact]
    public void AddingChildToLoadedParent_PersistsTheNewChild()
    {
        var parentId = Guid.CreateVersion7();
        using (var ctx = NewContext())
        {
            var parent = new TimelineParent { Id = parentId };
            parent.Events.Add(new TimelineEvent { Id = Guid.CreateVersion7(), Description = "first" });
            ctx.Parents.Add(parent);
            ctx.SaveChanges();
        }

        using (var ctx = NewContext())
        {
            var parent = ctx.Parents.Include(p => p.Events).Single(p => p.Id == parentId);
            parent.Events.Add(new TimelineEvent { Id = Guid.CreateVersion7(), Description = "second" });
            // RED before the fix: the new child's non-default domain key is read as a modification,
            // so EF emits UPDATE ... WHERE Id=@id (zero rows) and throws DbUpdateConcurrencyException.
            ctx.SaveChanges();
        }

        using var verify = NewContext();
        verify.Parents.Include(p => p.Events).Single(p => p.Id == parentId)
            .Events.Should().HaveCount(2, "the new owned child must be INSERTed, not lost or surfaced as a 409");
    }

    [Fact]
    public void OwnedCollectionChild_DomainAssignedKey_InsertsExplicitValue_OnSqlServer()
    {
        if (!SqlServerAvailable())
        {
            Assert.Skip("SQL Server (LocalDB) is not available");
            return;
        }

        const long explicitKey = 1001L;
        using (var ctx = new LedgerSqlServerContext())
        {
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
            var parent = new LedgerParent { Id = Guid.CreateVersion7() };
            parent.Entries.Add(new LedgerEntry { Id = explicitKey, Payload = "first" });
            // RED before the fix: an integer owned-collection key mapped as store-generated becomes an
            // IDENTITY column, so inserting the domain-assigned value throws SqlException 544.
            ctx.LedgerParents.Add(parent);
            ctx.SaveChanges();
        }

        using (var ctx = new LedgerSqlServerContext())
        {
            var loaded = ctx.LedgerParents.Include(p => p.Entries).Single();
            loaded.Entries.Should().ContainSingle().Which.Id.Should().Be(explicitKey);
            ctx.Database.EnsureDeleted();
        }
    }

    private static IReadOnlyProperty OwnedChildProperty(OwnedKeyDbContext ctx, Type parent, string navigation, string keyName)
    {
        var child = ctx.Model.FindEntityType(parent)!.FindNavigation(navigation)!.TargetEntityType;
        return child.FindProperty(keyName)!;
    }

    private sealed class TimelineEvent
    {
        public Guid Id { get; set; }
        public string Description { get; set; } = "";
    }

    private sealed class TimelineParent
    {
        public Guid Id { get; set; }
        public List<TimelineEvent> Events { get; } = [];
    }

    private sealed class LedgerEntry
    {
        public long Id { get; set; }
        public string Payload { get; set; } = "";
    }

    private sealed class LedgerParent
    {
        public Guid Id { get; set; }
        public List<LedgerEntry> Entries { get; } = [];
    }

    private sealed class PlainEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class OptOutLine
    {
        public int Id { get; set; }
        public string Sku { get; set; } = "";
    }

    private sealed class OptOutParent
    {
        public Guid Id { get; set; }
        public List<OptOutLine> Lines { get; } = [];
    }

    private sealed class KeylessTag
    {
        public string Label { get; set; } = "";
    }

    private sealed class TaggedParent
    {
        public Guid Id { get; set; }
        public List<KeylessTag> Tags { get; } = [];
    }

    private sealed class OwnedKeyDbContext(DbContextOptions<OwnedKeyDbContext> options) : DbContext(options)
    {
        public DbSet<TimelineParent> Parents => Set<TimelineParent>();
        public DbSet<LedgerParent> LedgerParents => Set<LedgerParent>();
        public DbSet<PlainEntity> PlainEntities => Set<PlainEntity>();
        public DbSet<OptOutParent> OptOutParents => Set<OptOutParent>();
        public DbSet<TaggedParent> TaggedParents => Set<TaggedParent>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TimelineParent>().OwnsMany(x => x.Events);
            modelBuilder.Entity<LedgerParent>().OwnsMany(x => x.Entries);
            modelBuilder.Entity<PlainEntity>();
            modelBuilder.Entity<OptOutParent>().OwnsMany(x => x.Lines, b => b.Property(l => l.Id).ValueGeneratedOnAdd());
            modelBuilder.Entity<TaggedParent>().OwnsMany(x => x.Tags);
        }
    }

    private const string SqlServerConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=TrellisOwnedCollectionKeyTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static bool SqlServerAvailable()
    {
        try
        {
            using var connection = new SqlConnection(
                "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
            connection.Open();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private sealed class LedgerSqlServerContext : DbContext
    {
        public DbSet<LedgerParent> LedgerParents => Set<LedgerParent>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlServer(SqlServerConnectionString).IgnoreManyServiceProvidersCreatedWarning();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<LedgerParent>().OwnsMany(x => x.Entries);
    }
}