---
title: Pagination
package: Trellis.Core
topics: [pagination, cursors, asp, results]
related_api_reference: [trellis-api-core.md, trellis-api-asp.md]
last_verified: 2026-05-22
audience: [developer]
---
# Pagination

Trellis ships cursor-based pagination as a first-class primitive. The data model
lives in `Trellis.Core` and is transport-agnostic; the HTTP projection lives in
`Trellis.Asp` and co-emits a JSON body envelope together with an RFC 8288 `Link`
header so both LLM consumers (which read the JSON they already parsed) and
RFC-aware crawlers / gateways (which follow the `Link: <…>; rel="next"` header)
are served from the same response.

This article walks through the building blocks, the canonical query-handler
shape, and the trade-offs the design makes deliberately.

## Why cursor-based, not offset-based?

Offset pagination (`?page=3&pageSize=25`) re-counts the source on every request
and breaks under inserts and deletes — the row that was at index 75 when the
client fetched page 3 is at a different index by the time the client asks for
page 4, producing skipped or duplicated items. Cursor-based pagination encodes a
*position* (an Id, or a `(CreatedAt, Id)` pair) into an opaque token that the
client echoes back. Each follow-up request is an O(log n) index seek, not an
O(offset) scan; results are stable across concurrent writes.

Trellis follows the body-envelope-with-cursor convention used by Microsoft
Graph, OData v4, GitHub, Stripe, and most other modern APIs.

## The building blocks

The storage-agnostic primitives live in the `Trellis` namespace under
`Trellis.Core`. The EF Core helper lives in `Trellis.EntityFrameworkCore`.

| Type | Package | Purpose |
| --- | --- | --- |
| `Cursor` | `Trellis.Core` | Opaque continuation token (a `readonly record struct` over a `string Token`). |
| `Page<T>` | `Trellis.Core` | Validated `readonly record struct` carrying `Items`, `Next`, `Previous`, `RequestedLimit`, `AppliedLimit`. |
| `PageSize` | `Trellis.Core` | Pair of `Requested` + `Applied` limits with `WasCapped`; canonical parser is `PageSize.FromRequested(int?, int max)`. |
| `CursorCodec` | `Trellis.Core` | Static encoder/decoder for single-key (`Id`) and composite (`CreatedAt, Id`) cursors. |
| `PageBuilder` | `Trellis.Core` | Storage-agnostic over-fetch slicer that turns `applied + 1` rows into a validated `Page<T>` with the correct `Next` cursor. |
| `Page<T>.Map<TOut>` | `Trellis.Core` | Instance method that projects items while preserving cursors and limits. |
| `IQueryable<T>.ToPageAsync` | `Trellis.EntityFrameworkCore` | EF Core extension that owns `OrderBy`, cursor decoding, the seek `WHERE`, the over-fetch, and the slice. Composes with `Page<T>.Map` for DTO projection. |

## The canonical query-handler shape

A query handler that takes a `Cursor?` and an `int?` limit, applies the server
cap, decodes the cursor, executes the seek query, slices the over-fetched list,
and projects to a DTO — all without leaking exceptions:

```csharp
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed record ListOrdersQuery(string? Cursor, int? Limit)
    : IQuery<Result<Page<OrderListItem>>>;

public sealed record OrderListItem(Guid Id, decimal Amount, string Currency);

public sealed class ListOrdersHandler(AppDbContext db)
    : IQueryHandler<ListOrdersQuery, Result<Page<OrderListItem>>>
{
    public async ValueTask<Result<Page<OrderListItem>>> Handle(
        ListOrdersQuery query, CancellationToken ct)
    {
        var pageSize = PageSize.FromRequested(query.Limit);
        var cursor = query.Cursor is { Length: > 0 } token ? new Cursor(token) : (Cursor?)null;

        var page = await db.Orders.AsNoTracking()
            .ToPageAsync(pageSize, cursor, o => o.Id.Value, cursorFieldName: "cursor", ct);

        return page.Map(p => p.Map(o =>
            new OrderListItem(o.Id.Value, o.Total.Amount, o.Total.Currency.Value)));
    }
}
```

`ToPageAsync` returns `Task<Result<Page<T>>>`; a malformed cursor surfaces as
`Result.Fail<Page<T>>(Error.InvalidInput.ForField("cursor", "cursor.malformed", …))`
without throwing. `Result<T>.Map(Func<T, TOut>)` then composes the
`Page<Order> → Page<OrderListItem>` projection without unwrapping the railway.

The endpoint then maps `Result<Page<T>>` to the HTTP wire:

```csharp
app.MapGet("/orders", async (
    string? cursor, int? limit, IMediator mediator, HttpContext http,
    LinkGenerator links, CancellationToken ct) =>
{
    return (await mediator.Send(new ListOrdersQuery(cursor, limit), ct))
        .ToHttpResponse(
            nextUrlBuilder: (c, applied) =>
                links.GetUriByName(http, "ListOrders",
                    values: new { cursor = c.Token, limit = applied })
                ?? throw new InvalidOperationException("Route 'ListOrders' not registered."),
            body: page => new { items = page.Items, next = page.Next, previous = page.Previous,
                                  requestedLimit = page.RequestedLimit, appliedLimit = page.AppliedLimit,
                                  deliveredCount = page.DeliveredCount, wasCapped = page.WasCapped });
}).WithName("ListOrders");
```

