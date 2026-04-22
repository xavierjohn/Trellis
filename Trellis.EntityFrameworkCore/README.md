# Trellis.EntityFrameworkCore

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore)

EF Core conventions and helpers for Trellis value objects, `Maybe<T>`, and Result-based persistence.

> **AOT / Trim compatibility:** This package opts **out** of NativeAOT and trimming.
> EF Core relies on runtime reflection (model building, change tracking, query translation,
> proxies). Microsoft documents NativeAOT support for EF Core as
> [_"highly experimental, not suited for production use"_](https://learn.microsoft.com/ef/core/performance/nativeaot-and-precompiled-queries).
> If your application targets `PublishAot=true`, do not reference this package. The rest
> of the Trellis framework (`Trellis.Core`, `Trellis.Asp`, `Trellis.FluentValidation`,
> `Trellis.Mediator`, `Trellis.Primitives`, `Trellis.StateMachine`, `Trellis.Authorization`)
> is fully AOT-compatible and is exercised end-to-end under AOT in `Examples/Showcase/src/Showcase.MinimalApi`.

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

// AOT-friendly alternative — generated at compile time, no reflection scan:
//   configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();

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
