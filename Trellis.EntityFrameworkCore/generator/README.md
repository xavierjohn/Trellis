# Trellis.EntityFrameworkCore.Generator

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Generator.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Generator)

Source generation for EF Core-friendly `Maybe<T>` properties and owned Trellis value objects.

## Installation
```bash
dotnet add package Trellis.EntityFrameworkCore.Generator
```

## Quick Example
```csharp
using System;
using System.Collections.Generic;
using Trellis;
using Trellis.EntityFrameworkCore;

[OwnedEntity]
public partial class Address : ValueObject
{
    protected override IEnumerable<IComparable?> GetEqualityComponents() => [];
}

public partial class Customer
{
    public partial Maybe<DateTimeOffset> SubmittedAt { get; set; }
}
```

## Key Features
- Generates backing members for partial `Maybe<T>` properties used by EF Core.
- Adds owned-entity helpers for Trellis value objects marked with `[OwnedEntity]`.
- Reduces repetitive persistence plumbing while keeping your domain types clean.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