`Trellis.Asp` co-emits the `Link: <…>; rel="next"` header automatically from the
`nextUrlBuilder` delegate, so both the JSON body and the header are sourced from
a single function — there is no way for the two to drift.

## `PageSize.FromRequested` — lenient by default, strict on demand

Pagination parameters arrive at the transport seam; they are not domain
invariants. `PageSize.FromRequested` is the lenient parser used in the example
above:

* `null` or a non-positive value collapses to `Requested = PageSize.Default`
  (50). `Applied` is `min(Default, max)`, so when a custom `max < Default` is
  supplied, `Applied` is clamped down further and `WasCapped` is observable.
* Values larger than `max` (defaults to `PageSize.Max` = 100) are clamped:
  `Applied` becomes `max`, but `Requested` is preserved **verbatim** so the
  caller's `WasCapped` observation survives the round-trip.

```csharp
PageSize.FromRequested(null);            // (Default, Default)  — WasCapped = false
PageSize.FromRequested(null, max: 5);    // (Default, 5)        — WasCapped = true
PageSize.FromRequested(0);               // (Default, Default)  — WasCapped = false
PageSize.FromRequested(20);              // (20, 20)            — WasCapped = false
PageSize.FromRequested(1000);            // (1000, 100)         — WasCapped = true
PageSize.FromRequested(20, max: 5);      // (20, 5)             — WasCapped = true
```

When a request must be **rejected** rather than silently clamped, use
`PageSize.TryCreate`:

```csharp
PageSize.TryCreate(1000)
    .Match(
        onSuccess: size => /* ... */,
        onFailure: err => Result.Fail<Page<Order>>(err));
```

`TryCreate` returns `Result.Fail<PageSize>` with `Error.InvalidInput` (field =
`fieldName ?? "pageSize"`, reason code `"page-size.out-of-range"`) on any
non-positive value or any value greater than `max`. Pick whichever fits your
contract; both compose cleanly with `Page<T>`.

## Cursors — opaque by design, not anti-tamper

`Cursor` is just a wrapper over a string token. The encoding format is
`CursorCodec`'s business: clients must treat the token as opaque and never
parse it. The codec itself is part of the public API so services can wrap or
replace it (for example, to add HMAC signing) without changing the wire
shape clients echo back:

* **Single-key:** URL-safe base64 of the key's invariant-culture string form.
* **Composite:** URL-safe base64 of `"{createdAt:O}|{id}"` in invariant culture.
  Decoding splits at the **first** `|`, so an Id that happens to contain a pipe
  remains unambiguous.

The codec is AOT-friendly: no JSON, no reflection, no `Expression.Compile`. It
uses `Convert.ToBase64String`, URL-safe substitution (`+`→`-`, `/`→`_`, drop
`=` padding), and `IParsable<TKey>.TryParse` with `CultureInfo.InvariantCulture`.

**Cursors are opaque so that clients don't reverse-engineer the sort key, but
the encoding is not signed.** Services that need to defend against tampering
must wrap or replace the codec with a signed variant; authorization filtering
must always apply to the underlying query.

### Trellis value-object IDs

The generic constraint accepts any `notnull` key, and decoding requires
`IParsable<TKey>`. Trellis value-object IDs (e.g. `OrderId : RequiredGuid<OrderId>`)
do not directly satisfy `IParsable<TSelf>` for their underlying primitive, so
the canonical pattern is to project to `.Value` at the boundary:

```csharp
// encode
CursorCodec.Encode(order.Id.Value)            // .Value : Guid

// decode (and rewrap)
var decoded = CursorCodec.TryDecode<Guid>(cursor);
return decoded.Bind(g =>
    OrderId.TryCreate(g)
        .MapOnFailure(_ => Error.InvalidInput.ForField("cursor", "cursor.malformed",
                                                    "Cursor payload is not a valid OrderId.")));
```

This keeps the wire format tight (raw Guid string) and the domain type-safe
(`OrderId` everywhere on the inside).

## `PageBuilder.FromOverFetch` — the over-fetch idiom

The "ask for one more than you need" idiom is how seek-style pagination
discovers whether another page exists:

```text
applied = 25
fetched = 26    →  page has Next     (kept = first 25; cursor from items[24])
fetched = 25    →  page has no Next  (under-fill; this is the last page)
fetched = 0     →  empty page        (under-fill; both cursors null)
```

`PageBuilder.FromOverFetch` encodes that logic once. EF Core consumers should
reach for `IQueryable<T>.ToPageAsync` (in `Trellis.EntityFrameworkCore`) which
wraps the over-fetch, the seek predicate, and the slice in one call. If you
must work at the lower layer (a non-EF store, or a manual seek shape), the
caller's responsibility is to fetch `pageSize.Applied + 1` rows ordered by the
same key the selector returns:

