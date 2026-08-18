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
using Trellis;
using Trellis.Asp;

// Declare [JsonConverter] yourself. System.Text.Json's generator only sees attributes
// written in your own source, so one emitted by Trellis arrives too late — STJ would
// treat OrderId as a POCO and emit a `new OrderId()` call that does not exist (CS1729).
[JsonConverter(typeof(ParsableJsonConverter<OrderId>))]
public partial class OrderId : RequiredGuid<OrderId>
{
}

[GenerateScalarValueConverters]
[JsonSerializable(typeof(CreateOrderRequest))]
public partial class AppJsonContext : JsonSerializerContext
{
}
```

The context needs at least one `[JsonSerializable]` of its own; otherwise System.Text.Json
skips it entirely and the build fails with CS0534. Trellis reports **TRLS059** when it sees
this, because the raw compiler error does not explain the cause.

## Key Features
- Generates reflection-free `JsonConverter<T>` implementations for Trellis scalar value objects.
- Removes reflection-heavy converter discovery from Native AOT deployments.
- Fits directly into existing `System.Text.Json` source-generation workflows.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
