---
title: Aggregate Factory Pattern
package: Trellis.Core
topics: [ddd, aggregate, factory, invariants, result, value-objects, reconstitution]
related_api_reference: [trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Aggregate Factory Pattern

A convention for giving `Aggregate<TId>` two safe creation paths — `TryCreate` for new instances and `TryCreateExisting` for reconstitution — so identity, invariants, and creation events live in one place.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Create a brand-new aggregate (generate ID, raise creation event) | `static Result<TAgg> TryCreate(...)` | [Defining the factory](#defining-the-factory) |
| Reconstitute an aggregate with a known ID (importer, fixture, manual hydration) | `static Result<TAgg> TryCreateExisting(TId id, ...)` | [Reconstitution path](#reconstitution-path) |
| Throw on invalid input (seeders, known-good test data) | `static TAgg Create(...)` / `CreateExisting(id, ...)` | [Throwing helpers](#throwing-helpers) |
| Run the same invariants on both paths | private `static Result<Unit> Validate(...)` | [Centralizing validation](#centralizing-validation) |
| Type the constructor parameters | derive each input from `Required*<TSelf>` | [Composing primitives](#composing-primitives) |
| Raise creation events exactly once | `DomainEvents.Add(...)` inside `TryCreate` only | [Domain events and reconstitution](#domain-events-and-reconstitution) |

## Use this guide when

- Your aggregate must support both new construction *and* reconstitution from existing data (importers, manual rehydration, fixtures, migrations).
- You want one place that enforces invariants, regardless of which path the caller took.
- Tests need deterministic IDs while production generates them.
- Creation events must fire exactly once per aggregate lifetime.

## Surface at a glance

This is a **convention**, not API. `Aggregate<TId>` itself only requires `protected Aggregate(TId id)` and exposes `DomainEvents`, `UncommittedEvents()`, `AcceptChanges()`, and `ETag` — see [`Aggregate<TId>` reference](../api_reference/trellis-api-core.md#aggregatetid). The pattern adds the four static methods below.

| Method | Signature shape | ID source | Validation | Raises creation event? |
|---|---|---|---|---|
| `TryCreate` | `static Result<T> TryCreate(...primitives)` | Generated (e.g. `TId.NewUniqueV7()`) | Yes | Yes |
| `TryCreateExisting` | `static Result<T> TryCreateExisting(TId id, ...primitives)` | Caller-supplied | Yes | No |
| `Create` | `static T Create(...primitives)` | Generated | Yes (throws on failure) | Yes |
| `CreateExisting` | `static T CreateExisting(TId id, ...primitives)` | Caller-supplied | Yes (throws on failure) | No |

Underlying types: [`Aggregate<TId>`, `IAggregate`, `IDomainEvent`](../api_reference/trellis-api-core.md#domain-driven-design); [`Required*<TSelf>` primitive bases](../api_reference/trellis-api-core.md#primitive-value-object-base-classes).

## Installation

```bash
dotnet add package Trellis.Core
```

## Quick start

A minimal `Product` aggregate exposing all four factory paths.

```csharp
using System;
using Trellis;

namespace Catalog;

[StringLength(200)]
public partial class ProductName : RequiredString<ProductName> { }

[StringLength(64)]
public partial class Sku : RequiredString<Sku> { }

public partial class ProductId : RequiredGuid<ProductId> { }

public sealed record ProductCreated(ProductId ProductId, DateTime OccurredAt) : IDomainEvent;

public sealed class Product : Aggregate<ProductId>
{
    public ProductName Name { get; private set; }
    public Sku Sku { get; private set; }
    public bool IsActive { get; private set; }

    private Product(ProductId id, ProductName name, Sku sku) : base(id)
    {
        Name = name;
        Sku = sku;
        IsActive = true;
    }

    private Product() : base(null!)
    {
        Name = null!;
        Sku = null!;
    }

    public static Result<Product> TryCreate(ProductName name, Sku sku)
    {
        var product = new Product(ProductId.NewUniqueV7(), name, sku);
        product.DomainEvents.Add(new ProductCreated(product.Id, DateTime.UtcNow));
        return Result.Ok(product);
    }

    public static Result<Product> TryCreateExisting(ProductId id, ProductName name, Sku sku) =>
        Result.Ok(new Product(id, name, sku));

    public static Product Create(ProductName name, Sku sku)
    {
        var result = TryCreate(name, sku);
        if (!result.TryGetValue(out var product))
            throw new InvalidOperationException(result.Error!.Detail);
        return product;
    }

    public static Product CreateExisting(ProductId id, ProductName name, Sku sku)
    {
        var result = TryCreateExisting(id, name, sku);
        if (!result.TryGetValue(out var product))
            throw new InvalidOperationException(result.Error!.Detail);
        return product;
    }
}
```

## Defining the factory

Two pillars:

1. **Identity is generated** inside `TryCreate` — the caller never supplies an ID.
2. **Validation runs first**; the constructor stays trivial (assignment-only).

Use `Result.Ok(aggregate)` on success and `Result.Fail<TAgg>(error)` on failure. There is no implicit `Error → Result<T>` conversion in the current API — always go through the static factory.

| Element | Convention | Why |
|---|---|---|
| Constructor visibility | `private` | Forces all callers through the factory. |
| ID parameter | First positional | Matches `Aggregate<TId>`'s base constructor. |
| Field assignment | Inside the constructor only | Keeps factories side-effect-free until the `Result.Ok` line. |
| Domain events | Added on the *new* path only | Reconstitution must not republish creation events. |
| Strong-typed inputs | `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, ... | Each primitive validates itself at construction time — see [`Required*` bases](../api_reference/trellis-api-core.md#primitive-value-object-base-classes). |

### Reconstitution path

`TryCreateExisting(TId id, ...)` exists for any caller that already knows the ID:

- importing data from an external system,
- manual rehydration (no EF Core),
- tests that need a deterministic ID,
- migrations that must preserve the existing primary key.

It runs the *same* validation as `TryCreate`, but it never raises a creation event — the aggregate already exists.

```csharp
var knownId = ProductId.Create(Guid.Parse("8e945d6d-e4f4-4dd6-bb50-3ab19f9d9fd1"));
var name = ProductName.Create("Trellis Mug");
var sku = Sku.Create("MUG-001");

var fresh = Product.TryCreate(name, sku);                    // new ID, ProductCreated raised
var rebuilt = Product.TryCreateExisting(knownId, name, sku); // ID preserved, no event
```

### Throwing helpers

`Create` / `CreateExisting` are thin wrappers around the `Try*` variants. Use them only for known-good data — fixtures, seeders, inline test setup. Production code paths should consume `Result<TAgg>` directly so the failure stays observable.

```csharp
public static Product Create(ProductName name, Sku sku)
{
    var result = TryCreate(name, sku);
    if (!result.TryGetValue(out var product))
        throw new InvalidOperationException(result.Error!.Detail);
    return product;
}
```

## Centralizing validation

Both creation paths must enforce the same rules. Extract them into a private static method that returns `Result<Unit>` (returned by the parameterless `Result.Ok()` / `Result.Fail(error)` overloads), and call it before constructing the aggregate.

```csharp
public static Result<Product> TryCreate(ProductName name, Sku sku)
{
    var validation = Validate(name, sku);
    if (validation.IsFailure)
        return Result.Fail<Product>(validation.Error!);

    var product = new Product(ProductId.NewUniqueV7(), name, sku);
    product.DomainEvents.Add(new ProductCreated(product.Id, DateTime.UtcNow));
    return Result.Ok(product);
}

public static Result<Product> TryCreateExisting(ProductId id, ProductName name, Sku sku)
{
    var validation = Validate(name, sku);
    if (validation.IsFailure)
        return Result.Fail<Product>(validation.Error!);

    return Result.Ok(new Product(id, name, sku));
}

private static Result<Unit> Validate(ProductName name, Sku sku)
{
    if (sku.Value.StartsWith("LEGACY-", StringComparison.OrdinalIgnoreCase))
        return Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty(nameof(sku)), "validation.error")
            {
                Detail = "SKU cannot start with LEGACY.",
            })));

    return Result.Ok();
}
```

> [!TIP]
> Per-field invariants (length, range, non-empty) belong on the value-object primitive itself via `[StringLength]`, `[Range]`, etc. The aggregate `Validate` method is for *cross-field* rules that no single primitive can enforce.

## Composing primitives

Strong-typed inputs eliminate most aggregate-level validation. By the time `TryCreate` runs, every primitive has already passed its own invariants.

```csharp
[StringLength(200)]
public partial class ProductName : RequiredString<ProductName> { }   // empty/whitespace + length

[StringLength(64)]
public partial class Sku : RequiredString<Sku> { }                   // empty/whitespace + length

public partial class ProductId : RequiredGuid<ProductId> { }         // Guid.Empty rejection
```

The source generator emits `TryCreate` / `Create` / `Parse` / `JsonConverter` for each primitive — full surface in the [`Required*` source-generated members table](../api_reference/trellis-api-core.md#source-generated-members). Callers convert raw input once at the boundary:

```csharp
var nameResult = ProductName.TryCreate(input.Name, fieldName: nameof(input.Name));
var skuResult  = Sku.TryCreate(input.Sku, fieldName: nameof(input.Sku));
```

## Domain events and reconstitution

A common mistake is raising "created" events while reconstituting existing data. The rule:

| Path | `DomainEvents.Add(new XxxCreated(...))`? |
|---|---|
| `TryCreate` | Yes — the aggregate is new |
| `TryCreateExisting` | No — the aggregate already exists |
| `Create` / `CreateExisting` | Inherits from the underlying `Try*` it wraps |

That keeps `UncommittedEvents()` (see [`Aggregate<TId>`](../api_reference/trellis-api-core.md#aggregatetid)) meaningful: the only events present are the ones that actually happened in this unit of work.

## Where EF Core fits

When EF Core materializes an aggregate, it uses the parameterless constructor and sets properties during rehydration — the same constructor that lets `private Product() : base(null!) { ... }` exist for infrastructure use only.

| Constructor | Purpose | Visibility |
|---|---|---|
| `private Product(ProductId id, ProductName name, Sku sku)` | Real construction; called by `TryCreate` and `TryCreateExisting` | `private` |
| `private Product()` | EF Core materialization stub | `private` |

Domain code stays on the factory methods; only the EF Core proxy ever touches the parameterless overload. `TryCreateExisting` is for *non-EF-Core* reconstitution (importers, JSON hydration, migrations).

## Composition

Aggregate factories return `Result<TAgg>`, which composes with the rest of Trellis (`Bind`, `Map`, `Ensure`, etc.). A typical write-side flow validates input primitives, calls `TryCreate`, then hands the aggregate to the persistence layer — application command handlers stay on `Result<Unit>`.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Trellis;

public sealed record CreateProductCommand(string Name, string Sku);

public sealed class CreateProductHandler(IProductRepository repo)
{
    public Task<Result<Unit>> HandleAsync(CreateProductCommand cmd, CancellationToken ct) =>
        Result.Combine(
                ProductName.TryCreate(cmd.Name, fieldName: nameof(cmd.Name)),
                Sku.TryCreate(cmd.Sku, fieldName: nameof(cmd.Sku)))
            .Bind(parts => Product.TryCreate(parts.Item1, parts.Item2))
            .BindAsync((product, token) => repo.AddAsync(product, token), ct)
            .AsUnitAsync();
}

public interface IProductRepository
{
    Task<Result<Product>> AddAsync(Product product, CancellationToken ct);
}
```

`Result.Combine` aggregates per-field failures; `BindAsync` runs the persistence step only on success; `AsUnitAsync` projects the success payload to `Unit` for the command contract.

## Practical guidance

- **Don't touch `ETag` from a factory.** It is owned by persistence infrastructure (see [`Aggregate<TId>` reference](../api_reference/trellis-api-core.md#aggregatetid)).
- **Don't call `AcceptChanges()` from a factory.** That belongs after persistence and event publication, in the repository or unit-of-work boundary.
- **Don't fetch from repositories in a factory.** Lookups belong in handlers; the factory takes already-resolved primitives and returns a fresh aggregate.
- **Pick `TryCreate` vs `Create` by audience.** Public API surfaces, command handlers, and parsers want `Result<TAgg>`. Test fixtures and inline seed data want `Create`.
- **One `Validate` method, two callers.** If you need a third creation path, add another wrapper around the same `Validate` — never duplicate the rules.
- **Use `RequireETagAsync` / `OptionalETagAsync` at the read-modify-write boundary**, not in the factory — see [`AggregateETagExtensions`](../api_reference/trellis-api-core.md#aggregateetagextensions).

## Cross-references

- API surface: [`trellis-api-core.md` → Domain-Driven Design](../api_reference/trellis-api-core.md#domain-driven-design)
- Primitive value-object bases (`Required*<TSelf>`): [`trellis-api-core.md` → Primitive value object base classes](../api_reference/trellis-api-core.md#primitive-value-object-base-classes)
- Built-in primitives (`EmailAddress`, `Money`, ...): [`trellis-api-primitives.md`](../api_reference/trellis-api-primitives.md)
- ETag-based optimistic concurrency on aggregates: [`trellis-api-core.md` → AggregateETagExtensions](../api_reference/trellis-api-core.md#aggregateetagextensions)
- EF Core conventions for aggregates and entities: [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md)
