// Cookbook Recipe 3 - validated request controls and a single-source seek definition.
namespace CookbookSnippets.Recipe03;

using CookbookSnippets.Stubs;
using global::Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.Asp;
using Trellis.EntityFrameworkCore;

public sealed record ListOrdersQuery(PageRequest Pagination) : IQuery<Result<Page<OrderListItem>>>;

public sealed record OrderListItem(Guid Id, decimal Amount, string Currency);

public sealed class ListOrdersHandler(AppDbContext db)
    : IQueryHandler<ListOrdersQuery, Result<Page<OrderListItem>>>
{
    public async ValueTask<Result<Page<OrderListItem>>> Handle(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        var seek = SeekDefinition.Ascending<Recipe01.Order, Guid>(o => o.Id.Value);
        var page = await db.Orders.AsNoTracking()
            .ToPageAsync(query.Pagination, seek, cancellationToken: cancellationToken);
        return page.Map(o => o.Map(order =>
            new OrderListItem(order.Id.Value, order.Total.Amount, order.Total.Currency.Value)));
    }
}

internal static class Recipe3EndpointSurface
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders", (HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
            request.TryCreatePageRequest()
                .BindAsync(pagination => sender.Send(
                    new ListOrdersQuery(pagination),
                    cancellationToken))
                .ToHttpResponseAsync(
                    nextUrlBuilder: (cursor, applied) =>
                        $"/orders?cursor={Uri.EscapeDataString(cursor.Token)}&limit={applied}",
                    body: item => item))
            .WithInputOrigin(InputLocation.Query);
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
