# Trellis.Primitives

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Primitives.svg)](https://www.nuget.org/packages/Trellis.Primitives)

Strongly typed value objects for .NET, with built-in primitives like `EmailAddress` and `Money` plus base classes for your own types.

## Installation
```bash
dotnet add package Trellis.Primitives
```

The source generator that powers the `Required*<TSelf>` and `ScalarValueObject<TSelf, TUnderlying>` base classes is bundled inside the `Trellis.Core` package (transitively referenced by `Trellis.Primitives`) — no extra package is required.

## Quick Example
```csharp
using Trellis;
using Trellis.Primitives;

// TryCreate returns Result<T>; pattern-match before using the value.
var emailResult = EmailAddress.TryCreate("ada@example.com");

// Money.Create throws on invalid input; use TryCreate for user input.
var subtotal = Money.Create(12.34m, "USD");
var shipping = Money.Create(2.00m, "USD");

// Arithmetic on Money returns Result<Money> (currency-mismatch / overflow safe).
Result<Money> grandTotal = subtotal.Add(shipping);

// Define a custom value object — the source generator emits TryCreate, equality, JSON converters, etc.
public sealed partial class CustomerEmail : RequiredString<CustomerEmail>;
public sealed partial class OrderId : RequiredGuid<OrderId>;
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
