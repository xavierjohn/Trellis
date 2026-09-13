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

Use the reflection-based `ApplyTrellisConventions(typeof(AppDbContext).Assembly)` when compile-time discovery is not suitable.

## Key Features

- Convention-based mapping for Trellis scalar and composite value objects.
- Source-generated discovery for `Maybe<T>` properties and `[OwnedEntity]` value objects.
- Query helpers for presence, equality, predicates, and ordering over `Maybe<T>`.
- `SaveChangesResultAsync` and `SaveChangesResultUnitAsync` for typed persistence failures.
- `TryInsertUniqueAsync` for idempotent unique-key inserts.
- Typed seek pagination through `SeekDefinition` and `ToPageAsync`.
- `EfUnitOfWork<TContext>` and `AddTrellisUnitOfWork<TContext>()` for mediator-owned commits.

## Important Setup

- Register `AddTrellisInterceptors()` when queries or updates rely on Trellis `Maybe<T>`, scalar-value, timestamp, or ETag behavior.
- Under `AddTrellisUnitOfWork<TContext>()`, repositories stage changes; the innermost transactional pipeline behavior owns the commit.
- `[OwnedEntity]` generates the constructor EF needs but introduces an EF Core dependency into the declaring assembly. Use a private parameterless constructor when the domain assembly must remain EF-free.
- Prefer the typed `MaybeQueryableExtensions` over hand-written storage/sentinel expressions.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-efcore.md)
- [EF Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)

## Development

Run the package and generator tests from the repository root:

```powershell
dotnet test Trellis.EntityFrameworkCore\tests\Trellis.EntityFrameworkCore.Tests.csproj -c Release
dotnet test Trellis.EntityFrameworkCore\generator-tests\Trellis.EntityFrameworkCore.Generator.Tests.csproj -c Release
```
