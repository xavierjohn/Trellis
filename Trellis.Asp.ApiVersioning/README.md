# Trellis.Asp.ApiVersioning

API-versioning helpers for [Trellis.Asp](../Trellis.Asp/README.md). Generate Location URLs using the actual destination's version mappings, including query/header and URL-segment versioning, without hard-coding version literals.

- **`HttpResponseOptionsBuilder<T>.WithVersionedRoute(...)`** — chains after `CreatedAtRoute(...)` / `CreatedAtAction(...)` (201 Created) or `WithLocation(...)` (200 OK on existing resources) to version the `Location` header.
- **`HttpContext.PageUrl(routeName, ...)`** — returns a `Func<Cursor, int, string>` for the `nextUrlBuilder` parameter of `ToHttpResponse(Async)` on `Result<Page<T>>`, so paginated `next` URLs carry the version, honor URL-segment ambient route values, and skip injection on neutral, URL-segment-versioned, and unversioned-host endpoints — no manual URL encoding.

## Why this package exists

Under query-string or header API versioning (`Asp.Versioning.QueryStringApiVersionReader`, `HeaderApiVersionReader`, or composite readers), `Location` headers from `HttpResponseOptionsBuilder<T>.CreatedAtRoute(routeName, routeValues)` silently omit the `api-version` parameter unless every author remembers to add it manually. The result: a `POST /api/orders?api-version=2026-12-01` returns `Location: /api/orders/42` (no version), and the follow-up `GET /api/orders/42` 404s because the controller is registered under the versioned route group.

The bug is invisible without integration tests that assert on the full Location URL, easy to miss in code review, and silently regresses across cycles. `.WithVersionedRoute()` removes the trap by injecting the version at request time using the configured `IApiVersionReader` chain.

## Resolution order

Location resolution runs at link generation against the **final named-route or MVC-action destination**, not the current request endpoint. Public ASP.NET Core endpoint-address schemes select the target; suppressed link-generation endpoints are excluded. Zero or multiple candidates throw `InvalidOperationException`; use a uniquely named destination route to remove ambiguity. Configuration order does not affect the target.

Both Location and `PageUrl` use these shared version-support checks:

1. **`HttpContext.RequestedApiVersion`** — use it only if the target's `metadata.IsMappedTo(version)` is true.
2. **Exactly one mapped declared version** — use `metadata.Map(ApiVersionMapping.Implicit | ApiVersionMapping.Explicit).DeclaredApiVersions`, filtered by `IsMappedTo`.
3. **`ApiVersioningOptions.DefaultApiVersion`** — use the configured default only if mapped to the target; otherwise throw `InvalidOperationException`.

Action-level `[MapToApiVersion]` **narrows** accepted controller versions. Controller declarations alone do not establish action support; do not union implicit and explicit declarations. Explicit pins must also map to the target.

For Location:

- Query-style targets receive conventional `api-version`; segment targets receive the resolved/pinned value in the **actual** `:apiVersion` parameter (e.g. `revision` in `v{revision:apiVersion}`), with no duplicate `api-version` query entry. Cross-route segments and explicit segment pins are supported.
- Neutral or missing-metadata targets skip injection and remove any supplied `api-version` from the cloned dictionary. Missing metadata warns once per **destination endpoint / AppDomain**, identifying the target, under `Trellis.Asp.ApiVersioning`; `TrellisAspOptions.FailFastOnSilentVersionInjection = true` throws on every offending execution. Neutral targets stay quiet.
- Warning deduplication uses weak endpoint-instance identity: same-named destinations in different hosts warn independently, while concurrent calls for one endpoint warn only once. Discarded endpoints are not kept alive by the diagnostic.
- `WithVersionedRoute` uses ASP's `WithLocationRouteResolver` callback after the domain selector and **all** legacy `WithRouteValueResolver` callbacks, overriding supplied version values. There is one callback slot: the last registration wins, including repeated `WithVersionedRoute` calls. Callback errors propagate; shared selector dictionaries are not mutated.
- Literal/selector `Created(...)` and `WriteOutcome`-owned URIs are unchanged. Named routes remain AOT-compatible; `CreatedAtAction` retains its trimming/AOT limitations.

## Migration

Existing `.WithVersionedRoute()` / `.WithVersionedRoute(ApiVersion)` syntax is retained, but runtime checks are stricter: missing/ambiguous targets and unsupported pins now fail, segment pins are honored, neutral/unversioned targets lose supplied `api-version` values, and warning identity changes from caller to destination. Test dereferencing each Location; uniquely name ambiguous destinations. No new service registration is needed.

