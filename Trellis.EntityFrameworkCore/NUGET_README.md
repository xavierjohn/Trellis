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

## Key Features

- Convention-based mapping for Trellis scalar and composite value objects.
- Source-generated discovery for `Maybe<T>` properties and `[OwnedEntity]` value objects.
- Query helpers for presence, equality, predicates, and ordering over `Maybe<T>`.
- Inspect `Maybe<T>` storage strategies, tables/schemas, all owned columns, and known convention reasons with `GetMaybePropertyMappings()` / `ToMaybeMappingDebugString()`.
- Result-returning save helpers and idempotent unique-key inserts.
- Shared `FaultCodes` constants for duplicate-key, foreign-key, retry-abort, and retry-exhaustion conflicts.
- Typed seek pagination through `SeekDefinition` and `ToPageAsync`.
- Selector-based `GeoCoordinateExpressions` for translated bounds, exact spherical radius, distance ordering, and seek pagination over numeric latitude/longitude columns.
- `EfUnitOfWork<TContext>` integration for mediator-owned commits.

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

`WithinRadius` combines the conservative box prefilter and exact haversine predicate.
Search values are parameterized; provider trigonometric rounding can differ from in-memory
math at an exact boundary, so include an application tolerance in the radius when needed.
Translation failures propagate. Bind the cursor codec to origin, radius, filters, sort
directions, and an algorithm version with `seek.WithCodec(...)` before exposing distance
pagination across requests. These expressions require no DI or interceptor registration.

Register `AddTrellisInterceptors()` when queries or updates rely on Trellis `Maybe<T>`, scalar-value, timestamp, or ETag behavior. Under `AddTrellisUnitOfWork<TContext>()`, repositories stage changes and the pipeline owns the commit.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore.html)
- [EF Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)
