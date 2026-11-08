---
name: recipe-03-paginated-query
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows a query handler that returns Page with a cursor. Use when adding a paginated list query: PageRequest, bound cursor and limit inputs, ordering, seeking before Take."
---
# Recipe 3 — Query handler returning `Page<T>` (paginated list with cursor)

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.EntityFrameworkCore`, `Trellis.Mediator`.

## Contents

- Problem
- What it shows
- Failure and consistency boundaries

**Problem.** Expose a list endpoint that paginates `Order` rows by cursor, exposes the requested vs. applied limit, and projects a DTO.

```csharp
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.Asp;
using Trellis.EntityFrameworkCore;

public sealed record ListOrdersQuery(PageRequest Pagination) : IQuery<Result<Page<OrderListItem>>>;

public sealed record OrderListItem(Guid Id, decimal Amount, string Currency);

public sealed class ListOrdersHandler(AppDbContext db)
    : IQueryHandler<ListOrdersQuery, Result<Page<OrderListItem>>>
{
    public async ValueTask<Result<Page<OrderListItem>>> Handle(ListOrdersQuery q, CancellationToken ct)
    {
        var seek = SeekDefinition.Ascending<Order, Guid>(o => o.Id.Value);
        var page = await db.Orders.AsNoTracking()
            .ToPageAsync(q.Pagination, seek, cursorFieldName: "cursor",
                cancellationToken: ct);

        return page.Map(p => p.Map(o =>
            new OrderListItem(o.Id.Value, o.Total.Amount, o.Total.Currency.Value)));
    }
}
```

At an ASP.NET Core boundary, construct the validated request from raw query values before
dispatching the application query:

```csharp
app.MapGet("/orders", (HttpContext context, ISender sender, CancellationToken ct) =>
    context.Request.TryCreatePageRequest()
        .BindAsync(pagination => sender.Send(new ListOrdersQuery(pagination), ct))
        .ToHttpResponseAsync(
            nextUrlBuilder: context.PageUrl(
                "Orders_List",
                (cursor, applied) => new Microsoft.AspNetCore.Routing.RouteValueDictionary
                {
                    ["cursor"] = cursor.Token,
                    ["limit"] = applied,
                }),
            body: item => item))
    .WithName("Orders_List")
    .WithInputOrigin(InputLocation.Query);
```

**What it shows.** `HttpRequest.TryCreatePageRequest` is the ASP untrusted-input boundary: only
a missing cursor means first page; present empty/whitespace and repeated cursors fail with
`cursor.malformed`. A missing limit uses `PageSize.Default`; empty, malformed, overflowing, or
repeated values fail with `format.integer`; zero/negative limits fail; above-cap limits clamp
while retaining `Requested` (or fail with `policy: PageSizeLimitPolicy.Reject`). Every parser
failure is located at the actual query parameter and maps to HTTP 422. `BindAsync` does not
dispatch the query on parse failure, so no database query runs. The endpoint's query input-origin
metadata also promotes a later, transport-neutral `/cursor` decode failure to the `cursor` query
parameter.

`HttpContext.PageUrl(...)` belongs to
[`Trellis.Asp`](../trellis-api-asp.md#httpcontextpaginationextensions), so the same endpoint
expression works without a versioning package. In versioned hosts, enable
[`UseVersionedPageUrls`](../trellis-api-asp-apiversioning.md#trellisaspoptionsapiversioningextensions)
once through `AddTrellisAsp` / `UseAsp` as in [Recipe 12](recipe-12-di-wiring.md). Identically routed namespace-versioned
list actions may share a name: the enabled policy resolves self-pagination against the active
endpoint, preserving next/previous versions independently of registration order.

The handler receives the validated, transport-neutral `PageRequest`. A non-HTTP adapter constructs
the same type with `PageRequest.TryCreate(rawCursor, rawLimit)` before creating
the application query. `SeekDefinition` owns ordering, extraction, and the matching predicate;
`ToPageAsync` decodes typed state, seeks, over-fetches, and delegates pure assembly to
`PageBuilder`. The final `Map` preserves both the railway and the page metadata.

Do not replace the raw parser with bound `string? cursor` / `int? limit` endpoint parameters:
MVC can normalize `?cursor=` to null, and host parsing can return 400 before Trellis produces its
coded 422. `TryCreatePageRequest` deliberately adds no ApiExplorer/OpenAPI parameters; declare
the two query parameters separately in endpoint metadata when documentation is required.

For non-unique primary sorts, change the definition rather than hand-writing a second predicate:

```csharp
var seek = SeekDefinition.Descending<Order, DateTimeOffset>(o => o.CreatedAt)
    .ThenAscending(o => o.Id.Value);
```

End with a stable unique key. Every key must be non-null and provider-comparable; verify translation/collation semantics. `.Id.Value` projection requires `AddTrellisInterceptors()` on the context options. Apply tenant/authorization/business filters before `ToPageAsync`; the sample assumes the order source is already appropriately scoped.

EF projects boundary values alongside each row using the same expressions as ordering and seeking; the cursor is encoded from that projected state, not from selectors compiled or re-evaluated after materialization. Provider-translated functions such as `EF.Functions.Collate` are usable when the provider translates the complete query. Do not substitute an assumed-equivalent C# calculation for a SQL boundary value.

**Failure and consistency boundaries.** Malformed state returns `Error.InvalidInput` with `cursor.malformed` (HTTP 422 through ASP mapping). Invalid server state, provider failures, and cancellation propagate rather than becoming cursor errors. Built-in tokens are versioned and unsigned; old unversioned tokens are rejected. `Previous` is null from the EF helper, and descending traversal is not reverse pagination. There is no snapshot guarantee across mutable data. Use [Recipe 40](recipe-40-computed-pagination.md) for query-bound computed state; see the [Core pagination reference](../trellis-api-core.md#pagination) and [EF seek contract](../trellis-api-efcore.md#seekdefinition).
