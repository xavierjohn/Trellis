# Trellis.Asp

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.svg)](https://www.nuget.org/packages/Trellis.Asp)

ASP.NET Core integration for Trellis results, scalar value validation, and clean HTTP responses.

## Installation
```bash
dotnet add package Trellis.Asp
```

## Quick Example
```csharp
using Trellis;
using Trellis.Asp;

builder.Services.AddTrellisAsp();

app.MapGet("/widgets/{id}", (string id) =>
    Result.Success(id).ToHttpResult());
```

## Key Features
- Convert `Result<T>` and `Error` values into consistent HTTP responses.
- Validate Trellis scalar values during model binding and JSON deserialization.
- Support controller and minimal API styles, including AOT-friendly setups.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
