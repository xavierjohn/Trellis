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
