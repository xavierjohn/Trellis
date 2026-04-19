# Trellis.EntityFrameworkCore

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore)

EF Core conventions and helpers for Trellis value objects, `Maybe<T>`, and Result-based persistence.

## Installation
```bash
dotnet add package Trellis.EntityFrameworkCore
```

## Quick Example
```csharp
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
    configurationBuilder.ApplyTrellisConventions(typeof(AppDbContext).Assembly);

Maybe<Customer> customer = await dbContext.Customers.FirstOrDefaultMaybeAsync(cancellationToken);
Result<int> saved = await dbContext.SaveChangesResultAsync(cancellationToken);
```

## Key Features
- Apply Trellis value converters and owned-type conventions with one registration point.
- Query `Maybe<T>` naturally instead of dropping to storage-specific null handling.
- Return `Result<int>` or `Result` from save operations instead of throwing on expected failures.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
