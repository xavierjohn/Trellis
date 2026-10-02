namespace Trellis.EntityFrameworkCore.Tests;

using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Primitives;
using Trellis.Testing;

public sealed class GeoCoordinateExpressionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GeoContext _context;

    public GeoCoordinateExpressionsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _context = new GeoContext(_connection);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DistanceMetersTo_SqliteProjection_MatchesPrimitive()
    {
        var origin = GeoCoordinate.Create(0, 180);
        var rows = new[]
        {
            new GeoRow { Id = 1, Latitude = 0, Longitude = -180 },
            new GeoRow { Id = 2, Latitude = 0, Longitude = 179 },
            new GeoRow { Id = 3, Latitude = 90, Longitude = 35 },
            new GeoRow { Id = 4, Latitude = -90, Longitude = 25 },
            new GeoRow { Id = 5, Latitude = 0, Longitude = 0 },
        };
        _context.Rows.AddRange(rows);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var distance = GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            row => row.Latitude, row => row.Longitude, origin);

        var actual = await _context.Rows.OrderBy(row => row.Id)
            .Select(distance)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        actual.Should().HaveCount(rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            var expected = origin.DistanceMetersTo(
                GeoCoordinate.Create(rows[index].Latitude, rows[index].Longitude));
            actual[index].Should().BeApproximately(expected, 0.001);
        }
    }

    [Fact]
    public async Task DistanceMetersTo_SamePole_SqliteProjection_ReturnsZero()
    {
        var origin = GeoCoordinate.Create(90, 0);
        _context.Rows.Add(new GeoRow { Id = 1, Latitude = 90, Longitude = 123 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var distance = GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            row => row.Latitude, row => row.Longitude, origin);

        var actual = await _context.Rows.Select(distance)
            .SingleAsync(TestContext.Current.CancellationToken);

        actual.Should().Be(0);
    }

    [Fact]
    public async Task WithinBounds_AntimeridianCrossing_SqliteFiltersBothLongitudeRanges()
    {
        var radius = 6_371_008.8 * Math.PI / 180;
        var bounds = GeoBounds.Create(GeoCoordinate.Create(0, 179.5), radius);
        _context.Rows.AddRange(
            new GeoRow { Id = 1, Latitude = 0, Longitude = 179.75 },
            new GeoRow { Id = 2, Latitude = 0, Longitude = -179.75 },
            new GeoRow { Id = 3, Latitude = 0, Longitude = 177 },
            new GeoRow { Id = 4, Latitude = 2, Longitude = 179.75 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var predicate = GeoCoordinateExpressions.WithinBounds<GeoRow>(
            row => row.Latitude, row => row.Longitude, bounds);

        var ids = await _context.Rows.Where(predicate).OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal([1, 2]);
    }

    [Fact]
    public async Task WithinRadius_SqliteFiltersBoundingFalsePositivesWithExactDistance()
    {
        var origin = GeoCoordinate.Create(0, 0);
        var radius = 120_000d;
        var bounds = GeoBounds.Create(origin, radius);
        _context.Rows.AddRange(
            new GeoRow { Id = 1, Latitude = 0, Longitude = 1 },
            new GeoRow { Id = 2, Latitude = 1, Longitude = 1 },
            new GeoRow { Id = 3, Latitude = -1, Longitude = 0 },
            new GeoRow { Id = 4, Latitude = 0, Longitude = 2 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var predicate = GeoCoordinateExpressions.WithinRadius<GeoRow>(
            row => row.Latitude, row => row.Longitude, bounds);

        var ids = await _context.Rows.Where(predicate).OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal([1, 3]);
    }

    [Theory]
    [InlineData(89.5, 40, 100_000)]
    [InlineData(0, 0, Math.PI * GeoCoordinate.MeanEarthRadiusMeters)]
    public async Task WithinRadius_PoleOrWorldBounds_SqliteMatchesInMemory(
        double originLatitude,
        double originLongitude,
        double radiusMeters)
    {
        var origin = GeoCoordinate.Create(originLatitude, originLongitude);
        var bounds = GeoBounds.Create(origin, radiusMeters);
        var rows = new[]
        {
            new GeoRow { Id = 1, Latitude = 90, Longitude = -170 },
            new GeoRow { Id = 2, Latitude = 90, Longitude = 170 },
            new GeoRow { Id = 3, Latitude = -90, Longitude = 0 },
            new GeoRow { Id = 4, Latitude = 0, Longitude = 0 },
        };
        _context.Rows.AddRange(rows);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var predicate = GeoCoordinateExpressions.WithinRadius<GeoRow>(
            row => row.Latitude, row => row.Longitude, bounds);
        var expected = rows
            .Where(row => origin.DistanceMetersTo(
                GeoCoordinate.Create(row.Latitude, row.Longitude)) <= radiusMeters)
            .Select(row => row.Id);

        var actual = await _context.Rows.Where(predicate).OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        actual.Should().Equal(expected);
    }

    [Fact]
    public async Task WithinRadius_NearAntipodalMeasuredBoundary_SqliteIncludesTarget()
    {
        var origin = GeoCoordinate.Create(89.5, 0);
        var target = GeoCoordinate.Create(-89.9, 0);
        _context.Rows.Add(new GeoRow
        {
            Id = 1,
            Latitude = target.Latitude,
            Longitude = target.Longitude,
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var distanceSelector = GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            row => row.Latitude, row => row.Longitude, origin);
        var radiusMeters = await _context.Rows
            .Select(distanceSelector)
            .SingleAsync(TestContext.Current.CancellationToken);
        var bounds = GeoBounds.Create(origin, radiusMeters);
        var predicate = GeoCoordinateExpressions.WithinRadius<GeoRow>(
            row => row.Latitude, row => row.Longitude, bounds);

        var included = await _context.Rows.AnyAsync(
            predicate,
            TestContext.Current.CancellationToken);

        included.Should().BeTrue();
    }

    [Fact]
    public void NearbyQuery_DifferentSearchValues_SqliteUsesOneParameterizedQueryShape()
    {
        var first = BuildNearbyQuerySql(
            GeoCoordinate.Create(47.6062, -122.3321),
            radiusMeters: 10_000);
        var second = BuildNearbyQuerySql(
            GeoCoordinate.Create(40.7128, -74.0060),
            radiusMeters: 25_000);

        first.Should().Contain(".param set");
        ParameterDeclarations(first).Should().NotBeEmpty();
        QueryBody(first).Should().Be(QueryBody(second));
    }

    [Fact]
    public void NearbyQuery_SqlServerTranslatesMathAndParameterizesSearchValues()
    {
        var options = new DbContextOptionsBuilder<SqlServerGeoContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GeoTranslationProbe;Trusted_Connection=True")
            .Options;
        using var context = new SqlServerGeoContext(options);
        var origin = GeoCoordinate.Create(47.6062, -122.3321);
        var bounds = GeoBounds.Create(origin, 10_000);
        var predicate = GeoCoordinateExpressions.WithinRadius<GeoRow>(
            row => row.Latitude, row => row.Longitude, bounds);
        var distance = GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            row => row.Latitude, row => row.Longitude, origin);

        var sql = context.Rows.Where(predicate).OrderBy(distance).ToQueryString();

        sql.Should().Contain("DECLARE @");
        sql.Should().Contain("ASIN");
        sql.Should().Contain("SQRT");
        sql.Should().Contain("SIN");
        sql.Should().Contain("COS");
    }

    [Fact]
    public async Task DistanceMetersTo_ComputedSeek_SqliteVisitsEveryRowOnce()
    {
        var origin = GeoCoordinate.Create(0, 0);
        var bounds = GeoBounds.Create(origin, 300_000);
        var rows = new[]
        {
            new GeoRow { Id = 1, Latitude = 0, Longitude = 1 },
            new GeoRow { Id = 2, Latitude = 0, Longitude = -1 },
            new GeoRow { Id = 3, Latitude = 0, Longitude = 2 },
            new GeoRow { Id = 4, Latitude = 0, Longitude = 0 },
            new GeoRow { Id = 5, Latitude = 0, Longitude = -2 },
        };
        _context.Rows.AddRange(rows);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
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
            var page = (await _context.Rows.Where(withinRadius)
                .ToPageAsync(new PageSize(2, 2), cursor, seek,
                    cancellationToken: TestContext.Current.CancellationToken)).Unwrap();
            collected.AddRange(page.Items.Select(row => row.Id));
            cursor = page.Next;
            if (cursor is null)
                break;
        }

        collected.Should().Equal(expected);
        collected.Distinct().Should().HaveCount(rows.Length);
        cursor.Should().BeNull();
    }

    [Fact]
    public void Methods_NullArguments_Throw()
    {
        var origin = GeoCoordinate.Create(0, 0);
        var bounds = GeoBounds.Create(origin, 1);
        Expression<Func<GeoRow, double>> latitude = row => row.Latitude;
        Expression<Func<GeoRow, double>> longitude = row => row.Longitude;

        var nullLatitude = () => GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            null!, longitude, origin);
        var nullLongitude = () => GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            latitude, null!, origin);
        var nullOrigin = () => GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            latitude, longitude, null!);
        var nullBounds = () => GeoCoordinateExpressions.WithinRadius<GeoRow>(
            latitude, longitude, null!);

        nullLatitude.Should().Throw<ArgumentNullException>().WithParameterName("latitudeSelector");
        nullLongitude.Should().Throw<ArgumentNullException>().WithParameterName("longitudeSelector");
        nullOrigin.Should().Throw<ArgumentNullException>().WithParameterName("origin");
        nullBounds.Should().Throw<ArgumentNullException>().WithParameterName("bounds");
    }

    private string BuildNearbyQuerySql(GeoCoordinate origin, double radiusMeters)
    {
        var bounds = GeoBounds.Create(origin, radiusMeters);
        var predicate = GeoCoordinateExpressions.WithinRadius<GeoRow>(
            row => row.Latitude, row => row.Longitude, bounds);
        var distance = GeoCoordinateExpressions.DistanceMetersTo<GeoRow>(
            row => row.Latitude, row => row.Longitude, origin);
        return _context.Rows.Where(predicate).OrderBy(distance).ToQueryString();
    }

    private static string[] ParameterDeclarations(string sql) =>
        sql.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeWhile(line => line.StartsWith(".param set", StringComparison.Ordinal))
            .ToArray();

    private static string QueryBody(string sql) =>
        string.Join(
            Environment.NewLine,
            sql.Split(Environment.NewLine)
                .SkipWhile(line => line.StartsWith(".param set", StringComparison.Ordinal))
                .SkipWhile(string.IsNullOrWhiteSpace));

    private sealed class GeoContext(SqliteConnection connection) : DbContext(
        new DbContextOptionsBuilder<GeoContext>().UseSqlite(connection).Options)
    {
        public DbSet<GeoRow> Rows => Set<GeoRow>();
    }

    private sealed class SqlServerGeoContext(DbContextOptions<SqlServerGeoContext> options)
        : DbContext(options)
    {
        public DbSet<GeoRow> Rows => Set<GeoRow>();
    }

    private sealed class GeoRow
    {
        public int Id { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}