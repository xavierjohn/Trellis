# Trellis.Asp.ApiVersioning

API-versioning helpers for [Trellis.Asp](../Trellis.Asp/README.md). Auto-inject the `api-version` route value into builder-generated URLs so responses round-trip the requested version under query/header API versioning, without authors having to remember the parameter or hard-code the version literal.

- **`HttpResponseOptionsBuilder<T>.WithVersionedRoute(...)`** — chains after `CreatedAtRoute(...)` / `CreatedAtAction(...)` (201 Created) or `WithLocation(...)` (200 OK on existing resources) to version the `Location` header.
- **`HttpContext.PageUrl(routeName, ...)`** — returns a `Func<Cursor, int, string>` for the `nextUrlBuilder` parameter of `ToHttpResponse(Async)` on `Result<Page<T>>`, so paginated `next` URLs carry the version, honor URL-segment ambient route values, and skip injection on neutral, URL-segment-versioned, and unversioned-host endpoints — no manual URL encoding.

## Why this package exists

Under query-string or header API versioning (`Asp.Versioning.QueryStringApiVersionReader`, `HeaderApiVersionReader`, or composite readers), `Location` headers from `HttpResponseOptionsBuilder<T>.CreatedAtRoute(routeName, routeValues)` silently omit the `api-version` parameter unless every author remembers to add it manually. The result: a `POST /api/orders?api-version=2026-12-01` returns `Location: /api/orders/42` (no version), and the follow-up `GET /api/orders/42` 404s because the controller is registered under the versioned route group.

The bug is invisible without integration tests that assert on the full Location URL, easy to miss in code review, and silently regresses across cycles. `.WithVersionedRoute()` removes the trap by injecting the version at request time using the configured `IApiVersionReader` chain.

## Resolution order

The resolver runs per request inside the `LinkGenerator` callback:

1. **`HttpContext.RequestedApiVersion`** — use the parsed version if the endpoint declares it.
2. **Exactly one declared version** — use the distinct union of the endpoint's implicit and explicit `DeclaredApiVersions`.
3. **`ApiVersioningOptions.DefaultApiVersion`** — use the configured host default only if the endpoint declares it; otherwise throw `InvalidOperationException`.

`WithVersionedRoute` resolves versions and skip rules from the **current request endpoint**, not the `Location` target. Applications must verify cross-route target compatibility themselves, including with explicit version pins. `PageUrl`, by contrast, inspects its named target endpoint.

The resolver short-circuits to a no-op (no `api-version` route value injected) when:

- The endpoint is decorated with `[ApiVersionNeutral]`.
- The endpoint participates in URL-segment versioning (route template contains `:apiVersion`).
- The endpoint has no `ApiVersionMetadata` attached — the host did not call `services.AddApiVersioning(...)`, or this endpoint sits outside its versioning surface. Lets the helpers compose cleanly in unversioned and mixed-versioned hosts by silently dropping injection rather than emitting a stale URL artefact. The framework emits a single warning per `(endpoint, AppDomain)` pair through the `Trellis.Asp.ApiVersioning` `ILogger` category to flag the mid-migration scenario where `AddApiVersioning(...)` was removed but the `.WithVersionedRoute()` chain remained; set `TrellisAspOptions.FailFastOnSilentVersionInjection = true` to throw on every offending request instead.

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

The resulting `Location` header is `/api/orders/42?api-version=2026-12-01` (or whatever version the client requested).

To pin a specific version regardless of what the client requested (cross-version `Location` redirects on deprecated endpoints), use the explicit overload: `.WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1)))`. The neutral, URL-segment, and missing-metadata skip rules still apply: an explicit pin is never injected into a Location targeting a `[ApiVersionNeutral]` endpoint, a `v{version:apiVersion}` template, or an endpoint with no `ApiVersionMetadata` (unversioned hosts where `AddApiVersioning(...)` was never called).

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

The emitted `next` URL is `/api/orders/overdue?cursor=<token>&limit=20&api-version=2026-12-01` for query/header versioning, `/api/v2026-12-01/orders/overdue?cursor=<token>&limit=20` for URL-segment versioning (no duplicate query parameter), and `/api/orders/overdue?cursor=<token>&limit=20` (no version) for `[ApiVersionNeutral]` controllers as well as for endpoints with no `ApiVersionMetadata` (unversioned hosts where `AddApiVersioning(...)` was never called). The cursor token is URL-encoded automatically. `PathBase` is preserved for hosts mounted under a virtual directory.

The target action must carry `Name = "..."` on the `[HttpGet(...)]` attribute — the helper resolves the endpoint by route name via `EndpointDataSource`. An explicit-version overload mirrors `WithVersionedRoute(ApiVersion)` for cross-version next-page URLs.

## Related diagnostics

- **TRLS023** (`Trellis.Analyzers`) warns on `HttpResponseOptionsBuilder<T>.CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` calls inside `[ApiVersion]`-decorated controllers when the chain is not followed by `.WithVersionedRoute(...)` (or the equivalent manual primitive `.WithRouteValueResolver("api-version", ...)`, matched case-insensitively) and the route values dictionary literal does not include an `"api-version"` key. The code fix appends `.WithVersionedRoute()` and adds `using Trellis.Asp.ApiVersioning;` when missing.

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
