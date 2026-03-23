# Specifications

**Level:** Intermediate | **Package:** `Trellis.DomainDrivenDesign`

Specifications express business rules as composable, storage-agnostic expression trees. The same specification works for querying EF Core, Cosmos DB, or in-memory collections.

## Why Specifications?

Business rules like "find all overdue orders over $500 in the West region" appear in multiple places — queries, validation, reporting. Without specifications, this logic gets duplicated across repositories, services, and controllers.

Specifications solve this by encapsulating business rules as **reusable expression trees** that any LINQ provider can evaluate.

## Defining a Specification

Inherit from `Specification<T>` and override `ToExpression()`:

```csharp
using System.Linq.Expressions;
using Trellis;

public class OverdueOrderSpec(DateTimeOffset now) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression()
        => order => order.DueDate < now && order.Status != OrderStatus.Completed;
}

public class HighValueOrderSpec(decimal threshold) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression()
        => order => order.TotalAmount > threshold;
}

public class CustomerInRegionSpec(string region) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression()
        => order => order.Customer.Region == region;
}
```

## In-Memory Evaluation

Use `IsSatisfiedBy` to evaluate a specification against an entity:

```csharp
var spec = new HighValueOrderSpec(500m);

if (spec.IsSatisfiedBy(order))
    Console.WriteLine("High-value order detected");

// Filter a collection
var highValueOrders = orders.Where(spec.IsSatisfiedBy).ToList();
```

## Composability

Specifications combine using `And`, `Or`, and `Not`:

```csharp
// Compose multiple specifications
var spec = new OverdueOrderSpec(DateTimeOffset.UtcNow)
    .And(new HighValueOrderSpec(500m))
    .And(new CustomerInRegionSpec("West"));

// Negate a specification
var notOverdue = new OverdueOrderSpec(DateTimeOffset.UtcNow).Not();

// OR composition
var urgentOrExpensive = new OverdueOrderSpec(DateTimeOffset.UtcNow)
    .Or(new HighValueOrderSpec(1000m));
```

Specifications are **immutable** — composition always returns a new specification, leaving the originals unchanged.

## IQueryable Integration

Specifications convert **implicitly** to `Expression<Func<T, bool>>`, so they work directly with `IQueryable.Where()`:

```csharp
var spec = new OverdueOrderSpec(DateTimeOffset.UtcNow)
    .And(new HighValueOrderSpec(500m));

// Implicit conversion — expression tree translates to SQL via EF Core
var results = await _dbContext.Orders
    .Where(spec)
    .OrderByDescending(o => o.TotalAmount)
    .ToListAsync();
```

> **Note:** Composite specifications use `Expression.Invoke` internally, which requires **EF Core 8+** for server-side translation.

## Repository Pattern

Define repository interfaces that accept specifications:

```csharp
public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListAsync(Specification<Order> spec, CancellationToken ct);
    Task<int> CountAsync(Specification<Order> spec, CancellationToken ct);
    Task<bool> AnyAsync(Specification<Order> spec, CancellationToken ct);
}

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;

    public OrderRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Order>> ListAsync(
        Specification<Order> spec, CancellationToken ct)
        => await _db.Orders.Where(spec).ToListAsync(ct);

    public async Task<int> CountAsync(
        Specification<Order> spec, CancellationToken ct)
        => await _db.Orders.Where(spec).CountAsync(ct);

    public async Task<bool> AnyAsync(
        Specification<Order> spec, CancellationToken ct)
        => await _db.Orders.Where(spec).AnyAsync(ct);
}
```

## API Reference

| Member | Description |
|--------|-------------|
| `ToExpression()` | Returns `Expression<Func<T, bool>>` for LINQ providers |
| `IsSatisfiedBy(T)` | Evaluates the specification in-memory (cached compilation) |
| `CacheCompilation` | `true` by default; override to `false` for mutable-state specs |
| `And(Specification<T>)` | Logical AND composition |
| `Or(Specification<T>)` | Logical OR composition |
| `Not()` | Logical negation |
| Implicit operator | Converts to `Expression<Func<T, bool>>` for `IQueryable.Where()` |

## Expression Caching

`IsSatisfiedBy` automatically caches the compiled expression using `Lazy<Func<T, bool>>`, so repeated in-memory evaluations compile the expression tree only once.

If your specification captures **mutable state** — for example, a threshold that changes at runtime — the cached delegate will not reflect updated values. In that case, override `CacheCompilation` to opt out:

```csharp
// Mutable threshold spec — opt out of caching
public class ThresholdSpec(Func<int> getThreshold) : Specification<int>
{
    protected override bool CacheCompilation => false;
    public override Expression<Func<int, bool>> ToExpression()
        => x => x > getThreshold();
}
```

> **Tip:** Most specifications are constructed with immutable parameters (e.g., `new HighValueOrderSpec(500m)`). For these, caching is always safe and enabled by default.

## Design Principles

- **Domain concept** — Specifications express business rules, not persistence concerns
- **Storage-agnostic** — Works with EF Core, Cosmos DB, or any LINQ provider
- **Composable** — `And`, `Or`, `Not` for building complex queries from simple parts
- **Testable** — Specifications can be tested against in-memory collections
- **Owned by the domain layer** — No dependency on any persistence library

## Relationship to Trellis

Specifications integrate with the rest of Trellis:

- Value objects in specification predicates enforce type safety
- Result types for specification evaluation failures
- Aggregate boundaries respected in specification queries

## Maybe<T> Support in Specifications

Specifications that reference `Maybe<T>` properties work with EF Core when using the Trellis query interceptor. Register the interceptor via `AddTrellisInterceptors()`, then use natural LINQ in your specification expression:

```csharp
public class OverdueOrderSpecification(DateTime cutoff) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.Status == OrderStatus.Submitted
              && order.SubmittedAt.HasValue
              && order.SubmittedAt.Value < cutoff;
}
```

The interceptor rewrites `Maybe<T>.HasValue` to `IS NOT NULL` and `Maybe<T>.Value` to the backing storage field, enabling EF Core to translate the expression to SQL. Without the interceptor, use the explicit `WhereLessThan` extension method in the repository instead.

## Next Steps

- [Clean Architecture](clean-architecture.md) — Architecture patterns that use specifications
- [Entity Framework Core](integration-ef.md) — Repository patterns for persistence
- [Trellis for AI Code Generation](ai-code-generation.md) — How specs map to code
- [DDD Samples](https://github.com/xavierjohn/Trellis/blob/main/Trellis.DomainDrivenDesign/SAMPLES.md#specification-pattern) — Comprehensive code examples
