# Trellis.Testing

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Testing.svg)](https://www.nuget.org/packages/Trellis.Testing)

FluentAssertions extensions and test doubles that make Result and Maybe tests read like intent instead of plumbing.

## Installation
```bash
dotnet add package Trellis.Testing
```

## Quick Example
```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var result = Result.Ok(42);
var maybe = Maybe.From("Ada");

result.Should().BeSuccess().Which.Should().Be(42);
maybe.Should().HaveValue().Which.Should().Be("Ada");
```

## Key Features
- **Result assertions** — `BeSuccess()`, `BeFailure()`, `BeFailureOfType<T>()`, `HaveErrorCode()`, async variants
- **Maybe assertions** — `HaveValue()`, `BeNone()`, `HaveValueEqualTo()`, `HaveValueMatching()`
- **Error assertions** — `HaveCode()`, `HaveDetail()`, `BeOfType<T>()`
- **ValidationError assertions** — `HaveFieldError()`, `HaveFieldErrorWithDetail()`, `HaveFieldCount()`
- **Unwrap helpers** — `Unwrap()` for extracting values in test code without analyzer warnings
- **FakeRepository** — In-memory aggregate store with domain event tracking and unique constraints
- **TestActorProvider** — Mutable `IActorProvider` with `AsyncLocal` scoping for authorization tests
- **AggregateTestMutator** — Reflection helpers for setting `Maybe<T>` backing fields in tests

## ASP.NET Core Integration Tests

For WebApplicationFactory helpers, DI service replacement, and MSAL token acquisition, use the companion package:

```bash
dotnet add package Trellis.Testing.AspNetCore
```

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-testing.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
