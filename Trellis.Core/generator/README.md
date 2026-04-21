# Trellis.Core.Generator

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Core.Generator.svg)](https://www.nuget.org/packages/Trellis.Core.Generator)

Source generation for custom scalar value objects built on `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, and related Trellis primitives.

## Installation
```bash
dotnet add package Trellis.Core.Generator
```

## Quick Example
```csharp
using Trellis;

[StringLength(100)]
public partial class CustomerName : RequiredString<CustomerName> { }

var name = CustomerName.Create("Ada");
```

## Key Features
- Generates `Create`, `TryCreate`, parsing, and conversion boilerplate for custom primitives.
- Reads Trellis validation attributes such as `[StringLength]` and `[Range]` at compile time.
- Keeps custom value objects terse without giving up strong typing.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/primitives.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
