# Trellis.AspSourceGenerator

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.AspSourceGenerator.svg)](https://www.nuget.org/packages/Trellis.AspSourceGenerator)

A source generator that makes Trellis ASP.NET serialization AOT-friendly by generating scalar value converter registrations for your `JsonSerializerContext`.

## Installation
```bash
dotnet add package Trellis.AspSourceGenerator
```

## Quick Example
```csharp
using System.Text.Json.Serialization;
using Trellis.Asp;

[GenerateScalarValueConverters]
[JsonSerializable(typeof(CreateOrderRequest))]
public partial class AppJsonContext : JsonSerializerContext
{
}
```

## Key Features
- Generates `JsonSerializable` entries for Trellis scalar value objects.
- Removes reflection-heavy converter discovery from Native AOT deployments.
- Fits directly into existing `System.Text.Json` source-generation workflows.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
