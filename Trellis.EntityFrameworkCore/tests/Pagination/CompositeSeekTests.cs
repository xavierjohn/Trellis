namespace Trellis.EntityFrameworkCore.Tests.Pagination;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Testing;

public class CompositeSeekTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ToPageAsync_CompositeDirections_VisitsEveryRowOnce(bool descendingScore, bool descendingId)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new SeekContext(connection);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var rows = Enumerable.Range(1, 17).Select(i => new SeekRow { Id = i, Score = i % 3 }).ToArray();
        db.Rows.AddRange(rows);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var primary = descendingScore
            ? SeekDefinition.Descending<SeekRow, double>(r => r.Score)
            : SeekDefinition.Ascending<SeekRow, double>(r => r.Score);
        var seek = descendingId ? primary.ThenDescending(r => r.Id) : primary.ThenAscending(r => r.Id);
        var expected = descendingScore ? rows.OrderByDescending(r => r.Score) : rows.OrderBy(r => r.Score);
        var ordered = descendingId ? expected.ThenByDescending(r => r.Id) : expected.ThenBy(r => r.Id);
        var seen = new List<int>();
        Cursor? cursor = null;
        for (var pageNumber = 0; pageNumber < 10; pageNumber++)
        {
            var page = (await db.Rows.ToPageAsync(new PageSize(2, 2), cursor, seek,
                cancellationToken: TestContext.Current.CancellationToken)).Unwrap();
            seen.AddRange(page.Items.Select(r => r.Id));
            cursor = page.Next;
            if (cursor is null)
                break;
        }

        seen.Should().Equal(ordered.Select(r => r.Id));
        seen.Distinct().Should().HaveCount(rows.Length);
        cursor.Should().BeNull();
    }

    [Fact]
    public async Task ToPageAsync_SuccessfulNullCodecState_ThrowsBeforeQuerying()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = new SeekContext(connection);
        var seek = SeekDefinition.Ascending<SeekRow, string>(r => r.Name, new NullStateCodec());
        var act = () => db.Rows.ToPageAsync(new PageSize(1, 1), new Cursor("supplied-token"), seek,
            cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*successful null*");
        connection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public async Task ToPageAsync_ServerOnlyComputedKey_ProjectsBoundaryInDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new SeekContext(connection);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.Rows.AddRange(
            new SeekRow { Id = 1, Name = "b" },
            new SeekRow { Id = 2, Name = "A" },
            new SeekRow { Id = 3, Name = "a" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var seek = SeekDefinition.Ascending<SeekRow, string>(r => EF.Functions.Collate(r.Name, "NOCASE"))
            .ThenAscending(r => r.Id);
        var seen = new List<int>();
        Cursor? cursor = null;
        for (var i = 0; i < 3; i++)
        {
            var page = (await db.Rows.ToPageAsync(new PageSize(1, 1), cursor, seek,
                cancellationToken: TestContext.Current.CancellationToken)).Unwrap();
            seen.AddRange(page.Items.Select(r => r.Id));
            cursor = page.Next;
        }

        seen.Should().Equal([2, 3, 1]);
        cursor.Should().BeNull();
    }

    [Fact]
    public async Task ToPageAsync_ComputedExpressionAndThirdKey_UsesMatchingSeekOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new SeekContext(connection);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var rows = Enumerable.Range(1, 12).Select(i => new SeekRow { Id = i, Score = i % 4 }).ToArray();
        db.Rows.AddRange(rows);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var origin = 1.5;
        var seek = SeekDefinition.Ascending<SeekRow, double>(r => Math.Abs(r.Score - origin))
            .ThenDescending(r => r.Score)
            .ThenAscending(r => r.Id);
        var seen = new List<int>();
        Cursor? cursor = null;
        for (var i = 0; i < 12; i++)
        {
            var page = (await db.Rows.ToPageAsync(new PageSize(1, 1), cursor, seek,
                cancellationToken: TestContext.Current.CancellationToken)).Unwrap();
            seen.AddRange(page.Items.Select(r => r.Id));
            cursor = page.Next;
            if (cursor is null)
                break;
        }

        seen.Should().Equal(rows.OrderBy(r => Math.Abs(r.Score - origin)).ThenByDescending(r => r.Score).ThenBy(r => r.Id).Select(r => r.Id));
        cursor.Should().BeNull();
    }

    [Fact]
    public async Task ToPageAsync_InvalidCursor_ReturnsFailureWithoutOpeningDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = new SeekContext(connection);
        var seek = SeekDefinition.Ascending<SeekRow, double>(r => r.Score).ThenAscending(r => r.Id);

        var result = await db.Rows.ToPageAsync(new PageSize(2, 2), new Cursor("bad-token"), seek,
            cursorFieldName: "after", cancellationToken: TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InvalidInput>().Which.Fields[0].Field.Path.Should().Be("/after");
        connection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public async Task ToPageAsync_FractionalTimestamp_DoesNotRepeatBoundaryRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new SeekContext(connection);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var start = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
        db.Rows.AddRange(Enumerable.Range(1, 3).Select(i => new SeekRow { Id = i, At = start.AddTicks(i * 12345) }));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var size = new PageSize(1, 1);
        var first = (await db.Rows.ToPageAsync(size, null, r => r.At,
            cancellationToken: TestContext.Current.CancellationToken)).Unwrap();
        var second = (await db.Rows.ToPageAsync(size, first.Next, r => r.At,
            cancellationToken: TestContext.Current.CancellationToken)).Unwrap();
        second.Items.Single().Id.Should().Be(2);
    }

    private sealed class SeekContext(SqliteConnection connection) : DbContext(
        new DbContextOptionsBuilder<SeekContext>().UseSqlite(connection).Options)
    {
        public DbSet<SeekRow> Rows => Set<SeekRow>();
    }

    private sealed class SeekRow
    {
        public int Id { get; set; }
        public double Score { get; set; }
        public DateTime At { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class NullStateCodec : ICursorCodec<string>
    {
        public Cursor Encode(string state) => new(state);
        public Result<string> TryDecode(Cursor? cursor, string? fieldName = null) => Result.Ok<string>(null!);
    }
}
