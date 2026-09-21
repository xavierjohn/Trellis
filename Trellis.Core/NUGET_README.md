# Trellis.Core

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)

Railway-oriented error handling and domain foundations for .NET with `Result<T>`, `Maybe<T>`, typed errors, value-object bases, and DDD primitives.

## Installation

```bash
dotnet add package Trellis.Core
```

## Quick Example

```csharp
using Trellis;

Result<string> email = Result.Ok("ada@example.com")
    .Ensure(
        value => value.Contains('@'),
        Error.InvalidInput.ForField(
            "email",
            ValidationCodes.StringEmail,
            "Email is invalid."))
    .Map(value => value.Trim().ToLowerInvariant());
```

## Key Features

- Compose explicit success and failure paths with `Bind`, `Map`, `Tap`, `Ensure`, `Combine`, and their async variants.
- Model expected absence with `Maybe<T>` instead of `null` or exceptions.
- Return a closed set of typed errors that adapters can map consistently.
- Accumulate failures with `EnsureAll`, including lazy value-dependent error factories.
- Compose nested validation paths with `InputPointer.AppendProperty` and `AppendIndex`, preserving location and RFC 6901 escaping.
- Build aggregates, entities, value objects, specifications, domain events, and integration-event contracts.
- Define source-generated `Required*<TSelf>` scalar value objects.
- Validate cursor pagination with `PageRequest`, `CursorCodec`, `Page<T>`, and `PageBuilder`.
- Classify failures for retry-aware workers and consumers.

`Result<T>` is deliberately not directly JSON-serializable. At an HTTP boundary, map it with `Trellis.Asp.ToHttpResponse()`; elsewhere, unwrap it through `Match` or `TryGetValue` before serialization.

Generated `Required*<TSelf>` types are lenient by default: use `[NotDefault]` to reject sentinel values and `[Trim]` to normalize strings.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-core.html)
- [Cross-package cookbook](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html)
- [Error-handling guide](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)
