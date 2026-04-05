# Trellis.Analyzers

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Analyzers.svg)](https://www.nuget.org/packages/Trellis.Analyzers)

Roslyn analyzers that keep Trellis usage safe, idiomatic, and review-friendly.

## Installation
```bash
dotnet add package Trellis.Analyzers
```

## Quick Example
```csharp
using Trellis;

Result<int> Parse(string text) => Result.Success(text.Length);

// TRLS002 recommends Bind instead of Map when the lambda returns Result<T>.
var result = Parse("abc")
    .Bind(length => Result.Success(length + 1));
```

## Key Features
- Flags unsafe `Result.Value`, `Result.Error`, and `Maybe.Value` access.
- Catches async misuse, double-wrapped results, and common ROP anti-patterns.
- Includes fixes for Trellis-specific issues such as `SaveChangesAsync` vs. `SaveChangesResultAsync`.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/analyzers/index.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
