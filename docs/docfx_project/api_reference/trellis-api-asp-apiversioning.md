---
package: Trellis.Asp.ApiVersioning
namespaces: [Trellis.Asp.ApiVersioning]
types: [HttpResponseOptionsBuilderApiVersioningExtensions, HttpContextPageUrlExtensions, TrellisAspOptionsApiVersioningExtensions]
version: v1
last_verified: 2026-10-08
audience: [llm]
agent_usage: onDemand
agent_description: "Open when versioned controllers return Result or Page and need Location or next-page URLs that carry the api-version (Trellis.Asp.ApiVersioning)."
---
# Trellis.Asp.ApiVersioning — API Reference

## Header

- **Package:** `Trellis.Asp.ApiVersioning`
- **Namespace:** `Trellis.Asp.ApiVersioning`
- **Purpose:** Target-aware API-versioning policies for URLs emitted by `Trellis.Asp`. `HttpResponseOptionsBuilderApiVersioningExtensions` versions `Location` headers from `CreatedAtRoute(...)` / `CreatedAtAction(...)` (201 Created) and `WithLocation(...)` (normal 2xx on existing resources). It resolves the actual destination and writes the supported version into its `:apiVersion` segment parameter or the conventional `api-version` query route value. Common pagination builders belong to `Trellis.Asp.HttpContextPaginationExtensions`; `TrellisAspOptionsApiVersioningExtensions.UseVersionedPageUrls` enables version-aware implicit builders, while this package's `HttpContextPageUrlExtensions` retains typed pins. Implicit segment links use ambient routing; explicit pagination pins reject segment targets. Neutral or missing-metadata targets skip injection; Location additionally removes supplied `api-version` values.

