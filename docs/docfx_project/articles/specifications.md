# Specifications

The same business rule often shows up in three places:

- query filters
- validation logic
- reporting or batch jobs

Without a specification, that rule gets copied, renamed, and slowly drifts apart.

`Specification<T>` gives you one reusable home for that rule.

## Start with a practical example

```csharp
using System.Linq.Expressions;
using Trellis;

namespace SpecificationExamples;

public sealed class Order
{
    public decimal TotalAmount { get; init; }
    public DateTimeOffset DueAt { get; init; }
    public bool IsPaid { get; init; }
    public string Region { get; init; } = string.Empty;
}

public sealed class OverdueOrderSpecification(DateTimeOffset now) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => !order.IsPaid && order.DueAt < now;
}

public sealed class HighValueOrderSpecification(decimal threshold) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.TotalAmount >= threshold;
}

public sealed class RegionSpecification(string region) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.Region == region;
}
```

Now the business rule reads clearly:

```csharp
var spec = new OverdueOrderSpecification(DateTimeOffset.UtcNow)
    .And(new HighValueOrderSpecification(500m))
    .And(new RegionSpecification("West"));
```

## Why this is better than inline predicates

Inline LINQ predicates are fine for one-off queries. Specifications help when the rule has a name and a life of its own.

They give you:

- **reuse** across repository methods and services
- **composability** through `And`, `Or`, and `Not`
- **storage-agnostic expressions** for LINQ providers
- **testability** through `IsSatisfiedBy(...)`

## In-memory evaluation

Use `IsSatisfiedBy(...)` when you already have objects in memory:

```csharp
var spec = new HighValueOrderSpecification(500m);

bool isMatch = spec.IsSatisfiedBy(new Order
{
    TotalAmount = 750m,
    DueAt = DateTimeOffset.UtcNow.AddDays(2),
    IsPaid = false,
    Region = "West"
});
```

`Specification<T>` caches the compiled delegate by default, so repeated in-memory checks stay cheap.

## Composing rules

Composition is where specifications become really useful:

```csharp
var overdue = new OverdueOrderSpecification(DateTimeOffset.UtcNow);
var highValue = new HighValueOrderSpecification(500m);
var westRegion = new RegionSpecification("West");

var urgent = overdue.And(highValue);
var westOrUrgent = westRegion.Or(urgent);
var notOverdue = overdue.Not();
```

## Querying with `IQueryable`

`Specification<T>` has an implicit conversion to `Expression<Func<T, bool>>`, so it plugs directly into LINQ:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

List<Order> orders =
[
    new() { TotalAmount = 250m, DueAt = DateTimeOffset.UtcNow.AddDays(1), IsPaid = false, Region = "West" },
    new() { TotalAmount = 750m, DueAt = DateTimeOffset.UtcNow.AddDays(-2), IsPaid = false, Region = "West" }
];

IQueryable<Order> query = orders.AsQueryable();

var spec = new OverdueOrderSpecification(DateTimeOffset.UtcNow)
    .And(new HighValueOrderSpecification(500m));

var filtered = query.Where(spec);
```

That same pattern works with EF Core and other LINQ providers.

> [!NOTE]
> Composed specifications use `Expression.Invoke` internally. For EF Core translation, use **EF Core 8+**.

## Repository-friendly design

A repository can accept a specification without learning any business details:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Trellis;

namespace RepositoryExample;

public sealed class Order
{
    public decimal TotalAmount { get; init; }
}

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListAsync(Specification<Order> specification, CancellationToken ct);
    Task<bool> AnyAsync(Specification<Order> specification, CancellationToken ct);
}
```

That keeps the repository generic and the business rules named.

## When to disable cached compilation

Most specifications are created with immutable constructor values and should keep the default behavior.

If your expression depends on mutable state, override `CacheCompilation`:

```csharp
using System.Linq.Expressions;
using Trellis;

namespace CacheControlExample;

public sealed class ThresholdSpecification(Func<int> getThreshold) : Specification<int>
{
    protected override bool CacheCompilation => false;

    public override Expression<Func<int, bool>> ToExpression() =>
        value => value > getThreshold();
}
```

## Specifications and Trellis value objects

Specifications work well with Trellis primitives because the domain types can stay in the predicate instead of being peeled back to raw values everywhere.

For example, you can use specifications over aggregates or entities that expose:

- `EmailAddress`
- `RequiredEnum<TSelf>` members
- `MonetaryAmount`
- `Money`

That keeps your query language aligned with the domain language.

## `Maybe<T>` support in EF Core queries

If your specification references `Maybe<T>` members in EF Core, register the Trellis interceptors so the query can be rewritten correctly.

```csharp
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddTrellisInterceptors();
}
```

Then expressions like `HasValue` and `Value` can translate cleanly in supported scenarios.

## When specifications are a good fit

Use them when the rule:

- has a clear business name
- appears in more than one place
- should run both in memory and in queries
- is worth testing independently

Skip them when a predicate is truly one-off and unlikely to matter again.

## Summary

Specifications help you keep business rules:

- named
- reusable
- composable
- testable

That is the main win. They are less about pattern purity and more about preventing rule duplication.

## See also

- [Clean Architecture](clean-architecture.md)
- [Primitive Value Objects](primitives.md)
- [Trellis DDD API reference](../../api_reference/trellis-api-core.md)
