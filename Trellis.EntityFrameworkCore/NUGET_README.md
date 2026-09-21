# Trellis.EntityFrameworkCore

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore)

EF Core conventions, interceptors, and Result-based persistence helpers for Trellis aggregates, value objects, and `Maybe<T>`.

> This package opts out of NativeAOT and trimming because EF Core model building, change tracking, and query translation rely on runtime reflection.

## Installation

```bash
dotnet add package Trellis.EntityFrameworkCore
```

## Quick Example

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

protected override void ConfigureConventions(
    ModelConfigurationBuilder configurationBuilder) =>
    configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();

Maybe<Customer> customer =
    await dbContext.Customers.FirstOrDefaultMaybeAsync(cancellationToken);

Result<int> saved =
    await dbContext.SaveChangesResultAsync(cancellationToken);
```

## Key Features

- Convention-based mapping for Trellis scalar and composite value objects.
- Source-generated discovery for `Maybe<T>` properties and `[OwnedEntity]` value objects.
- Query helpers for presence, equality, predicates, and ordering over `Maybe<T>`.
- Inspect `Maybe<T>` storage strategies, tables/schemas, all owned columns, and known convention reasons with `GetMaybePropertyMappings()` / `ToMaybeMappingDebugString()`.
- Result-returning save helpers and idempotent unique-key inserts.
- Typed seek pagination through `SeekDefinition` and `ToPageAsync`.
- `EfUnitOfWork<TContext>` integration for mediator-owned commits.

Register `AddTrellisInterceptors()` when queries or updates rely on Trellis `Maybe<T>`, scalar-value, timestamp, or ETag behavior. Under `AddTrellisUnitOfWork<TContext>()`, repositories stage changes and the pipeline owns the commit.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore.html)
- [EF Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)
