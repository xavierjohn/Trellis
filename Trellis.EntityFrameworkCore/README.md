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
- **Commit ownership:** `EfUnitOfWork.BeginScope()` returns `IUnitOfWorkScope`; `IsOwner` is true only for the outer scope. Nested commands defer persistence and event dispatch until the owning command succeeds; DTO/Unit outer responses retain nested aggregate events. Automatic event dispatch under a manually owned outer scope is rejected; use explicit post-commit dispatch instead.
- Apply Trellis value converters and owned-type conventions with one registration point.
- Owned-collection (`OwnsMany`) children that declare their own primary key are treated as **domain-assigned**: the key is marked `ValueGenerated.Never`, so an application-supplied `Guid`/`long`/`int` key persists on every provider (no SQL Server IDENTITY `544` error, no spurious 409 when adding a child to an already-loaded parent). Opt back into store generation with an explicit `ValueGeneratedOnAdd()` or `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]`.
- Query `Maybe<T>` naturally instead of dropping to storage-specific null handling.
- Return `Result<int>` or `Result` from save operations instead of throwing on expected failures.
- Idempotent inserts on a unique constraint via `db.TryInsertUniqueAsync(entity, ct)` — converts a duplicate-key violation into a failed `Result<TEntity>` carrying an `Error.Conflict` with reason code `"duplicate.key"` and the provider-extracted `ConstraintName` / `ConstraintTableName` telemetry fields, and detaches the introduced graph so a retry does not flush stale dependents.
- Typed cursor pagination via `IQueryable<T>.ToPageAsync(request, seek, …)` — `SeekDefinition` keeps single/composite, ascending/descending ordering and seek predicates together; malformed client state returns `Error.InvalidInput`.
- `TransactionalCommandBehavior` honors `Result.FailAfterCommit<T>(error)`: handlers that need to commit a permanent-failure transition (e.g., a worker marking an aggregate `permanently_failed` after a non-retryable external rejection) opt in per-result, and the staged row is committed alongside the failure outcome.

## Typed predicates over optional values

```csharp
var overdue = await dbContext.Orders
    .WhereHasValue(o => o.SubmittedAt, value => value < cutoff)
    .ToListAsync(cancellationToken);
```

**Breaking change:** `WhereLessThan`, `WhereLessThanOrEqual`, `WhereGreaterThan`,
and `WhereGreaterThanOrEqual` are replaced by `WhereHasValue(selector, predicate)`.
Move `<`, `<=`, `>`, or `>=` into the value lambda. The overload accepts an
`Expression<Func<TInner, bool>>`, including reusable expression variables, not a
compiled delegate. Absent values are always excluded, even for a constant-true predicate.
Presence-only `WhereHasValue(selector)`, equality, and ordering helpers are unchanged.

C# checks the predicate's operators; the configured provider must translate them.
String/GUID ordering follows provider semantics, and unsupported translations
(such as `DateTimeOffset` relational comparisons on SQLite) throw without a
client-side fallback. The helper targets storage directly; scalar value-object
`.Value` access still requires `AddTrellisInterceptors()`.
For shared domain specifications, continue using `Maybe<T>.HasValueWhere` with
an inline lambda and `AddTrellisInterceptors()` for EF execution.

## Typed seek pagination

```csharp
var seek = SeekDefinition.Descending<Order, DateTimeOffset>(o => o.CreatedAt)
    .ThenAscending(o => o.Id.Value);

Result<Page<Order>> page = await PageRequest.TryCreate(cursor, limit)
    .BindAsync(request => authorizedOrders.AsNoTracking()
        .ToPageAsync(request, seek, cancellationToken: ct));
```

Supply an authorized, pre-filtered `IQueryable<Order>`. `PageRequest` distinguishes
missing input from invalid empty cursors/non-positive limits; above-cap limits
clamp by default (`PageSizeLimitPolicy.Reject` opts into rejection). The helper
decodes typed state, seeks before `Take(Applied + 1)`, and assembles a forward page.
Boundary values are projected with the database query using the same key
expressions, not recomputed in C# after materialization. Provider-translated
functions are supported when the provider translates the complete query.

End with a stable unique key. Null keys are unsupported; verify your provider's
translation and comparison/collation semantics. `.Id.Value` projection requires
`AddTrellisInterceptors()`. There is no client-side fallback or snapshot guarantee,
and descending traversal is not previous-page support. Cancellation and provider
failures propagate rather than becoming malformed-cursor errors.

Use explicit key codecs or `seek.WithCodec(...)` for custom/context-bound/protected
state. Built-ins are versioned and unsigned; old unversioned tokens are rejected.
The single-key ascending `ToPageAsync(pageSize, cursor, keySelector, …)` convenience
remains. For non-translatable computed ordering, use an explicitly bounded
application algorithm with Core codecs and `PageBuilder` instead.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
