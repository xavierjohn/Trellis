# Aggregate Factory Pattern

When an aggregate can be both **created from scratch** and **reconstituted from existing data**, one factory method is not enough.

That is the problem this pattern solves.

- new aggregates need a **new ID**
- reconstituted aggregates must keep an **existing ID**
- both paths should enforce the same domain invariants

> [!TIP]
> `Aggregate<TId>` does not impose factory methods for you. The pattern below is a Trellis-friendly convention for keeping creation explicit and safe.

## The recommended shape

Use two factory paths:

| Method | Use it for | ID behavior |
| --- | --- | --- |
| `TryCreate(...)` | New aggregate | Generates a new ID |
| `TryCreateExisting(id, ...)` | Reconstitution, migrations, tests with known IDs | Preserves the supplied ID |
| `Create(...)` | Same as `TryCreate`, but for known-good data | Throws on failure |
| `CreateExisting(id, ...)` | Same as `TryCreateExisting`, but for known-good data | Throws on failure |

## A working example

```csharp
using Trellis;
using Trellis.Primitives;

namespace AggregateFactories;

[Trellis.StringLength(200)]
public partial class ProductName : RequiredString<ProductName> { }

[Trellis.StringLength(64)]
public partial class Sku : RequiredString<Sku> { }

public partial class ProductId : RequiredGuid<ProductId> { }

public sealed record ProductCreated(ProductId ProductId, DateTime OccurredAt) : IDomainEvent;

public sealed class Product : Aggregate<ProductId>
{
    private Product(ProductId id, ProductName name, Sku sku)
        : base(id)
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

    public ProductName Name { get; private set; }
    public Sku Sku { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<Product> TryCreate(ProductName name, Sku sku)
    {
        var product = new Product(ProductId.NewUniqueV7(), name, sku);
        product.DomainEvents.Add(new ProductCreated(product.Id, DateTime.UtcNow));
        return Result.Ok(product);
    }

    public static Result<Product> TryCreateExisting(ProductId id, ProductName name, Sku sku)
    {
        var product = new Product(id, name, sku);
        return Result.Ok(product);
    }

    public static Product Create(ProductName name, Sku sku)
    {
        var result = TryCreate(name, sku);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Detail);

        return result.Value;
    }

    public static Product CreateExisting(ProductId id, ProductName name, Sku sku)
    {
        var result = TryCreateExisting(id, name, sku);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Detail);

        return result.Value;
    }
}
```

## Why two methods are better than one

If `TryCreate(...)` always generates an ID, you cannot safely rebuild an existing aggregate:

```csharp
var knownId = ProductId.Create(Guid.Parse("8e945d6d-e4f4-4dd6-bb50-3ab19f9d9fd1"));
var name = ProductName.Create("Trellis Mug");
var sku = Sku.Create("MUG-001");

var newProduct = Product.Create(name, sku);                     // new ID
var existingProduct = Product.CreateExisting(knownId, name, sku); // preserved ID
```

That distinction matters for:

- **manual rehydration**
- **tests that need fixed IDs**
- **data import or migration code**
- **reconstitution outside EF Core**

## Where EF Core fits

When EF Core materializes an aggregate, it typically uses the parameterless constructor and sets properties during rehydration.

That is why many Trellis aggregates use this shape:

- parameterless constructor for EF Core
- private constructor with all required state
- `TryCreate(...)` for new instances
- `TryCreateExisting(...)` for explicit reconstitution outside EF Core

> [!NOTE]
> The parameterless constructor is for infrastructure. Your domain code should still prefer explicit factory methods.

## Keep validation in one place

Both creation paths should enforce the same rules. A simple way to do that is to validate the arguments before either constructor call.

```csharp
public static Result<Product> TryCreate(ProductName name, Sku sku)
{
    var validation = Validate(name, sku);
    if (validation.IsFailure)
        return validation.Error;

    var product = new Product(ProductId.NewUniqueV7(), name, sku);
    product.DomainEvents.Add(new ProductCreated(product.Id, DateTime.UtcNow));
    return Result.Ok(product);
}

public static Result<Product> TryCreateExisting(ProductId id, ProductName name, Sku sku)
{
    var validation = Validate(name, sku);
    if (validation.IsFailure)
        return validation.Error;

    return Result.Ok(new Product(id, name, sku));
}

private static Result Validate(ProductName name, Sku sku)
{
    if (sku.Value.StartsWith("LEGACY-", StringComparison.OrdinalIgnoreCase))
        return new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(sku)), "validation.error") { Detail = "SKU cannot start with LEGACY." }));

    return Result.Ok();
}
```

## Domain events and reconstitution

A common mistake is raising “created” events while reconstituting existing data.

Use this rule:

- `TryCreate(...)` may raise creation events
- `TryCreateExisting(...)` usually should **not**

That keeps `UncommittedEvents()` meaningful.

## What this pattern does **not** do

These concerns belong elsewhere:

- **`ETag`** is infrastructure-managed
- **`AcceptChanges()`** belongs after persistence/event publication
- **repository lookups** belong in repositories or handlers, not in the aggregate constructor

> [!WARNING]
> Do not assign `ETag` in your factory methods. `ETag` exists for optimistic concurrency and is owned by persistence infrastructure.

## A practical checklist

Use this pattern when your aggregate:

- has a strong identity type like `ProductId`
- needs a constructor for EF Core
- must support both new and existing instances
- raises domain events for true state changes

## Summary

The aggregate factory pattern gives you:

- clear intent
- correct identity handling
- safer tests
- cleaner reconstitution paths
- fewer accidental domain events

If the aggregate is new, generate a new ID. If it already exists, preserve the ID you were given.
