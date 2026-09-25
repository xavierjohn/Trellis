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
- Emits RFC 9457 Problem Details for typed Trellis failures.
- Supports `Created`, named-route and action locations, ETags, conditional requests, `Prefer`, cache controls, and pagination links.
- Validates scalar value objects during MVC and Minimal API binding and JSON deserialization.
- Provides claims, nested-JSON claims, Entra, Easy Auth, development, caching, and worker actor-provider composition.
- Maps ASP.NET Core rate-limit middleware rejections to the standard Trellis 429 Problem Details envelope and `Retry-After`.
- Adds opt-in `Idempotency-Key` middleware with in-memory and pluggable distributed stores.
- Includes the AOT-friendly scalar JSON-converter source generator.

Default failure mappings include 401 for `AuthenticationRequired`, 403 for `Forbidden`, 404 for `NotFound`, 409 for `Conflict`, 422 for `InvalidInput` and `InvariantViolation`, 429 for `RateLimited`, and 503 for `Unavailable`. HTTP-only faults such as 405, 412, 416, and 428 flow through `Error.TransportFault`.

## Important Setup

- Minimal API scalar validation also requires `app.UseScalarValueValidation()` and `.WithScalarValueValidation()` on participating endpoints.
- `AddTrellisProblemDetails()` pairs with `app.UseTrellisProblemDetails()`.
- Inside `AddRateLimiter(...)`, call `options.UseTrellisRejectionHandler()` to let Trellis own `OnRejected`; use named endpoint policies without policy-level `OnRejected` callbacks. Inline `RequireRateLimiting(policy)` bypasses the options handler, even without a policy callback. Rate-limit policies and partitioning remain application-owned.
- Idempotency requires `AddTrellisIdempotency(...)`, exactly one store registration, `app.UseTrellisIdempotency()`, and `[Idempotent]` on opted-in endpoints.
- Version-aware `Location` and pagination links live in `Trellis.Asp.ApiVersioning`.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-asp.md)
- [ASP.NET Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [API-versioning package](https://www.nuget.org/packages/Trellis.Asp.ApiVersioning)

## Development

Run the package and source-generator tests from the repository root:

```powershell
dotnet test Trellis.Asp\tests\Trellis.Asp.Tests.csproj -c Release
dotnet test Trellis.Asp\generator-tests\Trellis.AspSourceGenerator.Tests.csproj -c Release
```
