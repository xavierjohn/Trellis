---
package: Trellis.Asp.ApiVersioning
namespaces: [Trellis.Asp.ApiVersioning]
types: [HttpResponseOptionsBuilderApiVersioningExtensions, HttpContextPageUrlExtensions]
version: v1
last_verified: 2026-06-03
audience: [llm]
---
# Trellis.Asp.ApiVersioning — API Reference

## Header

- **Package:** `Trellis.Asp.ApiVersioning`
- **Namespace:** `Trellis.Asp.ApiVersioning`
- **Purpose:** API-versioning helpers that auto-inject the `api-version` route value into URLs emitted by `Trellis.Asp` builders. `HttpResponseOptionsBuilderApiVersioningExtensions` covers `Location` headers from `CreatedAtRoute(...)` / `CreatedAtAction(...)` (201 Created) and `WithLocation(...)` (200 OK on existing resources). `HttpContextPageUrlExtensions` covers paginated-list next-page URLs supplied to the `nextUrlBuilder` parameter of `ToHttpResponse(Async)` for `Result<Page<T>>`. All helpers skip injection on `[ApiVersionNeutral]` endpoints and on endpoints with no `ApiVersionMetadata` (hosts that never called `AddApiVersioning(...)`). For URL-segment versioning the per-request overloads and explicit `WithVersionedRoute(ApiVersion)` skip query injection and let ambient route data fill the path segment; explicit `PageUrl(routeName, version, ...)` throws instead, because silently dropping the pin would let `LinkGenerator` emit the wrong versioned path.

