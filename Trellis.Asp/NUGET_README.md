# Trellis.Asp

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.svg)](https://www.nuget.org/packages/Trellis.Asp)

ASP.NET Core integration for Trellis results, scalar value validation, and clean HTTP responses.

## Installation
```bash
dotnet add package Trellis.Asp
```

## Quick Example
```csharp
using Trellis;
using Trellis.Asp;

// Greenfield default — error mapping + scalar-value JSON / model-binding validation:
builder.Services.AddTrellisAspWithScalarValidation();

app.MapGet("/widgets/{id}", (string id) =>
    Result.Ok(id).ToHttpResponse());
```

> `AddTrellisAsp()` registers error-to-status mapping only. `AddTrellisAspWithScalarValidation()` (or `AddTrellisAsp()` + `AddScalarValueValidation()`) additionally configures global `MvcOptions` / `JsonOptions` for value-object binding and JSON serialization. The split exists so the global-options mutation is opt-in instead of silent.

## Key Features
- Convert `Result<T>` and `Error` values into consistent HTTP responses.
- Return `Result<Page<T>>` as a paginated envelope with matching RFC 8288 `Link` headers. Use `ToHttpResponse((cursor, direction, limit) => ..., body)` with `PageDirection.Next` / `PageDirection.Previous` for distinct URLs; the two-argument `nextUrlBuilder` convenience remains supported for both links. Task and ValueTask variants use `ToHttpResponseAsync`.
- Validate Trellis scalar values during model binding and JSON deserialization.
- Support controller and minimal API styles, including AOT-friendly setups.
- Emit [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) Problem Details with `instance` populated from the request path so clients can correlate failures with the originating request.
- Ship the canonical ProblemDetails recipe via `AddTrellisProblemDetails()` + `UseTrellisProblemDetails()` (trace id from `Activity.Current`, friendly 500 detail, `allow` array on 405). Composes with any consumer `CustomizeProblemDetails` callback so the application keeps the last word on collisions.
- Make `POST` / `PATCH` retry-safe with the opt-in IETF `Idempotency-Key` middleware (`AddTrellisIdempotency(...)` + `AddInMemoryIdempotencyStore()` + `UseTrellisIdempotency()`). Mark endpoints with `[Idempotent]`; the middleware buffers the request body, fingerprints `(method, path, headers, body)`, scopes by actor (default), and replays captured responses verbatim on retries.
- Compose a worker/system actor outside HTTP via `services.AddTrellisWorkerActor(systemActor)`. Wraps the existing unkeyed `IActorProvider` so HTTP requests still resolve through it and background-worker scopes (no `HttpContext`) resolve to the supplied system actor.
- Handle nested-JSON identity-provider claims (Auth0 `app_metadata.roles`, Azure B2C `extension_*`, some Okta token shapes) via `services.AddNestedJsonPathClaimsActorProvider(...)`. Configure a `ContainerClaim` plus dotted `ActorIdPath` / `PermissionsPath`; falls back through the inherited flat-claim resolver when the path misses or the container is malformed. The base `ClaimsActorProvider` also gains once-per-app-lifetime diagnostics that surface the silent-403 footgun when the configured `PermissionsClaim` resolves to zero entries on an authenticated identity or to a JSON-shaped scalar value, and the silent-401 footgun when the configured `ActorIdClaim` resolves nothing on an authenticated identity.
- Map Azure App Service / Container Apps built-in authentication ("Easy Auth") principal headers to an `Actor`: `AddAuthentication(...).AddEasyAuth()` decodes `X-MS-CLIENT-PRINCIPAL` (with `-ID` / `-NAME` fallback) onto `HttpContext.User`, then `services.AddEasyAuthActorProvider(...)` maps its claims and varies the response cache by the platform principal headers instead of `Authorization`. Trust precondition: enable only when the app is reachable exclusively through the Easy Auth front end.
- For microservices that consume gateway-minted internal JWTs, use the separately-packaged [`Trellis.Microservices.AspNetCore`](https://github.com/xavierjohn/Trellis.Microservices) (the `TrellisInternalJwtActorProvider` + `AddTrellisInternalJwtActorProvider` extension that previously lived under `Trellis.Asp.Authorization` moved to that repo, along with `Trellis.Yarp`).

## Destination-aware Location customization

`HttpResponseOptionsBuilder<T>.WithLocationRouteResolver(Action<LocationRouteContext>)` optionally customizes route values at Location generation after the domain selector and all `WithRouteValueResolver` callbacks. It observes the final destination regardless of configuration order. `LocationRouteContext` exposes get-only `HttpContext`, `RouteName`, `ActionName`, `ControllerName`, and mutable `RouteValues` on a per-execution copy; shared selector dictionaries are not changed.

One callback slot means the last registration wins, including `WithVersionedRoute(...)` from `Trellis.Asp.ApiVersioning`; errors propagate. Literal/selector `Created(...)` and `WriteOutcome`-owned URIs are unaffected. `CreatedAtRoute` / `CreatedAtAction` retain 201, `WithLocation` retains normal 2xx, and action links retain their trimming/AOT limitations (named routes remain AOT-compatible). No new service registration is required.

## Response and converter compatibility

`Error.ToHttpResponse(o => o.Vary("Accept-Language"))` appends case-insensitively unique `Vary` values without overwriting existing middleware headers. The non-generic error-only builder no longer exposes the previously ineffective `HonorPrefer()` method; remove that call when upgrading. Generic success builders retain `HonorPrefer()`.

Generated scalar converters now report the same null, blank and primitive-format reason codes as the reflection-mode converters for supported primitives. Structured validation failures retain their codes, args and locations through `AddBodyError`. JSON null remains valid for optional scalars using `MaybeScalarValueJsonConverter<TValue, TPrimitive>`.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
