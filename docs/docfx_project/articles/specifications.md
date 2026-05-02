---
title: Specifications
package: Trellis.Core
topics: [specifications, ddd, query, predicate, composition, expression-tree, ef-core]
related_api_reference: [trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Specifications

`Specification<T>` encapsulates a named business rule as a composable, storage-agnostic `Expression<Func<T, bool>>` that runs identically in memory (`IsSatisfiedBy`) and against `IQueryable<T>` providers such as EF Core.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Name a single business rule that can be reused across services and repositories | Subclass `Specification<T>` and override `ToExpression()` | [Defining a specification](#defining-a-specification) |
| Combine two rules with logical AND / OR | `spec.And(other)` / `spec.Or(other)` | [Composing rules](#composing-rules) |
| Invert a rule | `spec.Not()` | [Composing rules](#composing-rules) |
| Evaluate against an in-memory object | `spec.IsSatisfiedBy(entity)` | [In-memory evaluation](#in-memory-evaluation) |
| Filter an `IQueryable<T>` (LINQ-to-Objects, EF Core, ...) | Pass the spec where an `Expression<Func<T, bool>>` is expected (implicit conversion) | [Applying to IQueryable](#applying-to-iqueryable) |
| Reference `Maybe<T>` members from a spec used by EF Core | Register `AddTrellisInterceptors()` on the `DbContextOptionsBuilder` | [EF Core integration](#ef-core-integration) |
| Force recompilation on each `IsSatisfiedBy` call | Override `CacheCompilation` to return `false` | [Caching the compiled delegate](#caching-the-compiled-delegate) |

## Use this guide when

- The same predicate appears in more than one repository method, validator, or batch job.
- You need a single source of truth for a rule that runs both in memory and as a translated SQL `WHERE`.
- A repository should accept a domain-named filter (`OverdueOrderSpec`) instead of raw `Expression<Func<T, bool>>` arguments.

## Surface at a glance

`Trellis.Core` exposes one abstract class, `Specification<T>` (namespace `Trellis`).

| Member | Kind | Purpose |
|---|---|---|
| `ToExpression()` | `abstract Expression<Func<T, bool>>` | The canonical expression tree for the rule. The only member subclasses must implement. |
| `IsSatisfiedBy(T entity)` | `bool` | In-memory evaluation. Uses a lazily compiled, cached delegate by default. |
| `And(Specification<T> other)` | `Specification<T>` | Logical AND combinator. Throws `ArgumentNullException` if `other` is `null`. |
| `Or(Specification<T> other)` | `Specification<T>` | Logical OR combinator. Throws `ArgumentNullException` if `other` is `null`. |
| `Not()` | `Specification<T>` | Logical negation. |
| `implicit operator Expression<Func<T, bool>>` | conversion | Lets the spec be passed directly to `Where`, `Any`, `Count`, ... |
| `CacheCompilation` | `protected virtual bool` (default `true`) | Override to `false` when the expression depends on mutable closure state. |

`AndSpecification<T>`, `OrSpecification<T>`, and `NotSpecification<T>` are internal implementation types — they are not part of the public surface.

Full signatures: [trellis-api-core.md](../api_reference/trellis-api-core.md).

## Installation

```bash
dotnet add package Trellis.Core
```

## Quick start

Define one rule, compose with another, and run it both in memory and against a queryable.

```csharp
using System;
using System.Linq;
using System.Linq.Expressions;
using Trellis;

public sealed class Order
{
    public decimal TotalAmount { get; init; }
    public DateTimeOffset DueAt { get; init; }
    public bool IsPaid { get; init; }
    public string Region { get; init; } = string.Empty;
}

public sealed class OverdueOrderSpec(DateTimeOffset now) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => !order.IsPaid && order.DueAt < now;
}

public sealed class HighValueOrderSpec(decimal threshold) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.TotalAmount >= threshold;
}

var spec = new OverdueOrderSpec(DateTimeOffset.UtcNow)
    .And(new HighValueOrderSpec(500m));

bool match = spec.IsSatisfiedBy(new Order { TotalAmount = 750m, DueAt = DateTimeOffset.UtcNow.AddDays(-1) });

IQueryable<Order> filtered = new[] { /* ... */ }.AsQueryable().Where(spec);
```

## Defining a specification

Subclass `Specification<T>` and implement `ToExpression()`. Constructor parameters become closure values inside the expression — keep them immutable.

```csharp
using System;
using System.Linq.Expressions;
using Trellis;

public sealed class RegionSpec(string region) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.Region == region;
}
```

Rules of thumb:

- One specification = one named rule. Don't pack unrelated conditions into a single class.
- Keep the expression body translation-friendly (member access, arithmetic, comparisons). Method calls only translate if the LINQ provider supports them.
- Capture only immutable state in the constructor. See [Caching the compiled delegate](#caching-the-compiled-delegate) if you must capture mutable state.

## Composing rules

| Combinator | Result |
|---|---|
| `a.And(b)` | Satisfied when both `a` and `b` are. |
| `a.Or(b)` | Satisfied when either `a` or `b` is. |
| `a.Not()` | Satisfied when `a` is not. |

```csharp
var overdue   = new OverdueOrderSpec(DateTimeOffset.UtcNow);
var highValue = new HighValueOrderSpec(500m);
var west      = new RegionSpec("West");

var urgent        = overdue.And(highValue);
var westOrUrgent  = west.Or(urgent);
var notOverdue    = overdue.Not();
```

`And`/`Or`/`Not` return a new `Specification<T>` — original instances are never mutated, so combinators are safe to share.

## In-memory evaluation

`IsSatisfiedBy(entity)` evaluates the rule against an object you already hold in memory.

```csharp
var spec = new HighValueOrderSpec(500m);

bool match = spec.IsSatisfiedBy(new Order
{
    TotalAmount = 750m,
    DueAt = DateTimeOffset.UtcNow.AddDays(2),
    IsPaid = false,
    Region = "West"
});
```

By default the delegate compiled from `ToExpression()` is cached behind a `Lazy<Func<T, bool>>`, so repeated calls do not recompile.

## Applying to IQueryable

Because `Specification<T>` defines an implicit conversion to `Expression<Func<T, bool>>`, you can pass it directly to any LINQ operator that takes a predicate expression.

```csharp
using System.Linq;

IQueryable<Order> query = /* AsQueryable() / DbSet<Order> / ... */;

var spec = new OverdueOrderSpec(DateTimeOffset.UtcNow)
    .And(new HighValueOrderSpec(500m));

var filtered = query.Where(spec);
int count    = query.Count(spec);
bool any     = query.Any(spec);
```

A repository can therefore accept the named rule without learning the rule itself:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Trellis;

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListAsync(Specification<Order> spec, CancellationToken ct);
    Task<bool>                 AnyAsync(Specification<Order> spec, CancellationToken ct);
}
```

## EF Core integration

Composed specifications combine sub-expressions using `Expression.Invoke`. EF Core 8+ translates these reliably; older versions may not.

If your specification reads `Maybe<T>` members (`HasValue`, `Value`, `GetValueOrDefault(d)`, `== Maybe<T>.None`), register the Trellis interceptors so the `MaybeQueryInterceptor` rewrites the access into the underlying storage member:

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddTrellisInterceptors();
}
```

Without `AddTrellisInterceptors()`, `Maybe<T>` access inside a spec either fails to translate or silently drops the predicate — a "fake says yes, production says no" failure mode. See the EF Core guide for details.

## Caching the compiled delegate

`CacheCompilation` (protected, virtual, default `true`) controls whether `IsSatisfiedBy` reuses a single `Lazy<Func<T, bool>>`. Override it to `false` only when `ToExpression()` returns a different tree on different invocations of the same instance — for example, when the predicate captures a mutable callback.

```csharp
using System;
using System.Linq.Expressions;
using Trellis;

public sealed class ThresholdSpec(Func<int> getThreshold) : Specification<int>
{
    protected override bool CacheCompilation => false;

    public override Expression<Func<int, bool>> ToExpression() =>
        value => value > getThreshold();
}
```

The override only affects in-memory evaluation. LINQ providers always read the current `ToExpression()` result.

## Composition

Specifications compose with the rest of Trellis through the data they filter, not through `Result<T>`. A typical flow loads via spec, validates, and continues in a result pipeline:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Trellis;

public sealed class FulfilOverdueOrders(IOrderRepository repository)
{
    public Task<Result<int>> RunAsync(CancellationToken ct) =>
        FetchAsync(ct)
            .EnsureAsync(orders => orders.Count > 0, new Error.NotFound(ResourceRef.For<Order>("overdue")))
            .MapAsync(orders => orders.Count);

    private async Task<Result<IReadOnlyList<Order>>> FetchAsync(CancellationToken ct)
    {
        var spec = new OverdueOrderSpec(DateTimeOffset.UtcNow)
            .And(new HighValueOrderSpec(500m));

        var orders = await repository.ListAsync(spec, ct).ConfigureAwait(false);
        return Result.Ok(orders);
    }
}
```

## Practical guidance

- **One rule, one name.** A spec earns its keep when the rule has a domain name and shows up in more than one place. Inline `Where(o => ...)` is fine for one-off queries.
- **Prefer combinators over re-implementing.** If `OverdueOrderSpec.And(HighValueOrderSpec)` already says it, don't write `OverdueHighValueOrderSpec`.
- **Keep expressions translatable.** Stick to property access, comparisons, arithmetic, and provider-supported helpers. Avoid invoking arbitrary instance methods inside `ToExpression()`.
- **Treat constructor args as immutable.** They get baked into the expression tree closure; mutating a captured field after composition produces surprising results.
- **EF Core 8+.** Composed specs use `Expression.Invoke`; older EF Core versions cannot translate that pattern.
- **Test specs directly.** `IsSatisfiedBy` against representative objects is the cheapest way to lock the rule down before exercising it through a repository.

## Cross-references

- API surface: [`trellis-api-core.md` → `Specification<T>`](../api_reference/trellis-api-core.md#specificationt)
- `Maybe<T>` query rewriting and the `AddTrellisInterceptors()` registration: [`integration-ef.md`](integration-ef.md)
- Spec / `Maybe<T>` / fake-vs-real divergence walkthrough: [`trellis-api-cookbook.md` → Recipe 15](../api_reference/trellis-api-cookbook.md)
- Domain primitives that read well inside specifications: [`primitives.md`](primitives.md)
