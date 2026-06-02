# Trellis.Core

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)

Railway-oriented error handling for .NET with `Result<T>`, `Maybe<T>`, and typed errors.

## Installation
```bash
dotnet add package Trellis.Core
```

## Quick Example
```csharp
using Trellis;

Result<string> email = Result.Ok("ada@example.com")
    .Ensure(value => value.Contains('@'),
        Error.InvalidInput.ForField("email", "validation.error", "Email is invalid."))
    .Map(value => value.Trim().ToLowerInvariant());
```

## Key Features
- Compose success and failure paths with `Bind`, `Map`, `Tap`, and `Ensure`.
- Model optional data with `Maybe<T>` instead of `null`.
- Return typed errors that map cleanly to APIs, logs, and tests.
- Use `AsTask()` / `AsValueTask()` to return synchronous `Result` chains from async-shaped APIs.
- Build resource-aware HTTP errors tersely with `ResourceRef.For<TResource>(id)`.
- Define custom `Required*<TSelf>` value objects with source-generated parsing, JSON conversion, and tracing support.
- Persist staged state alongside a failure with `Result.FailAfterCommit<T>(error)` — opt-in for background-worker handlers that need a permanent-failure transition to commit even though the handler returns a failed result.
- Classify `Error` values into `Transient` / `Permanent` / `FailFast` retry buckets with `error.Classify()` / `error.IsTransient()` / `error.GetRetryAdvice()` — transport-neutral helpers for worker, consumer, and outbound-gateway retry loops.

## `Result<T>` is not directly JSON-serializable

`Result<T>` carries a default `[JsonConverter]` that throws `NotSupportedException` on direct `JsonSerializer.Serialize` / `Deserialize`. Returning a raw `Result<T>` from a controller would otherwise hand MVC a domain disposition whose public JSON surface is only status/error metadata such as `{"IsSuccess": true, "IsFailure": false, "Error": null}`; the success value stays private, and `Error.*` cases still get no HTTP status-code mapping (an `Error.NotFound` would render as 200 OK instead of 404). The throw fires at the first request with an actionable message. Fix paths:

- **HTTP** — call `.ToHttpResponse()` (Trellis.Asp). The returned `Microsoft.AspNetCore.Http.IResult` writes the body itself; the struct never reaches STJ.
- **Non-HTTP** — unwrap with `Match` / `TryGetValue` before serialization.
- **Genuinely need raw JSON** (logging, IPC) — register a converter (or a `JsonConverterFactory`) in `JsonSerializerOptions.Converters`; option-registered converters take precedence over the type's `[JsonConverter]` attribute. **The override must match the declared static type:** `JsonConverter<Result<T>>` covers only `Result<T>`-declared values; `IResult<T>`-declared values need `JsonConverter<IResult<T>>`; `IResult`-declared values need `JsonConverter<IResult>`. Use a `JsonConverterFactory` to cover multiple shapes at once.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