See also: [trellis-api-asp.md](trellis-api-asp.md#httpresponseoptionsbuildertdomain) — the underlying `HttpResponseOptionsBuilder<T>`, `CreatedAtRoute`, `CreatedAtAction`, `WithLocation`, and destination-aware `WithLocationRouteResolver` hook this package builds on. [trellis-api-analyzers.md](trellis-api-analyzers.md#error-ef-core-and-value-object-rules) — `TRLS023` warns on `CreatedAtRoute` / `CreatedAtAction` / `WithLocation` calls in versioned controllers that omit the `api-version` route value, and offers a code fix that chains `.WithVersionedRoute()`.

## Use this file when

- You return `Result<T>` or a Created `Result<WriteOutcome<T>>` from versioned controllers (`[ApiVersion("…")]`) and need a builder-generated `Location` to carry a destination-supported `api-version`.
- You return paginated `Result<Page<T>>` responses from versioned controllers and need the `next`-page URL (the `nextUrlBuilder` parameter of `ToHttpResponse(Async)`) to carry the version, honor URL-segment ambient route values, and skip injection on neutral endpoints — without hard-coding the version literal or hand-rolling URL encoding.
- You configured `Asp.Versioning` with query, header, URL-segment, or composite readers and need Location links to carry a version accepted by their destination.
- You need destination-aware Location customization (`WithLocationRouteResolver`), after ordinary per-request values (`WithRouteValueResolver`) have been applied.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Enable version-aware common pagination builders | `services.AddTrellisAsp(o => o.UseVersionedPageUrls())`, or configure the existing `UseAsp` builder slot; still register `Asp.Versioning` normally | [`TrellisAspOptionsApiVersioningExtensions`](#trellisaspoptionsapiversioningextensions) |
| Return 201 Created with versioned Location | Chain `.WithVersionedRoute()` after `CreatedAtRoute(...)` or `CreatedAtAction(...)` | [`HttpResponseOptionsBuilderApiVersioningExtensions`](#httpresponseoptionsbuilderapiversioningextensions) |
| Return 200 OK with versioned Location on an existing resource | Chain `.WithVersionedRoute()` after `WithLocation(...)` | [`WithLocation` composition](#withlocation-composition) |
| Single id route value | `CreatedAtRoute(routeName, x => x.Id).WithVersionedRoute()` (uses the single-id overload from `Trellis.Asp`) | [Composition examples](#composition-examples) |
| Multi-key route values | `CreatedAtRoute(routeName, x => new RouteValueDictionary { ["tenantId"] = x.TenantId, ["id"] = x.Id }).WithVersionedRoute()` | [Composition examples](#composition-examples) |
| Pin Location to a specific version (rare) | `CreatedAtRoute(...).WithVersionedRoute(new ApiVersion(new DateOnly(2026, 12, 1)))` | [Explicit-version overload](#explicit-version-overload) |
| Paginated list — emit versioned next-page URL | `HttpContext.PageUrl(routeName, (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied })` passed as the `nextUrlBuilder` argument of `ToHttpResponse(Async)` | [`HttpContextPageUrlExtensions`](#httpcontextpageurlextensions) |
| Paginated list — distinct versioned next/previous URLs | `HttpContext.PageUrl(routeName, (cursor, direction, applied) => ...)` passed as the `urlBuilder` argument of `ToHttpResponse(Async)`; choose `after` / `before` keys using `Trellis.Asp.PageDirection` | [`Directional pagination`](#directional-pagination) |
| Paginated list — share a route name across namespace-versioned controllers | Keep the same name and route template/defaults; implicit self-pagination uses the active endpoint, independently of endpoint registration order | [`PageUrl` behavioral notes](#pageurl-behavioral-notes) |
| Paginated list — pin next-page URL to a specific version | `HttpContext.PageUrl(routeName, new ApiVersion(new DateOnly(2026, 12, 1)), (c, applied) => …)` | [`PageUrl` explicit-version overload](#pageurl-explicit-version-overload) |
| Link to an `[ApiVersionNeutral]` destination | `.WithVersionedRoute()` skips injection and removes supplied `api-version` values from the cloned dictionary | [Behavioral notes](#behavioral-notes) |
| URL-segment Location, including cross-route links or explicit pins | `.WithVersionedRoute(...)` writes the resolved version into the target's actual `:apiVersion` parameter name, without a duplicate query value | [Behavioral notes](#behavioral-notes) |
| Detect mid-migration silent skips (`.WithVersionedRoute()` left in place after `AddApiVersioning(...)` was removed) | Default: warn once per `(endpoint, AppDomain)` to the `Trellis.Asp.ApiVersioning` `ILogger` category. Opt-in fail-fast: `services.AddTrellisAsp(o => o.FailFastOnSilentVersionInjection = true)` | [Behavioral notes](#behavioral-notes) |

## Common traps

- Implicit `PageUrl` overloads now belong to `Trellis.Asp`, not this package. Import `Trellis.Asp` and configure `UseVersionedPageUrls()` once in a versioned host. Omitting it selects ordinary unversioned routing; installing the package or calling `AddApiVersioning()` alone does not enable the pagination policy. Explicit pins and Location chains remain self-contained.
- Do **not** supply competing version values when chaining `.WithVersionedRoute()`. It owns version route values after the selector and **all** `WithRouteValueResolver` callbacks, regardless of configuration order. It overwrites query/segment version values or removes `api-version` for segment, neutral, and missing-metadata targets.
- Do not treat `PageUrl` segment behavior as identical to Location: Location writes and honors segment pins; implicit `PageUrl` retains ambient routing and cross-route validation, and explicit `PageUrl` still rejects segment pins.
- Location destinations must be uniquely addressable: zero or multiple link-generation candidates throw rather than falling back to the current endpoint. `PageUrl` supports versioned variants sharing a name and matching templates, defaults, required values, and parameter policies, with a narrow exception for non-URL MVC selector metadata: implicit self-pagination uses the active endpoint; cross-route links and explicit pins require a uniquely mapped destination. Suppressed link-generation endpoints are excluded by both helpers.
- `WithLocation(...)` does not change status: typically 200 for `Result<T>`, or 201 when it supplies a missing `WriteOutcome.Created` location. Builder route/action fallbacks run version resolution when the outcome Location is null, empty, or whitespace. Nonblank outcome locations and Accepted monitor URIs are not rewritten; other outcome variants do not use builder locations.
- The route values selector must return a non-null `RouteValueDictionary`. The runtime clones it before applying resolvers, so callbacks do not mutate a shared selector dictionary. Application code must not modify a shared dictionary concurrently.
- For a target with multiple mapped declared versions, supply a mapped requested version or configure a mapped `DefaultApiVersion`. If neither is available, resolution throws rather than silently picking one. Controller declarations alone do not make an action multi-version.
- `HttpContext.PageUrl(routeName, ...)` requires the target action to carry a route name (`[HttpGet("...", Name = "Things_List")]`). Without a name the helper cannot resolve the endpoint and the returned builder throws `InvalidOperationException` on first invocation. The route name typically matches the current paginated endpoint (self-referential pagination) but cross-route pagination is supported — supply path parameters in the callback's `RouteValueDictionary` for the target route's template.

## Types

### `TrellisAspOptionsApiVersioningExtensions`

`public static TrellisAspOptions UseVersionedPageUrls(this TrellisAspOptions options)`
sets `TrellisAspOptions.PageUrlRouteResolver` to the optional destination-aware version
policy. Configure once through `services.AddTrellisAsp(o => o.UseVersionedPageUrls())`
or `services.AddTrellis(o => o.UseAsp(asp => asp.UseVersionedPageUrls()))`.
Normal `AddApiVersioning(...)` configuration is still required. Null options throw;
repeated calls replace the pagination policy without resetting unrelated ASP options.
Core implicit builders now live in `Trellis.Asp`; this package retains only typed pins.
Pins supply a per-builder policy and need no host pagination-policy registration.

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
- Warning deduplication uses endpoint instance identity, not display names or route templates. Distinct endpoints, including same-named destinations in separate hosts, warn independently. Weak keys allow discarded endpoints to be collected; concurrent calls for the same endpoint still produce only one warning.
- There is one `WithLocationRouteResolver` callback slot: the last registration wins, including repeated `WithVersionedRoute` calls or a custom hook that replaces one. Callback errors propagate; shared selector dictionaries are not mutated.
- Literal/selector `Created(...)` locations, nonblank outcome locations, and Accepted monitor URIs ignore the hook. Route/action fallbacks for `WriteOutcome.Created` run it and retain the outcome's 201, including when configured with `WithLocation`. Named routes remain AOT-compatible; `CreatedAtAction` retains its existing trimming/AOT limitations.

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

Unlike `CreatedAtRoute`, `WithLocation` does **not** force 201: ordinary `Result<T>` retains
200, while a Created `WriteOutcome<T>` retains its natural 201. It supplies and versions that
outcome's location only when the outcome has no nonblank Location. Explicit outcome locations,
Accepted monitor URIs, and other outcome variants are unchanged.

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
| `PageUrl(this HttpContext httpContext, string routeName, ApiVersion version, Func<Cursor, int, RouteValueDictionary> routeValues)` | `Func<Cursor, int, string>` | Pins the next-page URL to a specific `ApiVersion`. The pin is silently skipped on neutral and missing-metadata targets. Unlike Location, URL-segment targets still throw `InvalidOperationException` rather than writing the pin into the segment. For a versioned query-style target, the version must map to the destination or the pin throws; controller declarations do not override an action's narrower mappings. Existing consumer-value precedence is unchanged. |

The implicit builders and their `PageUrlRouteContext` policy hook are documented in
[`Trellis.Asp`](trellis-api-asp.md#httpcontextpaginationextensions). With
`UseVersionedPageUrls` enabled, implicit resolution remains mapped requested version →
single mapped declared target version → mapped default → throw. Consumer query versions
win; neutral, unversioned, and segment targets skip injection without removing consumer
values. The optional policy also preserves shared-name selection and segment validation.

#### Directional pagination

The common two- and three-argument callback overloads live in `Trellis.Asp`.
This package supplies the direction-aware typed-pin overload:

```csharp
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
`PathBase`. Implicit builders require the host's `UseVersionedPageUrls` policy; typed
pins supply their own per-builder policy and require no pagination-policy registration.

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

- **Candidate caching does not cache versions.** The common ASP builder caches discovery and compatible route groups per endpoint data source, invalidated by its change token. The optional versioning policy still resolves each link independently, including requested versions, consumer overrides, active endpoints, and explicit pins.
- **Self-referential pagination is the common case.** A paginated list endpoint typically supplies its own route name to `PageUrl(...)`: the next-page URL targets the same action with a different `cursor`. The implicit overload uses the active `HttpContext.GetEndpoint()` when its name matches and it is link-enabled, including when middleware wraps the matched endpoint. Separate namespace-versioned controllers may share the route name `"Orders_List"` without dated suffixes; next/previous URLs retain the active version independently of endpoint registration order. Target version-support checks are shared with Location, but PageUrl retains its own segment, consumer-override, and missing-metadata behavior.
- **Shared names require equivalent URL generation.** Named version variants must use the same route template, defaults, required values, and parameter policies. MVC selector keys `controller`, `action`, and `area` may differ only when they are not template parameters and each endpoint has a matching default/required value for the key. This allows differently named attribute-routed controllers while keeping `{controller}/{action}` differences unsafe. Other non-template defaults remain checked because they can suppress explicit query values, including cursors. Inline policies compare by content; out-of-line policy objects must compare equal (normally the same instance). URL-affecting differences throw instead of letting named URL generation fall through to an unrelated or differently constrained destination. The helper does not add or rewrite route names; genuinely distinct destinations should keep distinct names.
- **Cross-route pagination is supported.** Pass any registered route name. A single link-enabled destination retains the mapped requested → single mapped declared → mapped default resolution order. For multiple destinations, a consumer-supplied or requested version must identify exactly one candidate using its actual action mappings; if none accepts it, a configured mapped default may select one. More than one match throws rather than breaking the tie by registration order. A consumer-supplied segment value, otherwise an ambient segment value, takes precedence over the parsed requested version when selecting segment variants. Supply the target's required path parameters (besides ambient ones that `LinkGenerator` fills automatically from the current request's route values) in the callback's `RouteValueDictionary`.
- **Explicit pins select their own destination.** A pin to another version selects that version's named variant rather than reusing the active endpoint. The pin must identify a unique mapped destination and still overrides consumer query values on versioned targets. Neutral/unversioned skip rules and rejection of explicit URL-segment pins remain unchanged.
- **URL-segment versioning works without consumer awareness.** When the target route template carries a `{version:apiVersion}` segment, `LinkGenerator.GetUriByRouteValues(httpContext, ...)` fills the segment from ambient route data. The helper skips automatic `api-version` query injection; it does not remove a query value supplied by the consumer.
- **Skipping injection does not remove consumer values.** For neutral, missing-metadata, and implicit URL-segment targets, PageUrl preserves any consumer-supplied `api-version` entry. A manually supplied value can therefore appear even on a neutral target. Only Location's `WithVersionedRoute` owns/removes/overrides version entries; PageUrl's existing consumer precedence is unchanged.
- **`PathBase` is preserved.** Building absolute URLs through `LinkGenerator.GetUriByRouteValues(httpContext, ...)` carries the request's scheme, host, and `PathBase` into the emitted URL — important for hosts mounted under a virtual directory.
- **Request-scoped contract.** The returned `Func` captures `httpContext` and must be invoked during the same request that produced it — not handed off to a background `Task`. The framework's `ToHttpResponse(Async)` consumes the builder synchronously while building the response envelope, so the typical consumer call site honors this naturally.
- **Cross-route version validation.** The destination action must accept the version: `[MapToApiVersion]` narrows controller declarations, so sharing a controller version is not sufficient. For query-style links, an unsupported requested version falls through to a single mapped declared version, then a mapped `DefaultApiVersion`, then throws. URL-segment links retain ambient segment routing and existing cross-route target-version validation; PageUrl does not adopt Location's segment rewriting.
- **Failure modes.** The returned builder throws `InvalidOperationException` when (a) the target route name resolves to no link-enabled endpoint, (b) no requested, single declared, or default version maps to the versioned target, (c) `LinkGenerator.GetUriByRouteValues` returns `null` (the supplied + ambient route values do not match the target template), (d) the consumer's `routeValues` callback returns `null`, (e) the explicit-version overload targets a URL-segment-versioned route, (f) the explicit pin does not map to the target, or (g) shared-name destinations have incompatible templates, defaults, required values, or parameter policies outside the non-URL MVC selector exception, or cannot be uniquely selected. Suppressed candidates cannot supply version metadata or win selection.
- **Unversioned hosts compose cleanly.** When the target endpoint has no `ApiVersionMetadata`, both PageUrl overloads skip automatic injection silently rather than throwing an unresolvable-version error; the explicit overload drops its pin. Consumer-supplied version entries are retained, so the URL is version-free only when the consumer has not supplied one. This also applies to individual unversioned endpoints in mixed hosts.

**Migration to common pagination builders (deliberate alpha break).** Import `Trellis.Asp`
for implicit `PageUrl` calls and enable `UseVersionedPageUrls()` through the existing
ASP options registration. Static implicit calls to this package's
`HttpContextPageUrlExtensions.PageUrl` must move to `HttpContextPaginationExtensions.PageUrl`.
Typed-pin extension syntax is unchanged. No duplicate implicit extensions or legacy shim
remain. Unversioned consumers need only `Trellis.Asp` and no versioning registration.

**Migration from version-suffixed pagination names.** Identically routed versioned list
actions may use one name in both `[HttpGet(Name = "...")]` and `PageUrl(...)`. Existing
unique names remain supported. Follow emitted next/previous URLs and assert the responding
API version, not only query text. This does not relax Location's uniquely addressable
destination requirement.

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

Register API versioning normally, then enable the common pagination policy through the
existing ASP configuration surface. No new `IServiceCollection` registration helper or
`TrellisServiceBuilder` slot is added:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(new DateOnly(2026, 12, 1));
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("api-version"));
});

builder.Services.AddTrellisAsp(options => options.UseVersionedPageUrls());
```

With `Trellis.ServiceDefaults`, use the existing callback instead:

```csharp
builder.Services.AddTrellis(options => options
    .UseAsp(asp => asp.UseVersionedPageUrls()));
```

The extension is defined in this optional package; neither `Trellis.Asp` nor
`Trellis.ServiceDefaults` takes a dependency on `Asp.Versioning`. Repeated ASP callbacks
compose in registration order. Repeated `UseVersionedPageUrls` calls replace only the
pagination policy. Policies are host-local and resolve metadata per link, never global.
`WithVersionedRoute` needs no pagination policy and retains its existing setup.

The package depends on `Asp.Versioning.Http` (for `HttpContext.RequestedApiVersion`), `Asp.Versioning.Mvc`, and `Asp.Versioning.Mvc.ApiExplorer`.

## Related diagnostics

- **TRLS023** (`Trellis.Analyzers`) — warns on `HttpResponseOptionsBuilder<T>.CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` calls inside `[ApiVersion]`-decorated controllers when the chain is not followed by `.WithVersionedRoute(...)` (or the manual primitive `.WithRouteValueResolver("api-version", ...)`, matched case-insensitively) and the route values dictionary literal does not include an `"api-version"` key. Code fix appends `.WithVersionedRoute()` and adds `using Trellis.Asp.ApiVersioning;` when missing. Detection is unchanged; only `WithVersionedRoute` supplies the target-aware runtime checks. Manual suppression is not equivalent validation.
