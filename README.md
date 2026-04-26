# Trellis

[![Build](https://github.com/xavierjohn/Trellis/actions/workflows/build.yml/badge.svg)](https://github.com/xavierjohn/Trellis/actions/workflows/build.yml)
[![codecov](https://codecov.io/gh/xavierjohn/Trellis/branch/main/graph/badge.svg)](https://codecov.io/gh/xavierjohn/Trellis)
[![NuGet](https://img.shields.io/nuget/v/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![C#](https://img.shields.io/badge/C%23-14.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![GitHub Stars](https://img.shields.io/github/stars/xavierjohn/Trellis?style=social)](https://github.com/xavierjohn/Trellis/stargazers)
[![Documentation](https://img.shields.io/badge/docs-online-blue.svg)](https://xavierjohn.github.io/Trellis/)

<p align="center">
  <img src="docs/images/hero-banner.png" alt="Trellis — Typed errors, validated objects, composable pipelines for .NET" />
</p>

> Typed errors, validated value objects, and composable application pipelines for .NET.

## Before / After

**Without Trellis**

```csharp
if (string.IsNullOrWhiteSpace(request.Email))
    return Results.BadRequest(new { code = "validation.error", detail = "Email is required." });

if (!request.Email.Contains('@'))
    return Results.BadRequest(new { code = "validation.error", detail = "Email is invalid." });

return Results.Ok(new User(request.Email.Trim().ToLowerInvariant()));
```

**With Trellis**

```csharp
using Trellis.Asp;
using Trellis.Primitives;

return EmailAddress.TryCreate(request.Email)
    .Map(email => new User(email))
    .ToHttpResponse();
```

## What You Get

- `Result<T>` and `Maybe<T>` pipelines that make failures explicit.
- Strongly typed value objects that remove primitive obsession.
- DDD building blocks: `Aggregate`, `Entity`, `ValueObject`, `Specification`, and domain events.
- ASP.NET Core, EF Core, Mediator, HttpClient, FluentValidation, and Stateless integrations.
- Roslyn analyzers and test helpers that keep teams on the happy path.
- AOT-friendly, allocation-conscious APIs built for modern .NET.

## Quick Start

```bash
dotnet add package Trellis.Core
```

```csharp
using Trellis;

var result = Result.Ok("ada@example.com")
    .Ensure(email => email.Contains('@'),
        new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is invalid." })))
    .Map(email => email.Trim().ToLowerInvariant());
```

## Packages

### Core

| Package | What it gives you |
| --- | --- |
| [Trellis.Core](https://www.nuget.org/packages/Trellis.Core) | `Result<T>`, `Maybe<T>`, typed errors, and pipeline operators |
| [Trellis.Primitives](https://www.nuget.org/packages/Trellis.Primitives) | Ready-to-use value objects plus base classes for your own |
| [Trellis.Analyzers](https://www.nuget.org/packages/Trellis.Analyzers) | Compile-time guidance for Result, Maybe, and EF Core usage |

### Integration

| Package | What it gives you |
| --- | --- |
| [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) | Result-to-HTTP mapping, scalar validation, JSON/model binding (bundles the AOT-friendly JSON converter generator), and ASP.NET actor providers (Claims, Entra, Development) |
| [Trellis.Authorization](https://www.nuget.org/packages/Trellis.Authorization) | `Actor`, permission checks, and resource authorization primitives |
| [Trellis.Http](https://www.nuget.org/packages/Trellis.Http) | `HttpClient` extensions that stay inside the Result pipeline |
| [Trellis.Mediator](https://www.nuget.org/packages/Trellis.Mediator) | Result-aware pipeline behaviors for [Mediator](https://github.com/martinothamar/Mediator) |
| [Trellis.FluentValidation](https://www.nuget.org/packages/Trellis.FluentValidation) | FluentValidation output converted into Trellis results |
| [Trellis.EntityFrameworkCore](https://www.nuget.org/packages/Trellis.EntityFrameworkCore) | EF Core conventions, converters, Maybe queries, and safe save helpers (bundles the `Maybe<T>` / owned value-object source generator) |
| [Trellis.ServiceDefaults](https://www.nuget.org/packages/Trellis.ServiceDefaults) | Opinionated composition builder for wiring Trellis web-service modules in the canonical order |
| [Trellis.StateMachine](https://www.nuget.org/packages/Trellis.StateMachine) | Stateless transitions that return `Result<TState>` |
| [Trellis.Testing](https://www.nuget.org/packages/Trellis.Testing) | FluentAssertions extensions for `Result<T>` and `Maybe<T>` |

## Performance

Typical overhead is measured in single-digit to low double-digit nanoseconds—tiny next to a database call or HTTP request. [Benchmarks](BENCHMARKS.md)

## Documentation

- [Full documentation](https://xavierjohn.github.io/Trellis/)
- [Getting started](https://xavierjohn.github.io/Trellis/articles/intro.html)
- [With vs without Trellis](https://xavierjohn.github.io/Trellis/articles/with-vs-without-trellis.html)
- [API reference](https://xavierjohn.github.io/Trellis/api/index.html)
- [Training lab](https://github.com/xavierjohn/trellis-training)

## Contributing

Contributions are welcome. For major changes, please open an issue first and run `dotnet test` before sending a PR.

## License

[MIT](LICENSE)
