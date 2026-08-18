// Cookbook Recipe 3 — Query handler returning Page<T>.
namespace CookbookSnippets.Recipe03;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CookbookSnippets.Stubs;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Trellis;

public sealed record ListOrdersQuery(string? Cursor, int? Limit) : IQuery<Result<Page<OrderListItem>>>;

public sealed record OrderListItem(System.Guid Id, decimal Amount, string Currency);

public sealed class ListOrdersHandler(AppDbContext db)
    : IQueryHandler<ListOrdersQuery, Result<Page<OrderListItem>>>
{
    public async ValueTask<Result<Page<OrderListItem>>> Handle(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        var pageSize = PageSize.FromRequested(query.Limit);

        System.Guid? afterId = null;
        if (query.Cursor is { } cursorToken)
        {
            if (cursorToken.Length == 0)
                return Result.Fail<Page<OrderListItem>>(
                    Error.InvalidInput.ForField("cursor", "cursor.malformed", "Cursor must not be empty."));

            var decoded = CursorCodec.TryDecode<System.Guid>(new Cursor(cursorToken), fieldName: "cursor");
            if (decoded.IsFailure)
                return Result.Fail<Page<OrderListItem>>(decoded.Error!);
            decoded.TryGetValue(out var id);
            afterId = id;
        }

        var ordered = db.Orders.AsNoTracking().OrderBy(o => o.Id);
        var filtered = afterId is { } cursorId
            ? ordered.Where(o => o.Id.Value > cursorId)
            : (IQueryable<CookbookSnippets.Recipe01.Order>)ordered;

        var rows = await filtered.Take(pageSize.Applied + 1).ToListAsync(cancellationToken);

        return Result.Ok(
            PageBuilder.FromOverFetch(rows, pageSize, o => o.Id.Value)
                .Map(o => new OrderListItem(o.Id.Value, o.Total.Amount, o.Total.Currency.Value)));
    }
}
internal static class Recipe3PageSurface
{
    public static void Page_RecordStructSurface()
    {
        Page<OrderListItem> capped = new(
            Items: [],
            Next: null,
            Previous: null,
            RequestedLimit: 100,
            AppliedLimit: 25);

        bool wasCapped = capped.WasCapped;
        Page<OrderListItem> empty = Page.Empty<OrderListItem>(requestedLimit: 100, appliedLimit: 25);
        IReadOnlyList<OrderListItem> defaultItems = default(Page<OrderListItem>).Items;

        _ = (wasCapped, empty, defaultItems);
    }
}