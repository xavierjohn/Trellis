# Trellis.Asp.ApiVersioning

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.ApiVersioning.svg)](https://www.nuget.org/packages/Trellis.Asp.ApiVersioning)

API-versioning helpers for `Trellis.Asp` — auto-inject the `api-version` route value into builder-generated URLs so responses round-trip the requested version under query/header API versioning. Covers `Location` headers (`WithVersionedRoute()`) and paginated next-page URLs (`HttpContext.PageUrl(...)`).

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
//   ↑ Location header includes ?api-version=<requested-version> automatically.

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

`WithVersionedRoute` inspects the **current request endpoint**, not the `Location` target; verify cross-route compatibility yourself, including for explicit pins. `PageUrl` instead inspects its named target. Automatic resolution uses a declared requested version, then a single version in the distinct implicit/explicit declared union, then a declared host default; otherwise it throws.

## Why
Under query/header API versioning, `Location` headers from `CreatedAtRoute(...)` / `CreatedAtAction(...)` / `WithLocation(...)` silently omit the `api-version` parameter unless every author remembers to add it to the route values dictionary — a recurring source of dereference 404s that's invisible without integration tests. `WithVersionedRoute()` injects the version at request time using the configured `IApiVersionReader` chain, with sensible fallbacks and explicit failures for ambiguous configurations.

## Key Features
- `WithVersionedRoute()` composes with `CreatedAtRoute(...)`, `CreatedAtAction(...)`, `WithLocation(...)`, and any other builder-generated Location method
- `HttpContext.PageUrl(routeName, ...)` returns a `Func<Cursor, int, string>` for the `nextUrlBuilder` parameter of paginated `ToHttpResponse(Async)` — replaces hand-rolled URL concatenation, version literals, and `Uri.EscapeDataString` calls with one helper
- The direction-aware `PageUrl(routeName, (cursor, direction, applied) => ...)` overload returns `Func<Cursor, PageDirection, int, string>` for `urlBuilder`, so `PageDirection.Next` / `PageDirection.Previous` can select distinct `after` / `before` keys while preserving API versions. Explicit-version pinning supports the same callback shape.
- Per-request resolution via `httpContext.RequestedApiVersion` (the `Asp.Versioning.Http` extension property), falling back to declared and default versions
- Explicit-version overloads for both `WithVersionedRoute(ApiVersion)` and `PageUrl(routeName, version, ...)` — pin cross-version Location / next-page URLs
- Honours `[ApiVersionNeutral]` endpoints and unversioned hosts (endpoints with no `ApiVersionMetadata`) by skipping injection — applies to all overloads, including explicit pinning. For URL-segment versioning the per-request `PageUrl` / `WithVersionedRoute()` overloads and the explicit `WithVersionedRoute(ApiVersion)` overload skip query injection (ambient routing fills the path segment); the explicit `PageUrl(routeName, version, ...)` overload throws instead, because silently dropping the pin would let `LinkGenerator` emit a URL with the wrong path-segment version
- Logs a single mid-migration warning per `(endpoint, AppDomain)` pair through the `Trellis.Asp.ApiVersioning` `ILogger` category when `.WithVersionedRoute()` runs against an endpoint with no `ApiVersionMetadata` (i.e. host removed `services.AddApiVersioning(...)` but left the chain in place); set `TrellisAspOptions.FailFastOnSilentVersionInjection = true` (e.g. only in Development) to throw `InvalidOperationException` on every offending request instead. `HttpContext.PageUrl(...)` stays silent — its return value is inspected by the caller so a missing `api-version` is visible there
- Throws on degenerate configurations (multi-version action with no client-requested version and no `DefaultApiVersion`) instead of silently picking

## Documentation
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
