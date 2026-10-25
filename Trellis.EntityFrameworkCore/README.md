# Trellis.EntityFrameworkCore

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore)

EF Core conventions, interceptors, Result-based persistence helpers, and translated spherical nearby-query expressions for Trellis aggregates, value objects, and `Maybe<T>`.

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
- Inspect `Maybe<T>` storage strategies, tables/schemas, all owned columns, and known convention reasons with `GetMaybePropertyMappings()` / `ToMaybeMappingDebugString()`.
- `SaveChangesResultAsync` and `SaveChangesResultUnitAsync` for typed persistence failures.
- `TryInsertUniqueAsync` for idempotent unique-key inserts.
- Shared `FaultCodes` constants for duplicate-key, foreign-key, retry-abort, and retry-exhaustion conflicts.
- Typed seek pagination through `SeekDefinition` and `ToPageAsync`.
- Selector-based `GeoCoordinateExpressions` for translated bounds, exact spherical radius, distance ordering, and seek pagination over numeric latitude/longitude columns.
- `EfUnitOfWork<TContext>` and `AddTrellisUnitOfWork<TContext>()` for mediator-owned commits.

## Nearby Queries

```csharp
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

var bounds = GeoBounds.Create(origin, radiusMeters);
var withinRadius = GeoCoordinateExpressions.WithinRadius<Store>(
    store => store.Latitude, store => store.Longitude, bounds);
var distance = GeoCoordinateExpressions.DistanceMetersTo<Store>(
    store => store.Latitude, store => store.Longitude, bounds.Center);
var seek = SeekDefinition.Ascending(distance).ThenAscending(store => store.Id);

Result<Page<Store>> page = await db.Stores
    .Where(withinRadius)
    .ToPageAsync(request, seek, cancellationToken: cancellationToken);
```

`WithinRadius` applies both the conservative one- or two-box prefilter and the exact
haversine predicate. Translation failures propagate; there is no client-side fallback.
Search values are parameterized so ordinary and antimeridian searches reuse their respective
query shapes. Provider trigonometric rounding can differ from in-memory math at an exact
boundary; include an application tolerance in the radius when that distinction matters.
Bind the cursor codec to origin, radius, all filters, sort directions, and an algorithm
version with `seek.WithCodec(...)` before exposing distance pagination across requests.
No DI or interceptor registration is required for these expressions.

## Important Setup

- Register `AddTrellisInterceptors()` when queries or updates rely on Trellis `Maybe<T>`, scalar-value, timestamp, or ETag behavior.
- Under `AddTrellisUnitOfWork<TContext>()`, repositories stage changes; the transactional pipeline behavior owns the commit after any selected inside-transaction behaviors.
- Use `AddInsideTransactionBehavior` for application-owned post-handler staging that must precede the commit decision. Return an ordinary failure to skip this invocation's commit; it does not roll back tracked changes or permanently reject outer/later commits.
- `[OwnedEntity]` generates the constructor EF needs but introduces an EF Core dependency into the declaring assembly. Use a private parameterless constructor when the domain assembly must remain EF-free.
- Prefer the typed `MaybeQueryableExtensions` over hand-written storage/sentinel expressions.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-efcore.md)
- [EF Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)

## Development

Run the package and generator tests from the repository root:

```powershell
dotnet test --project Trellis.EntityFrameworkCore\tests\Trellis.EntityFrameworkCore.Tests.csproj -c Release
dotnet test --project Trellis.EntityFrameworkCore\generator-tests\Trellis.EntityFrameworkCore.Generator.Tests.csproj -c Release
```

### SQL Server Testcontainers

With Docker running Linux containers, the specification tests can provision their own
SQL Server 2025 instance instead of requiring LocalDB or a prestarted database:

```powershell
$env:TRELLIS_TEST_SQLSERVER = 'testcontainers'
dotnet test --project Trellis.EntityFrameworkCore\tests\Trellis.EntityFrameworkCore.Tests.csproj -c Release `
    --filter-class Trellis.EntityFrameworkCore.Tests.SpecificationTests --filter-display-name '*SqlServer*' `
    --fail-skips on --minimum-expected-tests 15
Remove-Item Env:\TRELLIS_TEST_SQLSERVER
```

The first run downloads the image pinned by tag and digest. Container startup or SQL setup failures fail
the run rather than skip cases. The `Build` workflow's `build` job runs this selection on Linux
with skips treated as failures, and retains its results and container logs.

This mode currently applies only to `SpecificationTests`; other SQL integration tests
keep their existing setup. With the variable unset, the specification tests do not access
Docker. The existing `TRELLIS_TEST_SQLSERVER=true` LocalDB/external-connection mode remains
available; do not combine Testcontainers mode with `TRELLIS_TEST_SQLSERVER_CONNECTION`.
