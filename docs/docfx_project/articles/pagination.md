---
title: Pagination
package: Trellis.Core
topics: [pagination, cursors, efcore, asp, results]
related_api_reference: [trellis-api-core.md, trellis-api-efcore.md, trellis-api-asp.md]
last_verified: 2026-09-12
audience: [developer]
---
# Pagination

Trellis separates pagination into three responsibilities:

1. **Validate request controls** with `PageRequest.TryCreate`.
2. **Continue the query** using typed state: an EF `SeekDefinition`, an application-owned algorithm, or a provider's own continuation token.
3. **Assemble and project the result** with `PageBuilder`, `Page<T>`, and `Page<T>.Map`.

Core has no storage or HTTP dependency. `Trellis.EntityFrameworkCore` supplies
provider-translatable seek pagination. `Trellis.Asp` maps `Result<Page<T>>` to
`200 OK`, a JSON envelope, and an RFC 8288 `Link` header. Collection pagination
does not use `206 Partial Content`, which belongs to byte-range transfer.

## Why use a cursor?

Offset pagination can skip or repeat rows when concurrent changes shift their
positions. A seek cursor instead identifies a boundary in a deterministic
ordering, such as `(CreatedAt descending, Id ascending)`. With suitable indexes,
the database can avoid scanning a growing offset.

This is not a universal performance or consistency guarantee. Provider support,
indexes, filters, and comparison semantics determine the query plan. Mutable
sort values and concurrent writes can still change results between pages.
Trellis does not create a snapshot.

## Building blocks

| Type | Package | Responsibility |
| --- | --- | --- |
| `Cursor` | Core | Opaque token; clients echo `Token` unchanged. |
| `PageSize` | Core | Validated `Requested`, `Applied`, and `WasCapped`. |
| `PageRequest` | Core | Validates raw cursor/limit; `Decode(codec)` produces `Result<Maybe<TState>>`. |
| `ICursorCodec<TState>` / `CursorCodec` | Core | Typed encoding, parsing, and validation of continuation state. |
| `PageBuilder` | Core | Pure assembly from an already ordered, sought, over-fetched batch. |
| `Page<T>` | Core | Immutable item sequence, adjacent cursors, and observable limit metadata; `Map` preserves that metadata. |
| `SeekDefinition<T,TState>` | EF Core | Owns ordering, state extraction, and the matching lexicographic seek predicate together. |
| `ToPageAsync` | EF Core | Decode, seek, over-fetch, and assemble a forward page. |

`Cursor`, `PageSize`, `PageRequest`, and `Page<T>` are **sealed record classes**.
Their default value is null, not an empty valid struct. Use `Page.Empty<T>(...)`
for empty results and null only for an absent cursor.

## Validate raw input once

Use the non-throwing factory at an untrusted boundary:

```csharp
Result<PageRequest> request = PageRequest.TryCreate(
    cursor, limit, max: 100, defaultSize: 50,
    policy: PageSizeLimitPolicy.Clamp,
    cursorFieldName: "cursor", limitFieldName: "limit");
```

| Input | Outcome |
| --- | --- |
| Missing cursor (`null`) | First page: no continuation boundary. |
| Empty or whitespace-only cursor | Failed result, reason `cursor.malformed`. Never silently restarts at page one. |
| Missing limit | Uses `defaultSize`, capped to `max` if necessary. |
| Zero or negative limit | Failed result, reason `page-size.out-of-range`. |
| Explicit limit above `max`, `Clamp` policy | Keeps the original requested limit; caps the applied limit. |
| Explicit limit above `max`, `Reject` policy | Failed result, reason `page-size.out-of-range`. |

`PageRequest` defaults field names to `"cursor"` and `"limit"`. A custom cursor
field name must also be passed to `Decode` / `ToPageAsync`; it is not stored in
the request.

Preserve the distinction at the transport boundary too. MVC string binding can
convert a present-but-empty query value to null before the factory sees it.
When binding cursor input, preserve the raw query value (for example through
`Request.Query`) and pass null only when the parameter is actually absent.
Otherwise `?cursor=` can accidentally become a first-page request.

`PageSize.TryCreate(limit, ...)` offers the same policy when only a size is
needed (its default field name is `"pageSize"`). `PageSize.FromRequested`
is a **trusted, throwing convenience**, not a lenient query-string parser:
non-positive input throws. Missing input defaults; above-cap input clamps.

Bad server configuration also throws: `max` must be positive and at most
`PageSize.MaxApplied` (`int.MaxValue - 1`), `defaultSize` must be positive,
and the policy must be defined. That bound makes `Applied + 1` safe.

## EF Core: keep ordering and seek together

The source must already carry the application's authorization, tenant, and
business filters. This function assumes `authorizedOrders` is appropriately scoped:

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

