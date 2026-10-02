using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests.Pagination;

using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// SQL Server (LocalDB) integration tests for <see cref="PaginationQueryableExtensions.ToPageAsync"/>.
/// The CompareTo seek-predicate branch must round-trip through SQL Server when the key is a
/// value-object <see cref="Guid"/> projection. The continuity assertion is intentionally
/// shape-agnostic: we don't pin the .NET vs SQL Server <c>uniqueidentifier</c> ordering
/// (they differ); we only check that page1 ++ page2 ++ ... covers every row exactly once
/// in the same order EF produces with a plain <c>OrderBy(c =&gt; c.Id.Value)</c>.
/// Excluded from default test runs — use <c>dotnet test --filter "Category=Integration"</c>.
/// </summary>
[Trait("Category", "Integration")]
public class ToPageAsyncSqlServerIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=TrellisEfCoreToPageAsyncTests;Trusted_Connection=True;TrustServerCertificate=True";

    private SqlServerTestDbContext _context = null!;

    public async ValueTask InitializeAsync()
    {
        _context = new SqlServerTestDbContext(ConnectionString);
        await _context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Composite_MixedDirections_UsesSqlServerGuidOrdering()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        for (var i = 0; i < 9; i++)
            _context.Customers.Add(new TestCustomer
            {
                Id = TestCustomerId.NewUniqueV4(),
                Name = TestCustomerName.Create($"Composite{i}"),
                Email = EmailAddress.Create($"composite{i}@example.com"),
                CreatedAt = start.AddDays(i % 2),
            });
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
        var expected = await _context.Customers.OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id.Value).Select(c => c.Id.Value).ToListAsync(ct);
        var seek = SeekDefinition.Descending<TestCustomer, DateTime>(c => c.CreatedAt)
            .ThenAscending(c => c.Id.Value);
        var collected = new List<Guid>();
        Cursor? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var page = (await _context.Customers.ToPageAsync(new PageSize(2, 2), cursor, seek,
                cancellationToken: ct)).Unwrap();
            collected.AddRange(page.Items.Select(c => c.Id.Value));
            cursor = page.Next;
            if (cursor is null)
                break;
        }

        collected.Should().Equal(expected);
        cursor.Should().BeNull();
    }

    [Fact]
    public async Task GuidKey_RoundTrips_AcrossPages_SqlServer()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed 7 customers so the over-fetch (Applied=3, fetch=4) produces a clear
        // multi-page slice: page1=3, page2=3, page3=1, no page4.
        for (var i = 0; i < 7; i++)
        {
            _context.Customers.Add(new TestCustomer
            {
                Id = TestCustomerId.NewUniqueV4(),
                Name = TestCustomerName.Create($"Sql{i}"),
                Email = EmailAddress.Create($"sqlpager{i}@example.com"),
                CreatedAt = DateTime.UtcNow.AddDays(i),
            });
        }

        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Ground truth: server-side OrderBy on the SAME key path we paginate on.
        var expected = await _context.Customers
            .OrderBy(c => c.Id.Value)
            .Select(c => c.Id.Value)
            .ToListAsync(ct);

        var pageSize = new PageSize(3, 3);
        var collected = new List<Guid>();
        Cursor? cursor = null;

        for (var safety = 0; safety < 10; safety++)
        {
            var result = await _context.Customers
                .ToPageAsync(pageSize, cursor, c => c.Id.Value, cancellationToken: ct);

            var page = result.Unwrap();
            collected.AddRange(page.Items.Select(c => c.Id.Value));

            if (page.Next is null)
            {
                break;
            }

            cursor = page.Next;
        }

        collected.Should().Equal(expected);
    }

    [Fact]
    public async Task GeoDistance_FilterAndComputedSeek_ExecutesOnSqlServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var origin = GeoCoordinate.Create(0, 179.5);
        var bounds = GeoBounds.Create(origin, 250_000);
        var rows = new[]
        {
            new GeoRow { Id = 1, Latitude = 0, Longitude = 179.75 },
            new GeoRow { Id = 2, Latitude = 0, Longitude = -179.75 },
            new GeoRow { Id = 3, Latitude = 0, Longitude = 178 },
            new GeoRow { Id = 4, Latitude = 0, Longitude = -175 },
        };
        _context.GeoRows.AddRange(rows);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
        var distance = GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            row => row.Latitude, row => row.Longitude, origin);
        var withinRadius = GeoCoordinateExpressions.WithinRadius<GeoRow>(
            row => row.Latitude, row => row.Longitude, bounds);
        var seek = SeekDefinition.Ascending(distance).ThenAscending(row => row.Id);
        var expected = rows
            .Where(row => origin.DistanceMetersTo(GeoCoordinate.Create(row.Latitude, row.Longitude)) <= bounds.RadiusMeters)
            .OrderBy(row => origin.DistanceMetersTo(GeoCoordinate.Create(row.Latitude, row.Longitude)))
            .ThenBy(row => row.Id)
            .Select(row => row.Id);
        var collected = new List<int>();
        Cursor? cursor = null;

        for (var pageNumber = 0; pageNumber < 10; pageNumber++)
        {
            var page = (await _context.GeoRows.Where(withinRadius)
                .ToPageAsync(new PageSize(2, 2), cursor, seek, cancellationToken: ct)).Unwrap();
            collected.AddRange(page.Items.Select(row => row.Id));
            cursor = page.Next;
            if (cursor is null)
                break;
        }

        collected.Should().Equal(expected);
        cursor.Should().BeNull();
    }

    /// <summary>
    /// Mirrors <c>SqlServerMaybeIntegrationTests.SqlServerTestDbContext</c> — wires
    /// <c>AddTrellisInterceptors()</c> so VO <c>.Value</c> projections translate to SQL.
    /// </summary>
    private sealed class SqlServerTestDbContext : DbContext
    {
        public DbSet<TestCustomer> Customers => Set<TestCustomer>();
        public DbSet<TestOrder> Orders => Set<TestOrder>();
        public DbSet<GeoRow> GeoRows => Set<GeoRow>();

        public SqlServerTestDbContext(string connectionString)
            : base(new DbContextOptionsBuilder<SqlServerTestDbContext>()
                .UseSqlServer(connectionString).IgnoreManyServiceProvidersCreatedWarning()
                .AddTrellisInterceptors()
                .Options)
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

            modelBuilder.Entity<GeoRow>(builder =>
            {
                builder.HasKey(row => row.Id);
                builder.Property(row => row.Id).ValueGeneratedNever();
            });
        }
    }

    private sealed class GeoRow
    {
        public int Id { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}