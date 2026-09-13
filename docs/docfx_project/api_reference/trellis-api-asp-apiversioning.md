---
package: Trellis.Asp.ApiVersioning
namespaces: [Trellis.Asp.ApiVersioning]
types: [HttpResponseOptionsBuilderApiVersioningExtensions, HttpContextPageUrlExtensions]
version: v1
last_verified: 2026-09-12
audience: [llm]
---
# Trellis.Asp.ApiVersioning — API Reference

## Header

- **Package:** `Trellis.Asp.ApiVersioning`
- **Namespace:** `Trellis.Asp.ApiVersioning`
- **Purpose:** Target-aware API-versioning helpers for URLs emitted by `Trellis.Asp`. `HttpResponseOptionsBuilderApiVersioningExtensions` versions `Location` headers from `CreatedAtRoute(...)` / `CreatedAtAction(...)` (201 Created) and `WithLocation(...)` (normal 2xx on existing resources). It resolves the actual destination and writes the supported version into its `:apiVersion` segment parameter or the conventional `api-version` query route value. `HttpContextPageUrlExtensions` supplies paginated next/previous URLs to `ToHttpResponse(Async)` for `Result<Page<T>>`; its implicit URL-segment overloads still use ambient routing and its explicit overloads still reject segment pins. Both helpers skip injection for neutral or missing-metadata targets; Location additionally removes supplied `api-version` values in those cases.

