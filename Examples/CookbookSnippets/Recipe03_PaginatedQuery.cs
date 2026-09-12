// Cookbook Recipe 3 - validated request controls and a single-source seek definition.
namespace CookbookSnippets.Recipe03;

using CookbookSnippets.Stubs;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed record ListOrdersQuery(string? Cursor, int? Limit) : IQuery<Result<Page<OrderListItem>>>;

public sealed record OrderListItem(Guid Id, decimal Amount, string Currency);

public sealed class ListOrdersHandler(AppDbContext db)
    : IQueryHandler<ListOrdersQuery, Result<Page<OrderListItem>>>
{
    public async ValueTask<Result<Page<OrderListItem>>> Handle(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        var seek = SeekDefinition.Ascending<Recipe01.Order, Guid>(o => o.Id.Value);
        return await PageRequest.TryCreate(query.Cursor, query.Limit)
            .BindAsync(request => db.Orders.AsNoTracking().ToPageAsync(request, seek, cancellationToken: cancellationToken))
            .MapAsync(page => page.Map(o => new OrderListItem(o.Id.Value, o.Total.Amount, o.Total.Currency.Value)));
    }
}

internal static class Recipe3PageSurface
{
    public static void Page_ReferenceSurface()
    {
        Page<OrderListItem> capped = new([], null, null, 100, 25);
        Page<OrderListItem> empty = Page.Empty<OrderListItem>(100, 25);
        _ = (capped.WasCapped, empty);
    }
}
