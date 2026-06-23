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

// Reflection-free alternative — generated at compile time, no assembly scan:
//   configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();

Maybe<Customer> customer = await dbContext.Customers.FirstOrDefaultMaybeAsync(cancellationToken);
Result<int> saved = await dbContext.SaveChangesResultAsync(cancellationToken);
```

## Key Features
- Apply Trellis value converters and owned-type conventions with one registration point.
- Owned-collection (`OwnsMany`) children that declare their own primary key are treated as **domain-assigned**: the key is marked `ValueGenerated.Never`, so an application-supplied `Guid`/`long`/`int` key persists on every provider (no SQL Server IDENTITY `544` error, no spurious 409 when adding a child to an already-loaded parent). Opt back into store generation with an explicit `ValueGeneratedOnAdd()` or `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]`.
- Query `Maybe<T>` naturally instead of dropping to storage-specific null handling.
- Return `Result<int>` or `Result` from save operations instead of throwing on expected failures.
- Idempotent inserts on a unique constraint via `db.TryInsertUniqueAsync(entity, ct)` — duplicate-key violations surface as a failed `Result<TEntity>` carrying an `Error.Conflict` with reason code `"duplicate.key"` and the provider-extracted `ConstraintName` / `ConstraintTableName` telemetry fields, so worker outboxes and "save unless exists" deduplication never throw on redelivery.
- Cursor-based seek pagination via `IQueryable<T>.ToPageAsync(pageSize, cursor, keySelector, …)` — returns `Result<Page<T>>`, composes with `PageBuilder` and `CursorCodec`, and never throws on malformed input.
- `RepositoryBase<TAggregate, TId>` — staging-only base class (Add/Remove/Query/Exists/Count).
- `IUnitOfWork` / `EfUnitOfWork<TContext>` — single commit boundary for staged changes.
- `TransactionalCommandBehavior` — Mediator pipeline behavior that auto-commits after successful command handlers (requires Mediator pipeline registration). Also commits on `Result.FailAfterCommit<T>(error)` outcomes so persist-on-failure handlers (e.g., a worker recording a permanent-failure transition) can stage state alongside the failed result.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
