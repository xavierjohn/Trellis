# Trellis.Testing

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Testing.svg)](https://www.nuget.org/packages/Trellis.Testing)

FluentAssertions extensions that make Result and Maybe tests read like intent instead of plumbing.

## Installation
```bash
dotnet add package Trellis.Testing
```

## Quick Example
```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var result = Result.Success(42);
var maybe = Maybe.From("Ada");

result.Should().BeSuccess().Which.Should().Be(42);
maybe.Should().HaveValue().Which.Should().Be("Ada");
```

## Key Features
- Assert success, failure, error codes, and values with concise helpers.
- Test `Maybe<T>` without repetitive `HasValue` / `Value` ceremony.
- Keep Trellis-heavy test suites readable and intention-revealing.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-testing.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
