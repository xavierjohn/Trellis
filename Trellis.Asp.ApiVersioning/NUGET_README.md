# Trellis.Asp.ApiVersioning

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.ApiVersioning.svg)](https://www.nuget.org/packages/Trellis.Asp.ApiVersioning)

API-versioning helpers for `Trellis.Asp` — generate URLs using the destination's actual version mappings. Covers target-aware query/segment `Location` headers (`WithVersionedRoute()`) and paginated next-page URLs (`HttpContext.PageUrl(...)`).

## Installation
```bash
dotnet add package Trellis.Asp.ApiVersioning
```

## Quick Example
```csharp
using Trellis;
using Trellis.Asp;
using Trellis.Asp.ApiVersioning;

result.ToHttpResponse(opts => opts
    .CreatedAtRoute("Customers_GetById", c => c.Id.Value)
    .WithVersionedRoute());
//   ↑ Query-style Location carries a version mapped to Customers_GetById.

// Paginated list — versioned next-page URL with one helper call:
return pageResult.ToHttpResponse(
    nextUrlBuilder: HttpContext.PageUrl(
        "Orders_GetOverdue",
        (c, applied) => new RouteValueDictionary { ["cursor"] = c.Token, ["limit"] = applied }),
    body: o => OrderListItemResponse.From(o));
//   ↑ next URL carries the api-version, fills URL-segment templates from ambient route data,
//     skips injection on [ApiVersionNeutral] endpoints and on endpoints with no
//     ApiVersionMetadata (unversioned hosts), and URL-encodes the cursor token.
```

`WithVersionedRoute()` chains after any builder method that emits a builder-generated `Location` header — including `CreatedAtRoute(...)` / `CreatedAtAction(...)` (201 Created) and `WithLocation(...)` (2xx state-transition responses on existing resources).

`WithVersionedRoute` resolves the **final destination**, not the current request endpoint, using public ASP.NET Core endpoint-address schemes. Suppressed link-generation endpoints are excluded. Missing or multiple candidates throw `InvalidOperationException`; use a uniquely named destination route to avoid ambiguity.

Both Location and `PageUrl` accept a requested version only when `metadata.IsMappedTo(version)`. Otherwise they use a single version from `metadata.Map(ApiVersionMapping.Implicit | ApiVersionMapping.Explicit).DeclaredApiVersions` filtered by `IsMappedTo`, then a mapped `DefaultApiVersion`, otherwise throw. `[MapToApiVersion]` narrows controller declarations: their union is not the action's accepted version set. Explicit pins must map to the target.

## Why
Under query/header API versioning, `Location` headers from `CreatedAtRoute(...)` / `CreatedAtAction(...)` / `WithLocation(...)` silently omit the `api-version` parameter unless every author remembers to add it to the route values dictionary — a recurring source of dereference 404s that's invisible without integration tests. `WithVersionedRoute()` injects the version at request time using the configured `IApiVersionReader` chain, with sensible fallbacks and explicit failures for ambiguous configurations.

## Key Features
- `WithVersionedRoute()` composes with `CreatedAtRoute(...)`, `CreatedAtAction(...)`, `WithLocation(...)`, and any other builder-generated Location method
- `HttpContext.PageUrl(routeName, ...)` returns a `Func<Cursor, int, string>` for the `nextUrlBuilder` parameter of paginated `ToHttpResponse(Async)` — replaces hand-rolled URL concatenation, version literals, and `Uri.EscapeDataString` calls with one helper
- The direction-aware `PageUrl(routeName, (cursor, direction, applied) => ...)` overload returns `Func<Cursor, PageDirection, int, string>` for `urlBuilder`, so `PageDirection.Next` / `PageDirection.Previous` can select distinct `after` / `before` keys while preserving API versions. Explicit-version pinning supports the same callback shape.
- Per-request resolution via `httpContext.RequestedApiVersion` (the `Asp.Versioning.Http` extension property), falling back to mapped declared and default target versions
- Explicit-version overloads for both `WithVersionedRoute(ApiVersion)` and `PageUrl(routeName, version, ...)` — pin cross-version Location / next-page URLs
- Location writes resolved/pinned versions into the target's actual `:apiVersion` parameter name (not necessarily `version`), removing duplicate `api-version` query values. Query-style targets use conventional `api-version`.
- Neutral and missing-metadata targets skip injection; Location also removes supplied `api-version` entries. Missing metadata warns once per **destination endpoint / AppDomain** under `Trellis.Asp.ApiVersioning`, identifying the target. Set `TrellisAspOptions.FailFastOnSilentVersionInjection = true` to throw on every offending execution. Neutral targets and missing-metadata `PageUrl` targets remain quiet.
- Warning deduplication uses weak endpoint-instance identity, not display names or route templates. Same-named destinations in different hosts warn independently, without retaining discarded endpoints.
- Location's `WithLocationRouteResolver` hook runs after the selector and all legacy `WithRouteValueResolver` callbacks on a per-execution clone. Version values are overridden regardless of configuration order. One callback slot: the last registration wins, including repeated `WithVersionedRoute` calls; errors propagate.
- Literal/selector `Created(...)` and `WriteOutcome`-owned URIs are unchanged. Named routes remain AOT-compatible; `CreatedAtAction` retains its trimming/AOT limitations.

## Migration

Keep the existing `WithVersionedRoute()` / `WithVersionedRoute(ApiVersion)` syntax. Missing/ambiguous targets and unsupported pins now fail; segment pins are honored; supplied `api-version` values are removed for neutral/unversioned destinations; warnings now identify the destination rather than the caller. No new registration is required.

`PageUrl` changes only its shared version-mapping checks. Implicit PageUrl still uses ambient segment routing and existing cross-route validation; consumer version overrides remain unchanged. Skipping injection **does not remove consumer-supplied `api-version` entries**, even for neutral or missing-metadata targets. Only Location's `WithVersionedRoute` owns/removes/overrides version entries. **Explicit PageUrl still rejects URL-segment pins**, unlike Location.

## Documentation
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
