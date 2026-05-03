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
        new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is invalid." })))
    .Map(value => value.Trim().ToLowerInvariant());
```

## Key Features
- Compose success and failure paths with `Bind`, `Map`, `Tap`, and `Ensure`.
- Model optional data with `Maybe<T>` instead of `null`.
- Return typed errors that map cleanly to APIs, logs, and tests.
- Use `AsTask()` / `AsValueTask()` to return synchronous `Result` chains from async-shaped APIs.
- Build resource-aware HTTP errors tersely with `ResourceRef.For<TResource>(id)`.
- Define custom `Required*<TSelf>` value objects with source-generated parsing, JSON conversion, and tracing support.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