See also: [trellis-api-asp.md](trellis-api-asp.md#httpresponseoptionsbuildertdomain) — the underlying `HttpResponseOptionsBuilder<T>`, `CreatedAtRoute`, `CreatedAtAction`, `WithLocation`, and `WithRouteValueResolver` hook this package builds on. [trellis-api-analyzers.md](trellis-api-analyzers.md#error-ef-core-and-value-object-rules) — `TRLS023` warns on `CreatedAtRoute` / `CreatedAtAction` / `WithLocation` calls in versioned controllers that omit the `api-version` route value, and offers a code fix that chains `.WithVersionedRoute()`.

## Use this file when

- You return `Result<T>` responses from versioned controllers (`[ApiVersion("…")]`) and need a builder-generated `Location` header to round-trip the client's requested `api-version`.
- You return paginated `Result<Page<T>>` responses from versioned controllers and need the `next`-page URL (the `nextUrlBuilder` parameter of `ToHttpResponse(Async)`) to carry the version, honor URL-segment ambient route values, and skip injection on neutral endpoints — without hard-coding the version literal or hand-rolling URL encoding.
- You configured `Asp.Versioning` with `QueryStringApiVersionReader`, `HeaderApiVersionReader`, or a composite reader (anything that is *not* URL-segment versioning) and need link generation to preserve the version.
- You want a per-request hook to inject any other route value into `Location` (the `WithRouteValueResolver` mechanism `Trellis.Asp` exposes; this package consumes it).

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Return 201 Created with versioned Location | Chain `.WithVersionedRoute()` after `CreatedAtRoute(...)` or `CreatedAtAction(...)` | [`HttpResponseOptionsBuilderApiVersioningExtensions`](#httpresponseoptionsbuilderapiversioningextensions) |
| Return 200 OK with versioned Location on an existing resource | Chain `.WithVersionedRoute()` after `WithLocation(...)` | [`WithLocation` composition](#withlocation-composition) |
| Single id route value | `CreatedAtRoute(routeName, x => x.Id).WithVersionedRoute()` (uses the single-id overload from `Trellis.Asp`) | [Composition examples](#composition-examples) |
| Multi-key route values | `CreatedAtRoute(routeName, x => new RouteValueDictionary { ["tenantId"] = x.TenantId, ["id"] = x.Id }).WithVersionedRoute()` | [Composition examples](#composition-examples) |
| Pin Location to a specific version (rare) | `CreatedAtRoute(...).WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1)))` | [Explicit-version overload](#explicit-version-overload) |
| Paginated list — emit versioned next-page URL | `HttpContext.PageUrl(routeName, (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied })` passed as the `nextUrlBuilder` argument of `ToHttpResponse(Async)` | [`HttpContextPageUrlExtensions`](#httpcontextpageurlextensions) |
| Paginated list — pin next-page URL to a specific version | `HttpContext.PageUrl(routeName, new ApiVersion(new DateOnly(2026, 12, 1)), (c, applied) => …)` | [`PageUrl` explicit-version overload](#pageurl-explicit-version-overload) |
| `[ApiVersionNeutral]` controller | Use `CreatedAtRoute` (no version injected); `.WithVersionedRoute()` short-circuits the resolver to a no-op when the endpoint is neutral | [Behavioral notes](#behavioral-notes) |
| URL-segment versioning (`v{version:apiVersion}` in template) | Continue to use `CreatedAtRoute`; the segment is filled by the route template, not a query route value | [Behavioral notes](#behavioral-notes) |
| Detect mid-migration silent skips (`.WithVersionedRoute()` left in place after `AddApiVersioning(...)` was removed) | Default: warn once per `(endpoint, AppDomain)` to the `Trellis.Asp.ApiVersioning` `ILogger` category. Opt-in fail-fast: `services.AddTrellisAsp(o => o.FailFastOnSilentVersionInjection = true)` | [Behavioral notes](#behavioral-notes) |

## Common traps

- Do **not** add `["api-version"] = …` manually inside the route values dictionary when you chain `.WithVersionedRoute()`. The resolver runs *after* the route-values selector and **overwrites** any pre-existing `api-version` entry, so manual entries are silently discarded. Either trust the resolver, or use the `WithRouteValueResolver` hook directly with your own logic.
- Do not assume `.WithVersionedRoute()` works for URL-segment versioning. URL-segment templates resolve the version from the route template parameter, not from a query/header route value; the helper intentionally skips injection in that case.
- `WithLocation(...)` produces a `Location` header **without** changing the status code (typically 200 OK on `Result<T>` responses). Use it on state-transition endpoints that mutate an existing resource and want to point clients at the canonical URL (e.g., `POST /orders/{id}/return` returning 200 OK). For new-resource creation, use `CreatedAtRoute(...)` / `CreatedAtAction(...)` (201 Created). `Result<WriteOutcome<T>>` uses the `WriteOutcome` location/monitor URI instead; builder `WithLocation(...)` and route-value resolvers do not rewrite those outcome-owned URIs.
- The route values selector should return a fresh `RouteValueDictionary` per call. The runtime clones the dictionary defensively before applying resolvers, but selectors that return a shared instance still cost an unnecessary allocation per request.
- Configure `Asp.Versioning` with an explicit `DefaultApiVersion` if you support multiple declared versions on the same controller. With `AllowMultiple = true` `[ApiVersion]` and no client-supplied version, the resolver throws rather than silently picking one.
- `HttpContext.PageUrl(routeName, ...)` requires the target action to carry a route name (`[HttpGet("...", Name = "Things_List")]`). Without a name the helper cannot resolve the endpoint and the returned builder throws `InvalidOperationException` on first invocation. The route name typically matches the current paginated endpoint (self-referential pagination) but cross-route pagination is supported — supply path parameters in the callback's `RouteValueDictionary` for the target route's template.

## Types

### `HttpResponseOptionsBuilderApiVersioningExtensions`

**Declaration**

```csharp
public static class HttpResponseOptionsBuilderApiVersioningExtensions
```

**Constructors**

- None. This is a static class.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| None | — | This static class exposes no public properties. |

**Methods**

| Signature | Returns | Behavior |
| --- | --- | --- |
| `WithVersionedRoute<TDomain>(this HttpResponseOptionsBuilder<TDomain> builder)` | `HttpResponseOptionsBuilder<TDomain>` | Injects the configured `api-version` route value into the `Location` header emitted by a preceding `CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` call. The version is resolved per-request from `HttpContext`. Equivalent to `builder.WithRouteValueResolver("api-version", ResolveApiVersion)`. |
| `WithVersionedRoute<TDomain>(this HttpResponseOptionsBuilder<TDomain> builder, ApiVersion explicitVersion)` | `HttpResponseOptionsBuilder<TDomain>` | Escape hatch: pin the `Location` header to a specific `ApiVersion`, overriding the per-request resolution order (requested / declared / default). Skip rules still apply — `[ApiVersionNeutral]` endpoints, `:apiVersion` URL-segment templates, and endpoints with no `ApiVersionMetadata` (host did not call `AddApiVersioning(...)`) receive no `api-version` route value even when explicitly pinned. Useful for cross-version `Location` redirects on deprecated endpoints. |

#### Behavioral notes

The api-version resolver runs per request inside the `LinkGenerator` callback (after the route-values selector produces the route values dictionary, before link generation). Resolution order:

1. **`HttpContext.RequestedApiVersion`** — primary signal; reflects whatever the configured `IApiVersionReader` parsed (query, header, media-type, URL segment, composite). This is the C# extension property from `Asp.Versioning.Http`.
2. **Endpoint metadata `ApiVersionMetadata.Map(ApiVersionMapping.Implicit).DeclaredApiVersions`** — fallback when (1) is null and exactly one declared version exists. Throws if the endpoint declares multiple versions and the client supplied none.
3. **`ApiVersioningOptions.DefaultApiVersion`** — final fallback, configured via `services.AddApiVersioning(o => o.DefaultApiVersion = …)`.

Both overloads short-circuit to a no-op (no `api-version` route value injected) when:

- The endpoint is decorated with `[ApiVersionNeutral]` — version-neutral endpoints must not carry a version in their `Location` header.
- The endpoint participates in URL-segment versioning (the route template contains a `{version:apiVersion}` segment) — the segment is filled by the route template binder, not a query/header route value.
- The endpoint has no `ApiVersionMetadata` attached — the host did not call `services.AddApiVersioning(...)`, or this endpoint sits outside its versioning surface. Emitting an `api-version` route value when no API-versioning middleware is installed would be a stale URL artefact the receiving middleware would never act on; the helpers compose cleanly in unversioned and mixed-versioned hosts by silently dropping injection in this case. The framework emits a single warning per `(endpoint, AppDomain)` pair through the `Trellis.Asp.ApiVersioning` `ILogger` category to flag the mid-migration scenario where `AddApiVersioning(...)` was removed but the chain remained; set `TrellisAspOptions.FailFastOnSilentVersionInjection = true` to throw on every offending request instead.

The skip rules apply to the explicit-version overload as well: `WithVersionedRoute(explicitVersion)` overrides the resolution order but still respects neutral, URL-segment, and missing-metadata skips, so a pinned version is never injected into a Location that targets a neutral endpoint, duplicates a path-segment version, or lands on an endpoint with no versioning metadata.

The route-value key is fixed at `"api-version"`, matching the default for `QueryStringApiVersionReader` and the conventional header name. Hosts using a non-default reader parameter name should register a custom resolver via `WithRouteValueResolver(<key>, …)` directly instead of using this package.

#### Composition examples

Single-id overload (sugar for the common `{ ["id"] = order.Id }` shape):

```csharp
return result.ToHttpResponse(opts => opts
    .CreatedAtRoute("Customers_GetById", c => c.Id)
    .WithVersionedRoute());
```

Generates `Location: /customers/42?api-version=2026-12-01` when the request specified `?api-version=2026-12-01`. The `idRouteKey` parameter on `CreatedAtRoute` defaults to `"id"`; supply a different key when the route template uses a different parameter name (e.g. `"orderId"`).

Multi-key route values:

```csharp
return result.ToHttpResponse(opts => opts
    .CreatedAtRoute(
        "Orders_GetById",
        o => new RouteValueDictionary
        {
            ["tenantId"] = o.TenantId,
            ["id"] = o.Id,
        })
    .WithVersionedRoute());
```

#### `WithLocation` composition

For state-transition endpoints that return 200 OK (or another 2xx) but want a `Location` header pointing to the canonical URL of the mutated resource:

```csharp
return result.ToHttpResponse(opts => opts
    .WithLocation("Orders_GetById", o => o.Id)
    .WithVersionedRoute());
```

Unlike `CreatedAtRoute`, `WithLocation` does **not** force the status code to 201 — the `Result<T>` response's natural 200 OK status is preserved. It does not affect `Result<WriteOutcome<T>>` responses, where `WriteOutcome.Created`, `WriteOutcome.Accepted`, and `WriteOutcome.AcceptedNoContent` own their literal `Location` / monitor URI values.

#### Explicit-version overload

```csharp
return result.ToHttpResponse(opts => opts
    .CreatedAtRoute("Orders_GetById", o => new RouteValueDictionary { ["id"] = o.Id })
    .WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1))));
```

Pins the `Location` to `?api-version=2026-12-01` regardless of what the client requested. Use this only for redirects to a fixed version (deprecation flows, version migration). For the common case, prefer the parameterless overload. The neutral, URL-segment, and missing-metadata skip rules still apply: an explicit pin is never injected into a Location that targets a `[ApiVersionNeutral]` endpoint, a `v{version:apiVersion}` template, or an endpoint with no `ApiVersionMetadata` (unversioned hosts where `AddApiVersioning(...)` was never called).

### `HttpContextPageUrlExtensions`

**Declaration**

```csharp
public static class HttpContextPageUrlExtensions
```

**Constructors**

- None. This is a static class.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| None | — | This static class exposes no public properties. |

**Methods**

| Signature | Returns | Behavior |
| --- | --- | --- |
| `PageUrl(this HttpContext httpContext, string routeName, Func<Cursor, int, RouteValueDictionary> routeValues)` | `Func<Cursor, int, string>` | Returns a request-scoped builder suitable for the `nextUrlBuilder` parameter of `ToHttpResponse(Async)` on `Result<Page<T>>`. Per-request resolution of `api-version`: client-requested version (only when the target endpoint declares it — cross-route v2-request → v1-only target falls through) → single declared version on the target endpoint → `DefaultApiVersion` → throw. Defensively clones the consumer-returned dictionary before injecting `api-version`. Consumer-supplied `api-version` keys win. Skipped on `[ApiVersionNeutral]` and `:apiVersion` URL-segment routes, and also skipped when the target endpoint has no `ApiVersionMetadata` (host did not call `AddApiVersioning(...)`). |
| `PageUrl(this HttpContext httpContext, string routeName, ApiVersion version, Func<Cursor, int, RouteValueDictionary> routeValues)` | `Func<Cursor, int, string>` | Explicit-version overload — pins the next-page URL to a specific `ApiVersion`. The pin is silently skipped on `[ApiVersionNeutral]` targets and on targets with no `ApiVersionMetadata` (the same precedent as `WithVersionedRoute(ApiVersion)`); in both cases emitting the pin as a query parameter would be a stale URL artefact the target middleware would not act on. On URL-segment-versioned targets (`:apiVersion` in the route template) the pin throws `InvalidOperationException` instead — silently honouring the pin would let `LinkGenerator` fill the segment from ambient route data, producing a URL with the *wrong* version. The pin also throws when the target endpoint has `ApiVersionMetadata` but does not declare `version` among its implicit/explicit declared versions — emitting the URL would produce a link the target rejects (e.g. `400` from the versioning middleware) when followed. |

#### `PageUrl` behavioral notes

- **Self-referential pagination is the common case.** A paginated list endpoint typically supplies its own route name to `PageUrl(...)`: the next-page URL targets the same action with a different `cursor`. The resolver uses the *target* endpoint for skip / declared-version decisions, so when the target equals the current endpoint the rules collapse to the per-request rules of `WithVersionedRoute()`.
- **Cross-route pagination is supported.** Pass any registered route name. The helper picks up the target endpoint's `[ApiVersion]` declarations and URL-segment template; supply the target's required path parameters (besides ambient ones that `LinkGenerator` fills automatically from the current request's route values) in the `RouteValueDictionary` returned from the callback.
- **URL-segment versioning works without consumer awareness.** When the target route template carries a `{version:apiVersion}` segment, `LinkGenerator.GetUriByRouteValues(httpContext, ...)` fills the segment from ambient route data. The helper recognises this case and skips injecting an `api-version` query parameter — the version travels via the URL segment, not as a duplicate query value.
- **`PathBase` is preserved.** Building absolute URLs through `LinkGenerator.GetUriByRouteValues(httpContext, ...)` carries the request's scheme, host, and `PathBase` into the emitted URL — important for hosts mounted under a virtual directory.
- **Request-scoped contract.** The returned `Func` captures `httpContext` and must be invoked during the same request that produced it — not handed off to a background `Task`. The framework's `ToHttpResponse(Async)` consumes the builder synchronously while building the response envelope, so the typical consumer call site honors this naturally.
- **Cross-route version validation.** When the target endpoint is different from the current endpoint and does not declare the client-requested version, the resolver does NOT echo the requested version — it would emit a URL the target immediately rejects. The fallback chain (single-declared → `DefaultApiVersion` → throw) takes over instead. Same-route helpers are unaffected because the requested version is always declared on the current endpoint.
- **Failure modes.** The returned builder throws `InvalidOperationException` when (a) the target route name resolves to no registered endpoint, (b) a multi-version target has neither a declared client-requested version nor a `DefaultApiVersion`, (c) `LinkGenerator.GetUriByRouteValues` returns `null` (the supplied + ambient route values do not match the target template), (d) the consumer's `routeValues` callback returns `null`, (e) the explicit-version overload targets a URL-segment-versioned route (the pin cannot be honoured as a query parameter and silently filling the segment from ambient route data would emit the wrong version), or (f) the explicit-version overload targets an endpoint whose `ApiVersionMetadata` does not declare the pinned version — silently emitting a URL the target rejects would mislead clients.
- **Unversioned hosts compose cleanly.** When the host has not called `services.AddApiVersioning(...)`, the target endpoint has no `ApiVersionMetadata` and both overloads skip `api-version` injection silently — the per-request overload emits a clean URL instead of throwing the unresolvable-version error (whose advice — configure `DefaultApiVersion` or use the explicit overload — does not apply when versioning is not in use), and the explicit overload drops the pin instead of injecting a stale query parameter the (absent) versioning middleware would never consume. Same behaviour applies to mixed hosts where individual paginated endpoints sit outside the versioning surface.

#### `PageUrl` composition example

Canonical paginated controller action consuming `PageUrl` via the `nextUrlBuilder` parameter:

```csharp
[ApiController]
[ApiVersion("2026-12-01")]
[Route("orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("overdue", Name = "Orders_GetOverdue")]
    public async Task<IResult> GetOverdue(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        [FromServices] IMediator mediator,
        CancellationToken ct)
    {
        var query = new GetOverdueOrdersQuery(cursor, limit);
        var result = await mediator.Send(query, ct);
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
}
```

Replaces hand-rolled URL construction (`$"{Request.Scheme}://{Request.Host}/api/v{version}/orders/overdue?cursor={Uri.EscapeDataString(c.Token)}&limit={applied}"`) with a single call that resolves the version, encodes the cursor, and preserves `PathBase` automatically.

#### `PageUrl` explicit-version overload

```csharp
return result.ToHttpResponse(
    nextUrlBuilder: HttpContext.PageUrl(
        "Orders_GetOverdue",
        new ApiVersion(new DateOnly(2026, 12, 1)),
        (c, applied) => new RouteValueDictionary
        {
            ["cursor"] = c.Token,
            ["limit"] = applied,
        }),
    body: o => OrderListItemResponse.From(o));
```

Use only when the next-page URL must target a fixed version (cross-version migration of a deprecated paginated endpoint pointing clients at its successor). The pin is silently skipped on `[ApiVersionNeutral]` targets and on targets with no `ApiVersionMetadata` (unversioned hosts where `AddApiVersioning(...)` was never called). On URL-segment-versioned targets (`v{version:apiVersion}` in the template) the pin throws `InvalidOperationException` instead — silently honouring it would let `LinkGenerator` fill the segment from ambient route data and produce a URL with the wrong version; switch to the per-request implicit overload, which resolves the segment from ambient route data and validates cross-route target-version support. The pin also throws when the target endpoint declares `[ApiVersion]` metadata but not the pinned version (e.g. pinning v2 against a v1-only target) — the helper refuses to emit a URL the target would reject when followed, and the message points callers either at a declared version or at the per-request overload.

### Configuration

Register API versioning in the host as you would normally; `Trellis.Asp.ApiVersioning` does not require its own `services.AddXxx(...)` call:

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

The package depends on `Asp.Versioning.Http` (for `HttpContext.RequestedApiVersion`), `Asp.Versioning.Mvc`, and `Asp.Versioning.Mvc.ApiExplorer`.

## Related diagnostics

- **TRLS023** (`Trellis.Analyzers`) — warns on `HttpResponseOptionsBuilder<T>.CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` calls inside `[ApiVersion]`-decorated controllers when the chain is not followed by `.WithVersionedRoute(...)` (or the equivalent manual primitive `.WithRouteValueResolver("api-version", ...)`, matched case-insensitively) and the route values dictionary literal does not include an `"api-version"` key. Code fix appends `.WithVersionedRoute()` and adds `using Trellis.Asp.ApiVersioning;` when missing.
