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
- Idempotent inserts on a unique constraint via `db.TryInsertUniqueAsync(entity, ct)` — converts a duplicate-key violation into a failed `Result<TEntity>` carrying an `Error.Conflict` with reason code `"duplicate.key"` and the provider-extracted `ConstraintName` / `ConstraintTableName` telemetry fields, and detaches the introduced graph so a retry does not flush stale dependents.
- Cursor-based seek pagination via `IQueryable<T>.ToPageAsync(pageSize, cursor, keySelector, …)` — returns `Result<Page<T>>`, composes with `PageBuilder` and `CursorCodec`, and never throws on malformed input.
- `TransactionalCommandBehavior` honors `Result.FailAfterCommit<T>(error)`: handlers that need to commit a permanent-failure transition (e.g., a worker marking an aggregate `permanently_failed` after a non-retryable external rejection) opt in per-result, and the staged row is committed alongside the failure outcome.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