static Task<Result<Page<Order>>> ListOrders(
    IQueryable<Order> authorizedOrders, string? cursor, int? limit,
    CancellationToken ct)
{
    var seek = SeekDefinition.Descending<Order, DateTimeOffset>(o => o.CreatedAt)
        .ThenAscending(o => o.Id.Value);

    return PageRequest.TryCreate(cursor, limit)
        .BindAsync(request => authorizedOrders.AsNoTracking()
            .ToPageAsync(request, seek, cursorFieldName: "cursor",
                cancellationToken: ct));
}
```

`BindAsync` prevents database execution when request validation fails. The
helper decodes through `seek.Codec`, applies the matching order and boundary,
fetches `Applied + 1` rows, and emits a next cursor from the last retained row
only when another row exists.

Boundary values are projected alongside the rows in the database query using
the same key expressions as ordering and seeking. The helper does not compile
selectors or recompute their values after materialization. Provider-translated
functions such as `EF.Functions.Collate` can therefore participate when the
provider translates the complete query; equivalent-looking C# recomputation
is not assumed to match SQL semantics.

For descending time / ascending ID, the predicate is equivalent to:

```text
time < boundaryTime OR (time == boundaryTime AND id > boundaryId)
```

Use `ThenAscending` / `ThenDescending` for additional keys, ending with a
**stable unique tie-breaker**. Tuple state nests as keys are added: `(time, id)`,
then `((time, id), thirdKey)`. Null keys are unsupported. Do not add a competing
`OrderBy` upstream; the definition owns the complete ordering.

The single-key convenience remains:

```csharp
await authorizedOrders.ToPageAsync(
    pageSize, cursor, o => o.Id.Value, cancellationToken: ct);
```

Here `cursor` is already a `Cursor?` and `pageSize` is validated. This overload
wraps a single ascending definition, so the key must itself be unique.

### Provider comparison is part of correctness

Numeric/date-like keys use relational expressions; other comparable keys,
including GUID and string, use `CompareTo`. Verify provider translation and
agreement between `ORDER BY`, equality, and the seek comparison. Database
collations and GUID ordering need not match .NET in-memory ordering.

There is **no automatic client-side fallback**. A value converter alone cannot
guarantee translation of an arbitrary comparison method. Projections through
Trellis value objects (`o.Id.Value`) require `AddTrellisInterceptors()` on the
context options. Verify boundary projection as well as ordering and seek
translation when using provider-specific expressions.

`Ascending` / `Descending` and their `Then` counterparts accept an explicit
key codec when the scalar codec is unsuitable. `seek.WithCodec(codec)` replaces
the complete state codec without changing ordering or extraction; use it for
context binding or a caller-owned protection wrapper.

## Typed codecs: opaque, versioned, not signed

```csharp
ICursorCodec<Guid> ids = CursorCodec.Scalar<Guid>();
ICursorCodec<(DateTimeOffset Primary, Guid Secondary)> events =
    CursorCodec.Composite<DateTimeOffset, Guid>();

Cursor token = events.Encode((createdAt, id));
Result<(DateTimeOffset Primary, Guid Secondary)> decoded = events.TryDecode(token);
```

Scalar codecs require `IParsable<T>` plus invariant `IFormattable` support (or
`string`); unsupported formatting fails at factory creation. Generated scalar
IDs satisfying both interfaces can be used directly. For other value objects,
project their primitive or provide an explicit codec.

Built-in frames are URL-safe base64 of `1:s:...` (scalar), `1:c:...`
(composite), or `1:x-<schema>:...` (explicit codec). Composites length-prefix
the first component token and can nest arbitrary codecs without delimiter
ambiguity. The entire encoded token is limited to 1,024 characters on **both**
encoding and decoding. **Old unversioned tokens are deliberately rejected.**

`CursorCodec.Create(schema, format, parse)` supplies an explicit serializer
and validating parser. `CursorCodec.Map(wireCodec, toWire, fromWire)` instead
maps a composed wire representation to named validated state. There is no
implicit JSON fallback. Encode validates parsing and state equality, so a lossy
formatter or invalid server boundary throws rather than issuing an unusable
cursor. Floating-point state must be finite; dates use round-trip `"O"` format
to preserve ticks and kind/offset.

Built-in tokens are not encrypted or signed. Bind tenant/filter/origin/sort/
algorithm context in the application codec when tokens must not be reused
across different queries. A context hash is not authentication; use a
caller-owned `ICursorCodec<TState>` protection wrapper for anti-tamper needs.
Authorization must still filter every request.

## Computed distance or score

For a computed order that cannot be translated, the application owns the
algorithm and its candidate bounds. Use:

1. An authorized, appropriately bounded candidate source.
2. `PageRequest.Decode(codec)` to validate optional typed continuation state.
3. A deterministic computed value plus a stable unique tie-breaker.
4. The matching boundary predicate **before** `Take(Applied + 1)`.
5. `PageBuilder.FromOverFetch` with a callback that encodes the last retained boundary.

The [computed pagination recipe](../api_reference/trellis-api-cookbook.md#recipe-40--computed-pagination-with-validated-query-bound-continuation-state)
shows finite, nonnegative distance state mapped from nested composite codecs,
canonical query-context binding, and an in-memory GUID tie-breaker using
`CompareTo`. It deliberately uses a complete bounded snapshot; it is not
geospatial support or a reason to materialize an unbounded database table.

## Pure page assembly and provider tokens

`PageBuilder` has only one overload; the callback returns a `Cursor`, not a key:

```csharp
var codec = CursorCodec.Composite<DateTimeOffset, Guid>();
var page = PageBuilder.FromOverFetch(orderedAndSoughtRows, pageSize,
    last => codec.Encode((last.CreatedAt, last.Id)));
