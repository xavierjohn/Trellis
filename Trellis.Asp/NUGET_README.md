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
- Supports created-resource locations, ETags, conditional requests, `Prefer`, cache controls, and pagination links.
- Validates scalar value objects during MVC and Minimal API binding and JSON deserialization.
- Provides claims, Entra, Easy Auth, development, caching, and worker actor-provider composition.
- Maps ASP.NET Core rate-limit middleware rejections to the standard Trellis 429 Problem Details envelope and `Retry-After`.
- Adds opt-in `Idempotency-Key` middleware with pluggable stores.
- Includes the AOT-friendly scalar JSON-converter source generator.

Minimal API scalar validation also requires `app.UseScalarValueValidation()` and `.WithScalarValueValidation()` on participating endpoints. Version-aware `Location` and pagination links live in `Trellis.Asp.ApiVersioning`.

For ASP.NET Core rate limiting, keep policies and partitioning in the application and call
`options.UseTrellisRejectionHandler()` inside `AddRateLimiter(...)`. The adapter owns
`RateLimiterOptions.OnRejected` and writes the same Trellis Problem Details envelope as endpoint
failures. Use named endpoint policies without policy-level `OnRejected` callbacks; ASP.NET Core
gives those callbacks precedence and skips the options handler for inline
`RequireRateLimiting(policy)` even when the policy callback is null.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-asp.html)
- [ASP.NET Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [API-versioning package](https://www.nuget.org/packages/Trellis.Asp.ApiVersioning)
