// Cookbook Recipe 3 — Query handler returning Page<T>.
namespace CookbookSnippets.Recipe03;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CookbookSnippets.Stubs;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Trellis;

public sealed record ListOrdersQuery(string? Cursor, int Limit) : IQuery<Result<Page<OrderListItem>>>;

public sealed record OrderListItem(System.Guid Id, decimal Amount, string Currency);

public sealed class ListOrdersHandler(AppDbContext db)
    : IQueryHandler<ListOrdersQuery, Result<Page<OrderListItem>>>
{
    private const int MaxLimit = 100;

    public async ValueTask<Result<Page<OrderListItem>>> Handle(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        var requested = query.Limit;
        var applied = System.Math.Clamp(requested, 1, MaxLimit);

        var orders = db.Orders.AsNoTracking().OrderBy(o => o.Id);
        if (query.Cursor is not null)
        {
            if (!System.Guid.TryParseExact(query.Cursor, "N", out var cursorId))
                return Result.Fail<Page<OrderListItem>>(
                    Error.UnprocessableContent.ForField(nameof(query.Cursor), "Cursor is not a valid pagination token."));

            orders = orders.Where(o => o.Id.Value > cursorId).OrderBy(o => o.Id);
        }

        var rows = await orders.Take(applied + 1).ToListAsync(cancellationToken);
        var hasNext = rows.Count > applied;
        var items = rows.Take(applied)
                          .Select(o => new OrderListItem(o.Id.Value, o.Total.Amount, o.Total.Currency.Value))
                          .ToList();

        return Result.Ok(new Page<OrderListItem>(
            Items: items,
            Next: hasNext ? new Cursor(items[^1].Id.ToString("N")) : null,
            Previous: query.Cursor is null ? null : new Cursor(query.Cursor),
            RequestedLimit: requested,
            AppliedLimit: applied));
    }
}