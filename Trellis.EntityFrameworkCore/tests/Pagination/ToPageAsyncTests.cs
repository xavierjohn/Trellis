using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests.Pagination;

using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// SQLite in-memory tests for <see cref="PaginationQueryableExtensions.ToPageAsync"/>.
/// Covers cursor round-trip, over-fetch slicing, malformed-cursor handling, argument
/// validation, and the two seek-predicate strategies (Expression.GreaterThan for
/// <c>decimal</c> / <c>DateTime</c> and the <c>IComparable&lt;TKey&gt;.CompareTo</c>
/// fallback for value-object <see cref="Guid"/> projections).
/// </summary>
public class ToPageAsyncTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly SqliteConnection _connection;
    private readonly TestCustomerId _customerId;

    public ToPageAsyncTests()
    {
        (_context, _connection) = TestDbContext.CreateInMemory();
        _customerId = TestCustomerId.NewUniqueV4();
        SeedCustomer();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedCustomer()
    {
        _context.Customers.Add(new TestCustomer
        {
            Id = _customerId,
            Name = TestCustomerName.Create("Pager"),
            Email = EmailAddress.Create("pager@example.com"),
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    private async Task SeedOrdersAsync(int count, CancellationToken ct)
    {
        for (var i = 1; i <= count; i++)
        {
            _context.Orders.Add(new TestOrder
            {
                Id = TestOrderId.NewUniqueV4(),
                CustomerId = _customerId,
                Amount = i, // distinct, ascending
                Status = TestOrderStatus.Draft,
            });
        }

        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
    }

    // ───── Empty / single-page / exact-fit / over-fetch ──────────────────────

    [Fact]
    public async Task EmptySource_ReturnsEmptyPage_NoNext()
    {
        var ct = TestContext.Current.CancellationToken;
        var pageSize = new PageSize(10, 10);

        var result = await _context.Orders.ToPageAsync(pageSize, cursor: null, o => o.Amount, cancellationToken: ct);

        result.IsSuccess.Should().BeTrue();
        result.TryGetValue(out var page).Should().BeTrue();
        page.Items.Should().BeEmpty();
        page.Next.Should().BeNull();
        page.RequestedLimit.Should().Be(10);
        page.AppliedLimit.Should().Be(10);
    }

    [Fact]
    public async Task SinglePage_FewerThanApplied_NoNext()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(3, ct);

        var result = await _context.Orders.ToPageAsync(new PageSize(10, 10), cursor: null, o => o.Amount, cancellationToken: ct);

        result.TryGetValue(out var page).Should().BeTrue();
        page.Items.Should().HaveCount(3);
        page.Next.Should().BeNull();
    }

    [Fact]
    public async Task ExactFit_AppliedRowsExactlyAvailable_NoNext()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(5, ct);

        var result = await _context.Orders.ToPageAsync(new PageSize(5, 5), cursor: null, o => o.Amount, cancellationToken: ct);

        result.TryGetValue(out var page).Should().BeTrue();
        page.Items.Should().HaveCount(5);
        page.Next.Should().BeNull();
    }

    [Fact]
    public async Task OverFetch_EmitsNextCursor_AndDropsExtra()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(7, ct);

        var result = await _context.Orders.ToPageAsync(new PageSize(3, 3), cursor: null, o => o.Amount, cancellationToken: ct);

        result.TryGetValue(out var page).Should().BeTrue();
        page.Items.Should().HaveCount(3);
        page.Items.Select(o => o.Amount).Should().Equal(1m, 2m, 3m);
        page.Next.Should().NotBeNull();
    }

    // ───── Cursor round-trip (continuity) ───────────────────────────────────

    [Fact]
    public async Task CursorRoundTrip_PaginatesEntireSet_NoGapsNoDuplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(7, ct);

        var pageSize = new PageSize(3, 3);
        var collected = new List<decimal>();
        Cursor? cursor = null;

        for (var pageIndex = 0; pageIndex < 5; pageIndex++)
        {
            var pageResult = await _context.Orders.ToPageAsync(pageSize, cursor, o => o.Amount, cancellationToken: ct);
            pageResult.TryGetValue(out var page).Should().BeTrue();
            collected.AddRange(page.Items.Select(o => o.Amount));
            cursor = page.Next;
            if (cursor is null) break;
        }

        collected.Should().Equal(1m, 2m, 3m, 4m, 5m, 6m, 7m);
        cursor.Should().BeNull("the final page should close the cursor chain");
    }

    [Fact]
    public async Task LastPage_UnderFill_ClosesNext()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(5, ct);

        // page 1
        var page1 = await _context.Orders.ToPageAsync(new PageSize(3, 3), cursor: null, o => o.Amount, cancellationToken: ct);
        page1.TryGetValue(out var p1).Should().BeTrue();
        p1.Items.Should().HaveCount(3);
        p1.Next.Should().NotBeNull();

        // page 2 — only 2 remaining
        var page2 = await _context.Orders.ToPageAsync(new PageSize(3, 3), p1.Next, o => o.Amount, cancellationToken: ct);
        page2.TryGetValue(out var p2).Should().BeTrue();
        p2.Items.Should().HaveCount(2);
        p2.Items.Select(o => o.Amount).Should().Equal(4m, 5m);
        p2.Next.Should().BeNull();
    }

    // ───── PageSize round-trip ──────────────────────────────────────────────

    [Fact]
    public async Task PageSize_WasCapped_RoundTripsThroughPage()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(5, ct);
        var pageSize = PageSize.FromRequested(requested: 200, max: 50); // capped at 50

        var result = await _context.Orders.ToPageAsync(pageSize, cursor: null, o => o.Amount, cancellationToken: ct);

        result.TryGetValue(out var page).Should().BeTrue();
        page.RequestedLimit.Should().Be(200);
        page.AppliedLimit.Should().Be(50);
        page.WasCapped.Should().BeTrue();
    }

    // ───── Malformed-cursor handling ────────────────────────────────────────

    [Fact]
    public async Task MalformedCursor_ReturnsInvalidInput_DefaultFieldName()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(3, ct);
        var bogus = new Cursor("!!!notbase64!!!");

        var result = await _context.Orders.ToPageAsync(new PageSize(10, 10), bogus, o => o.Amount, cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        result.TryGetError(out var error).Should().BeTrue();
        error.Should().BeOfType<Error.InvalidInput>();
        var invalid = (Error.InvalidInput)error!;
        invalid.Fields.Length.Should().Be(1);
        invalid.Fields[0].Field.Path.Should().Be("/cursor");
        invalid.Fields[0].ReasonCode.Should().Be("cursor.malformed");
    }

    [Fact]
    public async Task MalformedCursor_CustomFieldName_HonoredInError()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(3, ct);
        var bogus = new Cursor("!!!notbase64!!!");

        var result = await _context.Orders.ToPageAsync(
            new PageSize(10, 10), bogus, o => o.Amount, cursorFieldName: "afterId", cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        result.TryGetError(out var error).Should().BeTrue();
        var invalid = (Error.InvalidInput)error!;
        invalid.Fields.Length.Should().Be(1);
        invalid.Fields[0].Field.Path.Should().Be("/afterId");
    }

    [Fact]
    public async Task DefaultCursor_ReturnsInvalidInput()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOrdersAsync(3, ct);

        var result = await _context.Orders.ToPageAsync(
            new PageSize(10, 10), default(Cursor), o => o.Amount, cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
    }

    // ───── Argument validation ──────────────────────────────────────────────

    [Fact]
    public async Task NullSource_Throws()
    {
        IQueryable<TestOrder>? source = null;
        var act = () => source!.ToPageAsync(new PageSize(10, 10), null, o => o.Amount, cancellationToken: default);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("source");
    }

    [Fact]
    public async Task NullKeySelector_Throws()
    {
        Expression<Func<TestOrder, decimal>>? keySelector = null;
        var act = () => _context.Orders.ToPageAsync(new PageSize(10, 10), null, keySelector!, cancellationToken: default);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("keySelector");
    }

    [Fact]
    public async Task DefaultPageSize_RejectedBeforeSql()
    {
        var act = () => _context.Orders.ToPageAsync(default(PageSize), null, o => o.Amount, cancellationToken: default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("pageSize");
    }

    // ───── DateTime key (Expression.GreaterThan path) ───────────────────────

    [Fact]
    public async Task DateTimeKey_PaginatesByCreatedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        // Seed 5 customers with distinct CreatedAt
        for (var i = 0; i < 5; i++)
        {
            _context.Customers.Add(new TestCustomer
            {
                Id = TestCustomerId.NewUniqueV4(),
                Name = TestCustomerName.Create($"User{i}"),
                Email = EmailAddress.Create($"user{i}@example.com"),
                CreatedAt = new DateTime(2024, 2, i + 1, 0, 0, 0, DateTimeKind.Utc),
            });
        }

        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Page 1 picks 2; page 2 picks the next 2; then no more.
        var pageSize = new PageSize(2, 2);
        var page1 = await _context.Customers
            .Where(c => c.Email != EmailAddress.Create("pager@example.com")) // exclude seeded fixture customer
            .ToPageAsync(pageSize, null, c => c.CreatedAt, cancellationToken: ct);

        page1.TryGetValue(out var p1).Should().BeTrue();
        p1.Items.Should().HaveCount(2);
        p1.Items.Select(c => c.CreatedAt).Should().BeInAscendingOrder();
        p1.Next.Should().NotBeNull();

        var page2 = await _context.Customers
            .Where(c => c.Email != EmailAddress.Create("pager@example.com"))
            .ToPageAsync(pageSize, p1.Next, c => c.CreatedAt, cancellationToken: ct);
        page2.TryGetValue(out var p2).Should().BeTrue();
        p2.Items.Should().HaveCount(2);
        var p1Ids = p1.Items.Select(c => c.Id).ToList();
        p2.Items.Select(c => c.Id).Should().NotIntersectWith(p1Ids);
    }

    // ───── Value-object Guid key (CompareTo path under interceptors) ────────

    [Fact]
    public async Task GuidKey_ViaValueObjectProjection_RequiresInterceptors()
    {
        var ct = TestContext.Current.CancellationToken;

        // New context with interceptors so c.Id.Value translates.
        var (interceptedCtx, interceptedConn) = TestDbContext.CreateInMemory(withInterceptors: true);
        try
        {
            var customers = new List<TestCustomer>();
            for (var i = 0; i < 5; i++)
            {
                customers.Add(new TestCustomer
                {
                    Id = TestCustomerId.NewUniqueV4(),
                    Name = TestCustomerName.Create($"VoUser{i}"),
                    Email = EmailAddress.Create($"vo{i}@example.com"),
                    CreatedAt = new DateTime(2024, 3, i + 1, 0, 0, 0, DateTimeKind.Utc),
                });
            }

            interceptedCtx.Customers.AddRange(customers);
            await interceptedCtx.SaveChangesAsync(ct);
            interceptedCtx.ChangeTracker.Clear();

            // Continuity: page1 + page2 covers everything; no duplicates, no skips.
            var pageSize = new PageSize(2, 2);
            var collected = new List<Guid>();
            Cursor? cursor = null;
            for (var i = 0; i < 5; i++)
            {
                var pageResult = await interceptedCtx.Customers.ToPageAsync(
                    pageSize, cursor, c => c.Id.Value, cancellationToken: ct);
                pageResult.TryGetValue(out var p).Should().BeTrue(
                    "VO Guid pagination should succeed under AddTrellisInterceptors");
                collected.AddRange(p.Items.Select(c => c.Id.Value));
                cursor = p.Next;
                if (cursor is null) break;
            }

            collected.Distinct().Should().HaveCount(collected.Count, "no duplicates across pages");
            collected.Should().HaveCount(5);
        }
        finally
        {
            interceptedCtx.Dispose();
            interceptedConn.Dispose();
        }
    }

    // ───── Duplicate-key boundary documented behaviour ──────────────────────

    [Fact]
    public async Task DuplicateKey_AtBoundary_SkipsRowsAtSameKey()
    {
        // Seed two orders with Amount = 1, three with Amount = 2.
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 2; i++)
            _context.Orders.Add(new TestOrder
            {
                Id = TestOrderId.NewUniqueV4(),
                CustomerId = _customerId,
                Amount = 1m,
                Status = TestOrderStatus.Draft,
            });
        for (var i = 0; i < 3; i++)
            _context.Orders.Add(new TestOrder
            {
                Id = TestOrderId.NewUniqueV4(),
                CustomerId = _customerId,
                Amount = 2m,
                Status = TestOrderStatus.Draft,
            });
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Page 1 picks the first two rows — both Amount = 1.
        var page1 = await _context.Orders.ToPageAsync(new PageSize(2, 2), null, o => o.Amount, cancellationToken: ct);
        page1.TryGetValue(out var p1).Should().BeTrue();
        p1.Items.Select(o => o.Amount).Should().Equal(1m, 1m);
        p1.Next.Should().NotBeNull("page 1 over-fetched and emits a cursor");

        // Page 2 seeks WHERE Amount > 1 — skips the entire Amount = 1 boundary.
        var page2 = await _context.Orders.ToPageAsync(new PageSize(2, 2), p1.Next, o => o.Amount, cancellationToken: ct);
        page2.TryGetValue(out var p2).Should().BeTrue();
        p2.Items.Select(o => o.Amount).Should().Equal(2m, 2m);

        // Documented contract: a non-unique key skips rows AT the boundary value.
        // Single-key seek pagination requires a UNIQUE stable key for full continuity;
        // callers with non-unique keys should use the composite (CreatedAt, Id) overload
        // (deferred to a follow-up PR) or extend the seek to a composite predicate.
    }
}