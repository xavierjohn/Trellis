# Trellis.Results

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Results.svg)](https://www.nuget.org/packages/Trellis.Results)

Railway-oriented error handling for .NET with `Result<T>`, `Maybe<T>`, and typed errors.

## Installation
```bash
dotnet add package Trellis.Results
```

## Quick Example
```csharp
using Trellis;

Result<string> email = Result.Ok("ada@example.com")
    .Ensure(value => value.Contains('@'),
        Error.Validation("Email is invalid.", "email"))
    .Map(value => value.Trim().ToLowerInvariant());
```

## Key Features
- Compose success and failure paths with `Bind`, `Map`, `Tap`, and `Ensure`.
- Model optional data with `Maybe<T>` instead of `null`.
- Return typed errors that map cleanly to APIs, logs, and tests.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
