# Trellis.Primitives

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Primitives.svg)](https://www.nuget.org/packages/Trellis.Primitives)

Strongly typed value objects for .NET, with built-in primitives like `EmailAddress` and `Money` plus base classes for your own types.

## Installation
```bash
dotnet add package Trellis.Primitives
```

For custom `Required*` types, also add `Trellis.Primitives.Generator`.

## Quick Example
```csharp
using Trellis.Primitives;

var email = EmailAddress.TryCreate("ada@example.com");
var total = Money.Create(12.34m, "USD");
var shipping = Money.Create(2.00m, "USD");
var grandTotal = total.Add(shipping);
```

## Key Features
- Ready-to-use value objects for common concepts such as email, URL, money, and percentages.
- Base classes like `RequiredString<CustomerEmail>` and `RequiredGuid<OrderId>` for custom domain types.
- Validation and parsing rules that stay with the type instead of leaking into handlers and controllers.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/primitives.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