See also: [trellis-api-asp.md](trellis-api-asp.md#httpresponseoptionsbuildertdomain) — the underlying `HttpResponseOptionsBuilder<T>`, `CreatedAtRoute`, `CreatedAtAction`, `WithLocation`, and destination-aware `WithLocationRouteResolver` hook this package builds on. [trellis-api-analyzers.md](trellis-api-analyzers.md#error-ef-core-and-value-object-rules) — `TRLS023` warns on `CreatedAtRoute` / `CreatedAtAction` / `WithLocation` calls in versioned controllers that omit the `api-version` route value, and offers a code fix that chains `.WithVersionedRoute()`.

## Use this file when

- You return `Result<T>` responses from versioned controllers (`[ApiVersion("…")]`) and need a builder-generated `Location` header to round-trip the client's requested `api-version`.
- You return paginated `Result<Page<T>>` responses from versioned controllers and need the `next`-page URL (the `nextUrlBuilder` parameter of `ToHttpResponse(Async)`) to carry the version, honor URL-segment ambient route values, and skip injection on neutral endpoints — without hard-coding the version literal or hand-rolling URL encoding.
- You configured `Asp.Versioning` with query, header, URL-segment, or composite readers and need Location links to carry a version accepted by their destination.
- You need destination-aware Location customization (`WithLocationRouteResolver`), after ordinary per-request values (`WithRouteValueResolver`) have been applied.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Return 201 Created with versioned Location | Chain `.WithVersionedRoute()` after `CreatedAtRoute(...)` or `CreatedAtAction(...)` | [`HttpResponseOptionsBuilderApiVersioningExtensions`](#httpresponseoptionsbuilderapiversioningextensions) |
| Return 200 OK with versioned Location on an existing resource | Chain `.WithVersionedRoute()` after `WithLocation(...)` | [`WithLocation` composition](#withlocation-composition) |
| Single id route value | `CreatedAtRoute(routeName, x => x.Id).WithVersionedRoute()` (uses the single-id overload from `Trellis.Asp`) | [Composition examples](#composition-examples) |
| Multi-key route values | `CreatedAtRoute(routeName, x => new RouteValueDictionary { ["tenantId"] = x.TenantId, ["id"] = x.Id }).WithVersionedRoute()` | [Composition examples](#composition-examples) |
| Pin Location to a specific version (rare) | `CreatedAtRoute(...).WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1)))` | [Explicit-version overload](#explicit-version-overload) |
| Paginated list — emit versioned next-page URL | `HttpContext.PageUrl(routeName, (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied })` passed as the `nextUrlBuilder` argument of `ToHttpResponse(Async)` | [`HttpContextPageUrlExtensions`](#httpcontextpageurlextensions) |
| Paginated list — distinct versioned next/previous URLs | `HttpContext.PageUrl(routeName, (cursor, direction, applied) => ...)` passed as the `urlBuilder` argument of `ToHttpResponse(Async)`; choose `after` / `before` keys using `Trellis.Asp.PageDirection` | [`Directional pagination`](#directional-pagination) |
| Paginated list — pin next-page URL to a specific version | `HttpContext.PageUrl(routeName, new ApiVersion(new DateOnly(2026, 12, 1)), (c, applied) => …)` | [`PageUrl` explicit-version overload](#pageurl-explicit-version-overload) |
| Link to an `[ApiVersionNeutral]` destination | `.WithVersionedRoute()` skips injection and removes supplied `api-version` values from the cloned dictionary | [Behavioral notes](#behavioral-notes) |
| URL-segment Location, including cross-route links or explicit pins | `.WithVersionedRoute(...)` writes the resolved version into the target's actual `:apiVersion` parameter name, without a duplicate query value | [Behavioral notes](#behavioral-notes) |
| Detect mid-migration silent skips (`.WithVersionedRoute()` left in place after `AddApiVersioning(...)` was removed) | Default: warn once per `(endpoint, AppDomain)` to the `Trellis.Asp.ApiVersioning` `ILogger` category. Opt-in fail-fast: `services.AddTrellisAsp(o => o.FailFastOnSilentVersionInjection = true)` | [Behavioral notes](#behavioral-notes) |

## Common traps

- Do **not** supply competing version values when chaining `.WithVersionedRoute()`. It owns version route values after the selector and **all** `WithRouteValueResolver` callbacks, regardless of configuration order. It overwrites query/segment version values or removes `api-version` for segment, neutral, and missing-metadata targets.
- Do not treat `PageUrl` segment behavior as identical to Location: Location writes and honors segment pins; implicit `PageUrl` retains ambient routing and cross-route validation, and explicit `PageUrl` still rejects segment pins.
- Use a uniquely named destination route when action or route addressing yields multiple link-generation candidates. Suppressed link-generation endpoints are excluded; zero or multiple remaining candidates throw rather than falling back to the current endpoint.
- `WithLocation(...)` produces a `Location` header **without** changing the status code (typically 200 OK on `Result<T>` responses). Use it on state-transition endpoints that mutate an existing resource and want to point clients at the canonical URL (e.g., `POST /orders/{id}/return` returning 200 OK). For new-resource creation, use `CreatedAtRoute(...)` / `CreatedAtAction(...)` (201 Created). `Result<WriteOutcome<T>>` uses the `WriteOutcome` location/monitor URI instead; builder `WithLocation(...)` and route-value resolvers do not rewrite those outcome-owned URIs.
- The route values selector must return a non-null `RouteValueDictionary`. The runtime clones it before applying resolvers, so callbacks do not mutate a shared selector dictionary. Application code must not modify a shared dictionary concurrently.
- For a target with multiple mapped declared versions, supply a mapped requested version or configure a mapped `DefaultApiVersion`. If neither is available, resolution throws rather than silently picking one. Controller declarations alone do not make an action multi-version.
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
| `WithVersionedRoute<TDomain>(this HttpResponseOptionsBuilder<TDomain> builder)` | `HttpResponseOptionsBuilder<TDomain>` | Registers a `WithLocationRouteResolver` callback that resolves the final named-route or MVC-action destination and injects a version mapped to that target at Location generation. Works before or after configuring `CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)`. |
| `WithVersionedRoute<TDomain>(this HttpResponseOptionsBuilder<TDomain> builder, ApiVersion explicitVersion)` | `HttpResponseOptionsBuilder<TDomain>` | Pins Location to a version mapped to the final destination, overriding automatic resolution. Unsupported pins throw `InvalidOperationException`. Honors target segment parameters as well as query-style links; neutral and missing-metadata targets skip injection and remove supplied `api-version` entries. |

#### Behavioral notes

At Location generation, the resolver receives the **final destination**, independently of fluent configuration order, and a per-execution copy of the domain selector's dictionary after **all** legacy `WithRouteValueResolver` callbacks. It uses public ASP.NET Core endpoint-address schemes for the named-route or MVC-action address. Endpoints suppressed for link generation are excluded. No candidates throws `InvalidOperationException`; multiple candidates throws `InvalidOperationException` with advice to use a uniquely named destination route. It never falls back to `HttpContext.GetEndpoint()` for target metadata. For action destinations, the controller is the explicit controller, otherwise the route-value controller, otherwise the ambient current controller.

For a versioned target, the shared Location / `PageUrl` resolution order is:

1. **`HttpContext.RequestedApiVersion`** — use the parsed version only if it maps to the destination. The `Asp.Versioning.Http` extension property reflects the configured `IApiVersionReader` (query, header, media-type, URL segment, composite).
2. **Exactly one mapped declared target version** — apply the destination's action-level mappings before counting accepted declared versions, and use the version if exactly one remains. This also applies when the requested version is unsupported.
3. **`ApiVersioningOptions.DefaultApiVersion`** — use the configured default only if it maps to the destination; otherwise throw `InvalidOperationException`.

**Action mappings narrow controller declarations.** `[MapToApiVersion]` on an action does not automatically accept all `[ApiVersion]` values on its controller. Do not union implicit and explicit declaration sets to decide support. Both helpers validate requested, fallback, and pinned versions against actual destination mappings using public `Asp.Versioning` APIs; no upstream feature is required.

Location-specific application (both overloads):

- A target with a `:apiVersion` segment receives the resolved/pinned value in that **actual parameter name**, e.g. `revision` in `v{revision:apiVersion}`. Supplied segment values are overridden, and `api-version` is removed to avoid a duplicate query parameter. This supports cross-route segment links without relying on ambient version values.
- A query-style target receives the conventional `"api-version"` value, overriding values supplied by the selector or legacy callbacks. For a non-default query-reader key, use a custom hook instead of `WithVersionedRoute`.
- A neutral or missing-metadata target receives no version injection, and any supplied `"api-version"` entry is **removed from the cloned dictionary**. Neutral targets stay quiet. Missing metadata logs once per **destination endpoint / AppDomain** under `Trellis.Asp.ApiVersioning`, identifying the destination; `TrellisAspOptions.FailFastOnSilentVersionInjection = true` throws on every offending execution instead. `PageUrl` remains quiet for missing metadata.
- There is one `WithLocationRouteResolver` callback slot: the last registration wins, including repeated `WithVersionedRoute` calls or a custom hook that replaces one. Callback errors propagate; shared selector dictionaries are not mutated.
- Literal/selector `Created(...)` locations and `WriteOutcome`-owned Location/monitor URIs ignore the hook. `CreatedAtRoute` / `CreatedAtAction` retain 201, while `WithLocation` retains the normal 2xx status. Named routes remain AOT-compatible; `CreatedAtAction` retains its existing trimming/AOT limitations.

#### Migration: existing syntax, stricter destination checks

Keep `.WithVersionedRoute()` and `.WithVersionedRoute(ApiVersion)` call sites; there is no replacement API or new registration. Re-test generated links: missing or ambiguous destinations and unsupported pins now fail rather than using current-endpoint metadata. Prefer uniquely named destinations for ambiguous MVC actions. Segment pins are now honored, not silently ignored. Neutral/unversioned targets lose supplied `api-version` entries; missing-metadata warnings identify and deduplicate by the **target**, not the caller. Action-level mappings now constrain accepted versions in **both Location and PageUrl**. PageUrl's ambient segment routing, consumer overrides, rejection of explicit segment pins, and quiet missing-metadata behavior are otherwise unchanged.

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

Pins the `Location` to `?api-version=2026-12-01` for a query-style destination, or writes `2026-12-01` into the destination's actual `:apiVersion` segment parameter. The target must map the pin or the resolver throws. Neutral and missing-metadata targets skip injection and remove supplied `api-version` entries. Use this for redirects to a fixed version (deprecation flows, version migration); prefer automatic resolution for the common case.

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
| `PageUrl(this HttpContext httpContext, string routeName, Func<Cursor, int, RouteValueDictionary> routeValues)` | `Func<Cursor, int, string>` | Returns a request-scoped builder suitable for the `nextUrlBuilder` parameter of `ToHttpResponse(Async)` on `Result<Page<T>>`. Version resolution uses the shared mapping checks above: mapped client-requested version → single mapped declared target version → mapped `DefaultApiVersion` → throw. Defensively clones the consumer-returned dictionary before injecting `api-version`. Consumer-supplied `api-version` keys win. Injection is skipped on `[ApiVersionNeutral]` and `:apiVersion` URL-segment routes, and when the target endpoint has no `ApiVersionMetadata`. Ambient segment routing and existing cross-route validation are unchanged. |
| `PageUrl(this HttpContext httpContext, string routeName, ApiVersion version, Func<Cursor, int, RouteValueDictionary> routeValues)` | `Func<Cursor, int, string>` | Pins the next-page URL to a specific `ApiVersion`. The pin is silently skipped on neutral and missing-metadata targets. Unlike Location, URL-segment targets still throw `InvalidOperationException` rather than writing the pin into the segment. For a versioned query-style target, the version must map to the destination or the pin throws; controller declarations do not override an action's narrower mappings. Existing consumer-value precedence is unchanged. |

#### Directional pagination

The existing two-argument callback overloads remain supported. Use these additional
overloads when next and previous links need different route values:

```csharp
public static Func<Cursor, PageDirection, int, string> PageUrl(
    this HttpContext httpContext,
    string routeName,
    Func<Cursor, PageDirection, int, RouteValueDictionary> routeValues);

public static Func<Cursor, PageDirection, int, string> PageUrl(
    this HttpContext httpContext,
    string routeName,
    ApiVersion version,
    Func<Cursor, PageDirection, int, RouteValueDictionary> routeValues);
```

`PageDirection` is the ASP-owned enum (`Trellis.Asp.PageDirection.Next` /
`Trellis.Asp.PageDirection.Previous`), not a separate versioning type. These overloads pass
the direction unchanged to `routeValues` and return the delegate expected by the
direction-aware `ToHttpResponse` / `ToHttpResponseAsync` overloads. Version resolution,
dictionary cloning, consumer-version precedence, explicit-pin validation, URL-segment
handling, and neutral/unversioned skip rules are identical to their respective
two-argument callback overloads. Each link preserves the request's scheme, host, and
`PathBase`. No registration changes are required.

```csharp
return pageResult.ToHttpResponse(
    urlBuilder: HttpContext.PageUrl(
        "Orders_List",
        (cursor, direction, applied) => new RouteValueDictionary
        {
            [direction == PageDirection.Next ? "after" : "before"] = cursor.Token,
            ["limit"] = applied,
        }),
    body: order => OrderListItemResponse.From(order));
```

The named endpoint must implement the corresponding forward/backward query semantics;
URL generation does not add reverse-seek support. `Page<T>.Previous` remains optional.

#### `PageUrl` behavioral notes

- **Self-referential pagination is the common case.** A paginated list endpoint typically supplies its own route name to `PageUrl(...)`: the next-page URL targets the same action with a different `cursor`. Target version-support checks are shared with Location, but PageUrl retains its own segment, consumer-override, and missing-metadata behavior.
- **Cross-route pagination is supported.** Pass any registered route name. The helper uses the target endpoint's actual version mappings and URL-segment template; supply the target's required path parameters (besides ambient ones that `LinkGenerator` fills automatically from the current request's route values) in the `RouteValueDictionary` returned from the callback.
- **URL-segment versioning works without consumer awareness.** When the target route template carries a `{version:apiVersion}` segment, `LinkGenerator.GetUriByRouteValues(httpContext, ...)` fills the segment from ambient route data. The helper skips automatic `api-version` query injection; it does not remove a query value supplied by the consumer.
- **Skipping injection does not remove consumer values.** For neutral, missing-metadata, and implicit URL-segment targets, PageUrl preserves any consumer-supplied `api-version` entry. A manually supplied value can therefore appear even on a neutral target. Only Location's `WithVersionedRoute` owns/removes/overrides version entries; PageUrl's existing consumer precedence is unchanged.
- **`PathBase` is preserved.** Building absolute URLs through `LinkGenerator.GetUriByRouteValues(httpContext, ...)` carries the request's scheme, host, and `PathBase` into the emitted URL — important for hosts mounted under a virtual directory.
- **Request-scoped contract.** The returned `Func` captures `httpContext` and must be invoked during the same request that produced it — not handed off to a background `Task`. The framework's `ToHttpResponse(Async)` consumes the builder synchronously while building the response envelope, so the typical consumer call site honors this naturally.
- **Cross-route version validation.** The destination action must accept the version: `[MapToApiVersion]` narrows controller declarations, so sharing a controller version is not sufficient. For query-style links, an unsupported requested version falls through to a single mapped declared version, then a mapped `DefaultApiVersion`, then throws. URL-segment links retain ambient segment routing and existing cross-route target-version validation; PageUrl does not adopt Location's segment rewriting.
- **Failure modes.** The returned builder throws `InvalidOperationException` when (a) the target route name resolves to no registered endpoint, (b) no requested, single declared, or default version maps to the versioned target, (c) `LinkGenerator.GetUriByRouteValues` returns `null` (the supplied + ambient route values do not match the target template), (d) the consumer's `routeValues` callback returns `null`, (e) the explicit-version overload targets a URL-segment-versioned route, or (f) the explicit pin does not map to the target.
- **Unversioned hosts compose cleanly.** When the target endpoint has no `ApiVersionMetadata`, both PageUrl overloads skip automatic injection silently rather than throwing an unresolvable-version error; the explicit overload drops its pin. Consumer-supplied version entries are retained, so the URL is version-free only when the consumer has not supplied one. This also applies to individual unversioned endpoints in mixed hosts.

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

Use only when the next-page URL must target a fixed version (cross-version migration of a deprecated paginated endpoint pointing clients at its successor). Behaviour depends on the target endpoint:

| Target endpoint | Pin behaviour | Why |
|---|---|---|
| `[ApiVersionNeutral]`, or no `ApiVersionMetadata` (unversioned host where `AddApiVersioning(...)` was never called) | Pin silently skipped; consumer-supplied values retained | There is no version to pin; skipping does not remove dictionary entries |
| URL-segment-versioned (`v{version:apiVersion}` in the template) | Throws `InvalidOperationException` | Silently honouring it would let `LinkGenerator` fill the segment from ambient route data and emit a URL with the wrong version. Switch to the per-request implicit overload, which resolves the segment from ambient route data and validates cross-route target-version support |
| Has version metadata but the pin does not map to the target (including an action narrowed by `[MapToApiVersion]`) | Throws `InvalidOperationException` | Choose a version actually mapped to the target or use the per-request overload |

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

- **TRLS023** (`Trellis.Analyzers`) — warns on `HttpResponseOptionsBuilder<T>.CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` calls inside `[ApiVersion]`-decorated controllers when the chain is not followed by `.WithVersionedRoute(...)` (or the manual primitive `.WithRouteValueResolver("api-version", ...)`, matched case-insensitively) and the route values dictionary literal does not include an `"api-version"` key. Code fix appends `.WithVersionedRoute()` and adds `using Trellis.Asp.ApiVersioning;` when missing. Detection is unchanged; only `WithVersionedRoute` supplies the target-aware runtime checks. Manual suppression is not equivalent validation.
