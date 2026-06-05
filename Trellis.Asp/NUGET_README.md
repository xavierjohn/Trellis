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
- Validate Trellis scalar values during model binding and JSON deserialization.
- Support controller and minimal API styles, including AOT-friendly setups.
- Emit [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) Problem Details with `instance` populated from the request path so clients can correlate failures with the originating request.
- Ship the canonical ProblemDetails recipe via `AddTrellisProblemDetails()` + `UseTrellisProblemDetails()` (trace id from `Activity.Current`, friendly 500 detail, `allow` array on 405). Composes with any consumer `CustomizeProblemDetails` callback so the application keeps the last word on collisions.
- Make `POST` / `PATCH` retry-safe with the opt-in IETF `Idempotency-Key` middleware (`AddTrellisIdempotency(...)` + `AddInMemoryIdempotencyStore()` + `UseTrellisIdempotency()`). Mark endpoints with `[Idempotent]`; the middleware buffers the request body, fingerprints `(method, path, headers, body)`, scopes by actor (default), and replays captured responses verbatim on retries.
- Compose a worker/system actor outside HTTP via `services.AddTrellisWorkerActor(systemActor)`. Wraps the existing unkeyed `IActorProvider` so HTTP requests still resolve through it and background-worker scopes (no `HttpContext`) resolve to the supplied system actor.
- Handle nested-JSON identity-provider claims (Auth0 `app_metadata.roles`, Azure B2C `extension_*`, some Okta token shapes) via `services.AddNestedJsonPathClaimsActorProvider(...)`. Configure a `ContainerClaim` plus dotted `ActorIdPath` / `PermissionsPath`; falls back through the inherited flat-claim resolver when the path misses or the container is malformed. The base `ClaimsActorProvider` also gains once-per-app-lifetime diagnostics that surface the silent-403 footgun when the configured `PermissionsClaim` resolves to zero entries on an authenticated identity or to a JSON-shaped scalar value.
- Hydrate the FULL `Actor` surface (id + permissions + forbidden permissions + ABAC attributes) from a gateway-minted internal JWT via `services.AddTrellisInternalJwtActorProvider(...)`. Enforces a strict sentinel + count claim contract that defends the deny-overrides-allow invariant against a proxy stripping the deny set.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