Only shared mapping checks change for `PageUrl`: its implicit ambient segment routing and cross-route validation, consumer overrides, explicit segment-pin rejection, and quiet missing-metadata behavior remain unchanged.

## Usage

```csharp
using Trellis.Asp;
using Trellis.Asp.ApiVersioning;

[ApiController]
[ApiVersion("2026-12-01")]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Orders_GetById")]
    public IActionResult Get(int id) => Ok(...);

    [HttpPost]
    public ActionResult<Order> Create([FromBody] CreateOrderRequest req) =>
        _mediator.Send(new CreateOrderCommand(req))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Orders_GetById",
                    o => new RouteValueDictionary { ["id"] = o.Id })
                .WithVersionedRoute())
            .AsActionResult<Order>();
}
```

The resulting `Location` header is `/api/orders/42?api-version=2026-12-01` when the target maps that version. An unsupported requested version uses the mapped fallback order above.

To pin a specific version regardless of what the client requested, use `.WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1)))`. The pin must map to the target; it is written into the query value or actual URL-segment parameter. Neutral and missing-metadata targets skip injection and remove supplied `api-version` entries.

### Paginated lists — `HttpContext.PageUrl`

For distinct next/previous URLs, use the direction-aware overload and pass its result as
`urlBuilder: HttpContext.PageUrl(routeName, (cursor, direction, applied) => ...)`.
Return `RouteValueDictionary` entries keyed by `direction == PageDirection.Next ? "after" : "before"`.
The returned `Func<Cursor, PageDirection, int, string>` composes with both sync and async
paginated responses. An explicit `ApiVersion` overload is also available; both retain
the existing version-preservation, validation, and skip rules.

For paginated `Result<Page<T>>` responses, supply the `nextUrlBuilder` parameter of `ToHttpResponse(Async)` from `HttpContext.PageUrl(...)`:

```csharp
[HttpGet("overdue", Name = "Orders_GetOverdue")]
public async Task<IResult> GetOverdue(
    [FromQuery] string? cursor,
    [FromQuery] int? limit,
    [FromServices] IMediator mediator,
    CancellationToken ct)
{
    var result = await mediator.Send(new GetOverdueOrdersQuery(cursor, limit), ct);
    return result.ToHttpResponse(
        nextUrlBuilder: HttpContext.PageUrl(
            "Orders_GetOverdue",
            (c, applied) => new RouteValueDictionary
            {
                ["cursor"] = c.Token,
                ["limit"] = applied,
            }),
        body: o => OrderListItemResponse.From(o));
}
```

With the callback shown above (no consumer-supplied version), the emitted `next` URL is `/api/orders/overdue?cursor=<token>&limit=20&api-version=2026-12-01` for query/header versioning, `/api/v2026-12-01/orders/overdue?cursor=<token>&limit=20` for URL-segment versioning, and `/api/orders/overdue?cursor=<token>&limit=20` for neutral or missing-metadata targets. PageUrl skips automatic injection in the latter cases but **does not remove consumer-supplied `api-version` entries**, even on neutral targets. Only Location's `WithVersionedRoute` owns/removes/overrides version entries. The cursor is URL-encoded and `PathBase` is preserved.

The target action must carry `Name = "..."` on the `[HttpGet(...)]` attribute — the helper resolves the endpoint by route name via `EndpointDataSource`. An explicit-version overload supports cross-version next-page query URLs, but **unlike Location it rejects URL-segment pins**. Implicit PageUrl still uses ambient segment values and existing cross-route validation. Consumer-supplied `api-version` values keep their existing precedence.

## Related diagnostics

- **TRLS023** (`Trellis.Analyzers`) warns on `HttpResponseOptionsBuilder<T>.CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` calls inside `[ApiVersion]`-decorated controllers when the chain is not followed by `.WithVersionedRoute(...)` (or the manual primitive `.WithRouteValueResolver("api-version", ...)`, matched case-insensitively) and the route values dictionary literal does not include an `"api-version"` key. The code fix appends `.WithVersionedRoute()` and adds `using Trellis.Asp.ApiVersioning;` when missing. Manual suppression does not provide target-aware validation; detection rules are unchanged.

## Configuration

Register API versioning in the host as you would normally; this package does not require its own `services.AddXxx(...)` call:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(new DateOnly(2026, 12, 1));
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("api-version"));
});
```

The package depends on `Asp.Versioning.Http` (for the `HttpContext.RequestedApiVersion` extension property), `Asp.Versioning.Mvc`, and `Asp.Versioning.Mvc.ApiExplorer`.

## Documentation

- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)
- [`trellis-api-asp-apiversioning.md`](../docs/docfx_project/api_reference/trellis-api-asp-apiversioning.md) — LLM-targeted API reference

## Part of Trellis

This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
