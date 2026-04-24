// Cookbook Recipe 3 — Query handler returning Page<T>.
namespace CookbookSnippets.Recipe03;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Trellis;
using CookbookSnippets.Stubs;

public sealed record ListOrdersQuery(string? Cursor, int Limit) : IQuery<Result<Page<OrderListItem>>>;

public sealed record OrderListItem(System.Guid Id, decimal Amount, string Currency);

public sealed class ListOrdersHandler(AppDbContext db)
    : IQueryHandler<ListOrdersQuery, Result<Page<OrderListItem>>>
{
    private const int MaxLimit = 100;

    public async ValueTask<Result<Page<OrderListItem>>> Handle(ListOrdersQuery q, CancellationToken ct)
    {
        var requested = q.Limit;
        var applied   = System.Math.Clamp(requested, 1, MaxLimit);

        var query = db.Orders.AsNoTracking().OrderBy(o => o.Id);
        if (q.Cursor is not null)
            query = query.Where(o => o.Id.Value > System.Guid.Parse(q.Cursor)).OrderBy(o => o.Id);

        var rows = await query.Take(applied + 1).ToListAsync(ct);
        var hasNext = rows.Count > applied;
        var items   = rows.Take(applied)
                          .Select(o => new OrderListItem(o.Id.Value, o.Total.Amount, o.Total.Currency.Value))
                          .ToList();

        return Result.Ok(new Page<OrderListItem>(
            Items: items,
            Next: hasNext ? new Cursor(items[^1].Id.ToString("N")) : null,
            Previous: q.Cursor is null ? null : new Cursor(q.Cursor),
            RequestedLimit: requested,
            AppliedLimit: applied));
    }
}
