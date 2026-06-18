namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Regression tests for owned collections inside composite value objects.
/// <para>
/// A composite <see cref="ValueObject"/> that owns a collection of composite value objects —
/// exposed as an <c>IReadOnlyList&lt;T&gt;</c> facade over a private backing field — is auto-mapped
/// as an EF Core owned collection by the Trellis conventions, with no manual <c>Ignore</c> +
/// <c>OwnsMany</c>. This holds whether the composite owner is required or optional
/// (<see cref="Maybe{T}"/>). The model-shape tests lock the auto-mapping; the SQL Server tests
/// lock the round-trip (and skip when LocalDB is unavailable).
/// </para>
/// <para>
/// Entity collections behave differently and are out of scope here: Trellis auto-owns value
/// objects only, so an entity collection is not auto-owned — a root entity collection may be
/// discovered as a regular non-owned relationship, and one nested inside a composite VO can fail
/// convention-only. Use an explicit <c>OwnsMany</c> when owned aggregate-child semantics are required.
/// </para>
/// </summary>
public partial class CompositeValueObjectCollectionConventionTests : IDisposable
{
    private CollectionVoDbContext? _context;
    private SqliteConnection? _connection;

    private CollectionVoDbContext Context
    {
        get
        {
            if (_context is not null)
                return _context;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<CollectionVoDbContext>()
                .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
                .Options;
            _context = new CollectionVoDbContext(options);
            _context.Database.EnsureCreated();
            return _context;
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RequiredComposite_OwningVoCollection_IsAutoMappedAsOwnedCollection()
    {
        var owner = Context.Model.FindEntityType(typeof(RequiredScorecardEntity))!;
        var inningNav = owner.FindNavigation(nameof(RequiredScorecardEntity.Inning));

        inningNav.Should().NotBeNull();
        inningNav!.TargetEntityType.IsOwned().Should().BeTrue();

        var linesNav = inningNav.TargetEntityType.FindNavigation(nameof(CricketInning.Lines));
        linesNav.Should().NotBeNull(
            "a composite VO's IReadOnlyList<VO> collection is auto-mapped without manual Ignore+OwnsMany");
        linesNav!.IsCollection.Should().BeTrue();
        linesNav.TargetEntityType.IsOwned().Should().BeTrue();
        linesNav.TargetEntityType.ClrType.Should().Be<CricketLine>();
    }

    [Fact]
    public void MaybeComposite_OwningVoCollection_IsAutoMappedAsOwnedCollection()
    {
        var owner = Context.Model.FindEntityType(typeof(MaybeScorecardEntity))!;
        // Maybe<T> ownership is created via the source-generated backing field.
        var inningNav = owner.FindNavigation("_inning");

        inningNav.Should().NotBeNull();
        inningNav!.TargetEntityType.IsOwned().Should().BeTrue();

        var linesNav = inningNav.TargetEntityType.FindNavigation(nameof(CricketInning.Lines));
        linesNav.Should().NotBeNull(
            "an optional composite VO's IReadOnlyList<VO> collection is auto-mapped without manual Ignore+OwnsMany");
        linesNav!.IsCollection.Should().BeTrue();
        linesNav.TargetEntityType.IsOwned().Should().BeTrue();
    }

    [Fact]
    public void RequiredComposite_OwnedVoCollection_UsesBareColumns_WhileTableSplitScalarIsPrefixed()
    {
        var owner = Context.Model.FindEntityType(typeof(RequiredScorecardEntity))!;
        var inningType = owner.FindNavigation(nameof(RequiredScorecardEntity.Inning))!.TargetEntityType;
        var lineType = inningType.FindNavigation(nameof(CricketInning.Lines))!.TargetEntityType;

        // An owned VO collection maps to its own table (RequiredScorecards_Lines), so EF Core's
        // table-splitting prefix does not apply — the columns keep their bare conventional names.
        // The convention must defer to EF here and NOT stamp the owner-navigation prefix on them.
        StoreColumn(lineType, nameof(CricketLine.Bowler)).Should().Be("Bowler");
        StoreColumn(lineType, nameof(CricketLine.Runs)).Should().Be("Runs");

        // A required composite scalar table-splits into the owner's table, so EF Core prefixes it
        // with the owner navigation name.
        StoreColumn(inningType, nameof(CricketInning.Team)).Should().Be("Inning_Team");
    }

    private static string? StoreColumn(
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType entityType, string propertyName)
    {
        var property = entityType.FindProperty(propertyName)!;
        var table = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Create(
            entityType, Microsoft.EntityFrameworkCore.Metadata.StoreObjectType.Table);
        return table.HasValue ? property.GetColumnName(table.Value) : property.GetColumnName();
    }

    [Fact]
    public void RequiredComposite_OwningVoCollection_RoundTrips_OnSqlServer()
    {
        if (!SqlServerAvailable())
        {
            Assert.Skip("SQL Server (LocalDB) is not available");
            return;
        }

        using (var ctx = new CollectionVoSqlServerContext())
        {
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
            ctx.Set<RequiredScorecardEntity>().Add(new RequiredScorecardEntity
            {
                Inning = new CricketInning(
                    TestStateCode.Create("Alpha"),
                    [
                        new CricketLine(TestStateCode.Create("Bob"), TestTicketNumber.Create(10)),
                        new CricketLine(TestStateCode.Create("Cara"), TestTicketNumber.Create(20)),
                    ]),
            });
            ctx.SaveChanges();
        }

        using (var ctx = new CollectionVoSqlServerContext())
        {
            var loaded = ctx.Set<RequiredScorecardEntity>().Single();
            loaded.Inning.Lines.Should().HaveCount(2);
            ctx.Database.EnsureDeleted();
        }
    }

    [Fact]
    public void MaybeComposite_OwningVoCollection_RoundTrips_OnSqlServer()
    {
        if (!SqlServerAvailable())
        {
            Assert.Skip("SQL Server (LocalDB) is not available");
            return;
        }

        using (var ctx = new CollectionVoSqlServerContext())
        {
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
            ctx.Set<MaybeScorecardEntity>().Add(new MaybeScorecardEntity
            {
                Inning = Maybe.From(new CricketInning(
                    TestStateCode.Create("Alpha"),
                    [
                        new CricketLine(TestStateCode.Create("Bob"), TestTicketNumber.Create(10)),
                        new CricketLine(TestStateCode.Create("Cara"), TestTicketNumber.Create(20)),
                    ])),
            });
            ctx.SaveChanges();
        }

        using (var ctx = new CollectionVoSqlServerContext())
        {
            var loaded = ctx.Set<MaybeScorecardEntity>().Single();
            loaded.Inning.HasValue.Should().BeTrue();
            loaded.Inning.Value.Lines.Should().HaveCount(2);
            ctx.Database.EnsureDeleted();
        }
    }

    private const string SqlServerConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=TrellisCompositeVoCollectionTests;Trusted_Connection=True;TrustServerCertificate=True";

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

    [OwnedEntity]
    public partial class CricketLine : ValueObject
    {
        public TestStateCode Bowler { get; private set; } = null!;
        public TestTicketNumber Runs { get; private set; } = null!;

        public CricketLine(TestStateCode bowler, TestTicketNumber runs)
        {
            Bowler = bowler;
            Runs = runs;
        }

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Bowler;
            yield return Runs;
        }
    }

    [OwnedEntity]
    public partial class CricketInning : ValueObject
    {
        private readonly List<CricketLine> _lines = [];

        public TestStateCode Team { get; private set; } = null!;
        public IReadOnlyList<CricketLine> Lines => _lines;

        public CricketInning(TestStateCode team, IEnumerable<CricketLine> lines)
        {
            Team = team;
            _lines.AddRange(lines);
        }

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Team;
        }
    }

    private sealed class RequiredScorecardEntity
    {
        public int Id { get; set; }
        public CricketInning Inning { get; set; } = null!;
    }

    private sealed partial class MaybeScorecardEntity
    {
        public int Id { get; set; }
        public partial Maybe<CricketInning> Inning { get; set; }
    }

    private sealed class CollectionVoDbContext(DbContextOptions<CollectionVoDbContext> options) : DbContext(options)
    {
        public DbSet<RequiredScorecardEntity> RequiredScorecards => Set<RequiredScorecardEntity>();
        public DbSet<MaybeScorecardEntity> MaybeScorecards => Set<MaybeScorecardEntity>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestStateCode).Assembly);
    }

    private sealed class CollectionVoSqlServerContext : DbContext
    {
        public DbSet<RequiredScorecardEntity> RequiredScorecards => Set<RequiredScorecardEntity>();
        public DbSet<MaybeScorecardEntity> MaybeScorecards => Set<MaybeScorecardEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlServer(SqlServerConnectionString).IgnoreManyServiceProvidersCreatedWarning();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestStateCode).Assembly);
    }
}
