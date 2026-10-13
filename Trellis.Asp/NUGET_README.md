# Trellis.Asp

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.svg)](https://www.nuget.org/packages/Trellis.Asp)

ASP.NET Core integration for mapping Trellis results to HTTP, binding scalar value objects, resolving actors, and applying protocol behavior such as Problem Details and idempotency.

## Installation

```bash
dotnet add package Trellis.Asp
```

## Quick Example

```csharp
using Trellis;
using Trellis.Asp;

builder.Services.AddTrellisAspWithScalarValidation();

app.MapGet("/widgets/{id}", (string id) =>
    Result.Ok(id).ToHttpResponse());
```

`AddTrellisAsp()` registers Result-to-HTTP mapping only. Use `AddTrellisAspWithScalarValidation()` when the application also binds Trellis scalar value objects from route, query, or JSON input.

## Key Features

- Converts `Result<T>`, `Result<Unit>`, `Result<Page<T>>`, and `Result<WriteOutcome<T>>` into consistent ASP.NET Core responses.
- Parses raw cursor/limit query input into `PageRequest` consistently across MVC and Minimal APIs without collapsing an empty cursor into absence.
- Builds named-route pagination URLs through `HttpContext.PageUrl(...)` without an API-versioning dependency.
- Caches pagination endpoint discovery and compatible route groups until routing changes, without caching request values or selected versions.
- Emits RFC 9457 Problem Details for typed Trellis failures.
- Supports created-resource locations, ETags, conditional requests, `Prefer`, cache controls, and pagination links.
- Supplies endpoint-builder locations for `WriteOutcome.Created` when its optional location is absent; an explicit outcome location wins.
- Validates scalar value objects during MVC and Minimal API binding and JSON deserialization.
- Provides claims, Entra, Easy Auth, development, caching, and worker actor-provider composition.
- Maps ASP.NET Core rate-limit middleware rejections to the standard Trellis 429 Problem Details envelope, with `Retry-After` when supplied by the rejected lease.
- Adds opt-in `Idempotency-Key` middleware with pluggable stores.
- Includes the AOT-friendly scalar JSON-converter source generator.

Minimal API scalar validation also requires `app.UseScalarValueValidation()` and `.WithScalarValueValidation()` on participating endpoints. Common `PageUrl` builders live here; versioned hosts install `Trellis.Asp.ApiVersioning` and configure `AddTrellisAsp(o => o.UseVersionedPageUrls())` (or `UseAsp`) once. Typed pins and `WithVersionedRoute()` remain optional-package helpers.

Scalar and composite JSON validation collects expected failures before the handler runs,
without enabling `ThrowOnBadRequest`. Malformed JSON remains 400; semantic
failures use the configured validation status (default 422). Scalar-validation middleware
leaves application-owned bare responses unchanged. Hosts can explicitly pair
`AddTrellisProblemDetails()` with `app.UseStatusCodePages()` to format bare responses,
including eligible application-owned ones, as Problem Details.

JSON options configured for scalar validation also reject missing/null required value-object
inputs outside a request scope, such as manual `ReadFromJsonAsync` or persisted-payload reads.
These reads throw `TrellisJsonValidationException` rather than returning invalid DTOs.

For ASP.NET Core rate limiting, keep policies and partitioning in the application and call
`options.UseTrellisRejectionHandler()` inside `AddRateLimiter(...)`. The adapter owns
`RateLimiterOptions.OnRejected` and writes the same Trellis Problem Details envelope as endpoint
failures. Use named endpoint policies without policy-level `OnRejected` callbacks; ASP.NET Core
gives those callbacks precedence and skips the options handler for inline
`RequireRateLimiting(policy)` even when the policy callback is null.

On .NET 10, fixed-window and token-bucket limiters normally supply retry estimates;
sliding-window and concurrency limiters do not. Queue eviction or limiter disposal can also
omit retry advice. Choose fixed window or token bucket when the header matters, but clients
must still handle a 429 without it; Trellis does not invent a fallback delay.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-asp.html)
- [ASP.NET Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [API-versioning package](https://www.nuget.org/packages/Trellis.Asp.ApiVersioning)