```

The caller has already sought and ordered the rows using those same keys.
For an applied size of 25, 26 fetched rows means 25 retained rows and a next
cursor from row 25; exactly 25, fewer rows, or no rows means no next cursor.
The callback runs exactly once if there is more data, otherwise never.

For stores that supply opaque continuation tokens, construct `Page<T>` directly:

```csharp
var page = new Page<Order>(
    providerItems,
    providerNextToken is null ? null : new Cursor(providerNextToken),
    providerPreviousToken is null ? null : new Cursor(providerPreviousToken),
    pageSize.Requested, pageSize.Applied);
```

Do not decode provider tokens with `CursorCodec` or infer completion solely
from item count. Some providers return an empty batch with a continuation.
The page constructor snapshots the item sequence; value equality includes
sequence contents, both cursors, and both limits.

`Page<T>.Map` preserves both cursors and limits while projecting items:

```csharp
Result<Page<OrderListItem>> response = result.Map(page => page.Map(order =>
    new OrderListItem(order.Id.Value, order.Total.Amount, order.Total.Currency.Value)));
```

The EF helper and `PageBuilder` are forward-only (`Previous` is null).
Descending ordering does not implement previous-page navigation. A direct
`Page<T>` can carry a provider-supplied previous token when the provider really
supports that operation.

## What fails how?

| Failure | Behavior |
| --- | --- |
| Missing/empty distinction, invalid raw limit | `PageRequest.TryCreate` returns a field-specific `Error.InvalidInput`. |
| Bad token version/base64/UTF-8/framing, oversized or invalid state | Built-in codecs return `Error.InvalidInput` with reason `cursor.malformed`. |
| Invalid server size configuration, null required arguments | Throws: a configuration/programming error, not invalid client input. |
| Encoding invalid or non-round-tripping server state | Throws `ArgumentException`. |
| A custom codec/parser returns successful null continuation state | Throws `InvalidOperationException`, including through `CursorCodec.TryDecodeOptional` / `PageRequest.Decode`; never restarts at the first page. |
| Cancellation, query translation, database/connection failures | Propagate; pagination does not relabel infrastructure faults as malformed cursors. |

Expected invalid input maps to HTTP 422 through Trellis ASP mapping; successful
pages map to 200. See the [ASP reference](../api_reference/trellis-api-asp.md#use-this-file-when)
for response-envelope and link-builder overloads. Do not serialize a raw
`Result<Page<T>>` directly.

## Upgrading existing pagination code

- Replace manual string-to-cursor parsing and lenient limit handling with
  `PageRequest.TryCreate`; do not treat empty cursors or non-positive limits as absence.
- Replace raw-key / timestamp-selector `PageBuilder` lambdas with
  `last => codec.Encode(state)`; there is no key-selector overload.
- Replace separately maintained ordering and seek predicates with an EF
  `SeekDefinition` where translation is supported.
- Treat cursors as nullable references, not nullable structs (`cursor.Token`,
  not `cursor.Value.Token` after a presence check).
- Expect outstanding old unversioned tokens to fail validation; clients must
  restart pagination. No legacy migration decoder is shipped.

## See also

- [Core pagination API](../api_reference/trellis-api-core.md#pagination)
- [EF pagination API](../api_reference/trellis-api-efcore.md#paginationqueryableextensions)
- [Paginated query-handler recipe](../api_reference/trellis-api-cookbook.md#recipe-3--query-handler-returning-paget-paginated-list-with-cursor)
- [Computed pagination recipe](../api_reference/trellis-api-cookbook.md#recipe-40--computed-pagination-with-validated-query-bound-continuation-state)
