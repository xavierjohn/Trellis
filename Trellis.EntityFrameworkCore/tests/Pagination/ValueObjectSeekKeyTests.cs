namespace Trellis.EntityFrameworkCore.Tests.Pagination;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;
using Trellis.Testing;

/// <summary>
/// A seek keyed directly on a value object (<c>c =&gt; c.Id</c>) must page the same rows in the same
/// order as the provider-value projection (<c>c =&gt; c.Id.Value</c>).
/// </summary>
public sealed class ValueObjectSeekKeyTests : IAsyncLifetime
{
    private TestDbContext _context = null!;
    private SqliteConnection _connection = null!;

    public async ValueTask InitializeAsync()
    {
        (_context, _connection) = TestDbContext.CreateInMemory(withInterceptors: true);
        for (var i = 0; i < 9; i++)
            _context.Customers.Add(new TestCustomer
            {
                Id = TestCustomerId.NewUniqueV4(),
                Name = TestCustomerName.Create($"Name{i % 3}"),
                Email = EmailAddress.Create($"seek{i}@example.com"),
                CreatedAt = new DateTime(2024, 3, 1 + (i % 2), 0, 0, 0, DateTimeKind.Utc),
            });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedGuidIdKey_PagesLikeItsValueProjection(bool descending)
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = descending
            ? await _context.Customers.OrderByDescending(c => c.Id.Value).Select(c => c.Id.Value).ToListAsync(ct)
            : await _context.Customers.OrderBy(c => c.Id.Value).Select(c => c.Id.Value).ToListAsync(ct);
        var seek = descending
            ? SeekDefinition.Descending<TestCustomer, TestCustomerId>(c => c.Id)
            : SeekDefinition.Ascending<TestCustomer, TestCustomerId>(c => c.Id);

        var collected = await CollectAsync(seek, c => c.Id.Value, ct);

        collected.Should().Equal(expected);
    }

    [Fact]
    public async Task GeneratedGuidIdKey_AsCompositeSecondary_PagesEveryRowOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = await _context.Customers.OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id.Value).Select(c => c.Id.Value).ToListAsync(ct);
        var seek = SeekDefinition.Descending<TestCustomer, DateTime>(c => c.CreatedAt)
            .ThenAscending<TestCustomerId>(c => c.Id);

        var collected = await CollectAsync(seek, c => c.Id.Value, ct);

        collected.Should().Equal(expected);
    }

    [Fact]
    public async Task GeneratedStringKey_PagesLikeItsValueProjection()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = await _context.Customers.OrderBy(c => c.Name.Value).ThenBy(c => c.Id.Value)
            .Select(c => c.Id.Value).ToListAsync(ct);
        var seek = SeekDefinition.Ascending<TestCustomer, TestCustomerName>(c => c.Name)
            .ThenAscending<Guid>(c => c.Id.Value);

        var collected = await CollectAsync(seek, c => c.Id.Value, ct);

        collected.Should().Equal(expected);
    }

    private async Task<List<Guid>> CollectAsync<TState>(
        SeekDefinition<TestCustomer, TState> seek, Func<TestCustomer, Guid> id, CancellationToken ct)
        where TState : notnull
    {
        var collected = new List<Guid>();
        Cursor? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var page = (await _context.Customers.ToPageAsync(new PageSize(2, 2), cursor, seek,
                cancellationToken: ct)).Unwrap();
            collected.AddRange(page.Items.Select(id));
            cursor = page.Next;
            if (cursor is null)
                break;
        }

        cursor.Should().BeNull();
        return collected;
    }
}