```csharp
var ordered = db.Orders.AsNoTracking().OrderBy(o => o.Amount);
var filtered = afterAmount is { } cursorAmount
    ? ordered.Where(o => o.Amount > cursorAmount)
    : (IQueryable<Order>)ordered;

var rows = await filtered.Take(pageSize.Applied + 1).ToListAsync(ct);

var page = PageBuilder.FromOverFetch(rows, pageSize, o => o.Amount);
```

> **Guid / string keys.** `Guid` and `string` do not expose a C# `>` operator,
> so a hand-rolled `Where(o => o.Id.Value > cursorId)` will not compile. Use
> `ToPageAsync` (which routes through `IComparable<TKey>.CompareTo` for those
> types and emits provider-translated SQL), or write the predicate as
> `Where(o => o.Id.Value.CompareTo(cursorId) > 0)` in the manual shape.

For stable time-ordered seek (rows with non-unique primary sort keys, e.g.
events at the same millisecond), use the composite overload:

```csharp
var rows = await db.Events.AsNoTracking()
    .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
    .Where(e => /* cursor predicate on (CreatedAt, Id) */)
    .Take(pageSize.Applied + 1)
    .ToListAsync(ct);

var page = PageBuilder.FromOverFetch(rows, pageSize,
    createdAtSelector: e => e.CreatedAt,
    idSelector: e => e.Id.Value);
```

> **Selector contract.** The selectors passed to `FromOverFetch` must match the
> sort keys used in the upstream query. Mismatched selectors produce
> semantically wrong cursors — the boundary item the cursor points at will not
> be the one the next query would seek past.

### Forward-only by design

`Page<T>.Previous` is always `null` from `PageBuilder`. Trellis does not yet
ship a reverse-seek API, and re-running the `nextUrlBuilder` against an echoed
"incoming" cursor would walk **forward** from that point — re-fetching the same
page rather than the page before it. Until a real reverse-seek API exists,
forward-only is the only correct behavior. Clients that need to go back navigate
through their own request history.

## `Page<T>.Map` — projecting to DTOs

Repositories typically yield `Page<Entity>`; HTTP wire types are usually DTOs.
`Page<T>.Map<TOut>` projects each item and preserves the cursors and limits in
one call:

```csharp
Page<Order> entities = await repo.ListAsync(pageSize, cursor, ct);
Page<OrderDto> response = entities.Map(o => new OrderDto(o.Id.Value, o.Total));
```

`Next`, `Previous`, `RequestedLimit`, `AppliedLimit`, and therefore `WasCapped`
all survive the projection. Use it freely at the application/transport boundary.

## ROP, not exceptions — what fails how

| Failure | Result | Wire |
| --- | --- | --- |
| Malformed cursor token (bad base64, bad payload) | `Error.InvalidInput.ForField("cursor", "cursor.malformed", …)` | `422 Unprocessable Content` |
| Out-of-range `limit` via `PageSize.TryCreate` | `Error.InvalidInput.ForField("pageSize", "page-size.out-of-range", …)` | `422 Unprocessable Content` |
| Storage/timeout/connectivity | Whatever your repository chooses to surface (often `Error.Unavailable`) | `503` / `500` per `Trellis.Asp` mapping |
| Success | `Result.Ok(page)` | `200 OK` + body envelope + `Link` header |

No throw on any cursor or limit input — every failure surfaces as
`Result.Fail<Page<T>>` and is mapped to a Problem Details response by
`Trellis.Asp`. There is no path from a malformed query string to a 500.

## Anti-patterns to avoid

* **Hand-rolling base64 + JSON cursors.** `CursorCodec` already exists and is
  AOT-safe; rolling your own duplicates the codec, drifts on culture handling,
  and makes mistakes around `+`/`-` / `/`/`_` substitution and padding. Use the
  codec; if you need signing, wrap it.
* **Throwing on a bad cursor.** A malformed cursor is invalid client input, not
  an exceptional condition. Throw a 500 and you've lost the audit trail and the
  Problem Details body. Always return `Result.Fail<Page<T>>`.
* **Returning items larger than the cap without `WasCapped`.** Clients can't
  tell when the server clamped if `Requested` is rewritten to `Applied`. Use
  `PageSize.FromRequested` (which preserves `Requested` verbatim) so the cap is
  observable.
* **`Items.Take(applied)` after the fact.** That works but loses the chance to
  detect under-fill cleanly. `PageBuilder.FromOverFetch` separates the
  "over-fetch + slice" concern from the rest of the handler.

## See also

* API reference: [Trellis.Core pagination types](../api_reference/trellis-api-core.md#pagination)
* API reference: [`PaginationQueryableExtensions.ToPageAsync` (Trellis.EntityFrameworkCore)](../api_reference/trellis-api-efcore.md#paginationqueryableextensions)
* Cookbook: [Recipe 3 — Query handler returning Page<T>](../api_reference/trellis-api-cookbook.md#recipe-3--query-handler-returning-paget-paginated-list-with-cursor)
* EF Core provider quirks: [Provider-specific column mapping](../api_reference/trellis-api-efcore.md#provider-specific-column-mapping)
