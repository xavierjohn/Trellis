# EF Core Integration

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore)

Thin integration layer that eliminates repetitive EF Core boilerplate when using Trellis value objects and `Result<T>`.

## Installation

```bash
dotnet add package Trellis.EntityFrameworkCore
```

## Quick Start

Register all Trellis value objects as scalar properties with a single line in `ConfigureConventions`:

```csharp
using Trellis.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Scans your assembly for CustomerId, OrderStatus, etc.
        // Also auto-scans Trellis.Primitives for EmailAddress, Url, PhoneNumber, etc.
        // Also auto-maps Money properties as owned types (Amount + Currency columns)
        configurationBuilder.ApplyTrellisConventions(typeof(CustomerId).Assembly);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // No HasConversion() boilerplate needed — just configure keys, indexes, constraints
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
            b.Property(c => c.Email).HasMaxLength(254).IsRequired();
        });
    }
}
```

### Money Properties — Zero Configuration

`Money` properties are automatically mapped as owned types with proper column naming and precision.
No `OwnsOne` calls needed — just declare `Money` properties on your entities and they work.
This also applies when `Money` is declared on owned entity types, including items inside `OwnsMany` collections.

`Maybe<Money>` is also supported — it auto-configures as an optional owned type with nullable Amount/Currency columns, no `OwnsOne` needed.

| Property Name | Amount Column | Currency Column | Amount Type | Currency Type |
|---------------|---------------|-----------------|-------------|---------------|
| `Price` | `Price` | `PriceCurrency` | `decimal(18,3)` | `nvarchar(3)` |
| `ShippingCost` | `ShippingCost` | `ShippingCostCurrency` | `decimal(18,3)` | `nvarchar(3)` |

### Maybe\<T\> Properties — Source Generator + Convention

`Maybe<T>` is a `readonly struct` that EF Core cannot map as optional. Use `partial` properties — the source generator and `MaybeConvention` handle everything:

```csharp
public partial class Customer
{
    public CustomerId Id { get; set; } = null!;

    public partial Maybe<PhoneNumber> Phone { get; set; }
    public partial Maybe<DateTime> SubmittedAt { get; set; }
}
```

No `OnModelCreating` configuration needed. When `T` is a composite owned type (e.g., `Money`), it creates an optional ownership navigation instead of a scalar column. Querying uses dedicated extensions:

```csharp
var withoutPhone = await context.Customers.WhereNone(c => c.Phone).ToListAsync(ct);
var withPhone    = await context.Customers.WhereHasValue(c => c.Phone).ToListAsync(ct);
var matches      = await context.Customers.WhereEquals(c => c.Phone, phone).ToListAsync(ct);
var ordered      = await context.Customers.WhereHasValue(c => c.Phone).OrderByMaybe(c => c.Phone).ToListAsync(ct);
```

Indexes, bulk updates, and diagnostics also stay strongly typed:

```csharp
modelBuilder.Entity<Customer>(builder => builder.HasTrellisIndex(c => c.Phone));

await context.Customers
    .Where(c => c.Id == customerId)
    .ExecuteUpdateAsync(setters => setters.SetMaybeValue(c => c.Phone, phone), ct);

var mappings = context.GetMaybePropertyMappings();
var debugView = context.ToMaybeMappingDebugString();
```

## Result-Returning SaveChanges

```csharp
// Returns Result<int> instead of throwing on conflicts or FK violations
var result = await context.SaveChangesResultAsync(ct);

// Returns Result<Unit> when you don't need the count
var result = await context.SaveChangesResultUnitAsync(ct);
```

| Exception | Error Type |
|-----------|------------|
| `DbUpdateConcurrencyException` | `ConflictError` |
| Duplicate key (unique constraint) | `ConflictError` |
| Foreign key violation | `DomainError` |

## Query Extensions

```csharp
// Maybe-returning queries (no exception on missing)
Maybe<Customer> customer = await context.Customers
    .FirstOrDefaultMaybeAsync(c => c.Id == customerId, ct);

// Result-returning queries
Result<Customer> customer = await context.Customers
    .FirstOrDefaultResultAsync(
        c => c.Id == customerId,
        Error.NotFound("Customer", customerId),
        ct);

// Specification pattern
var activeSpec = new ActiveCustomerSpec();
var activeCustomers = await context.Customers
    .Where(activeSpec)
    .ToListAsync(ct);
```

## Related Packages

- [Trellis.Results](https://www.nuget.org/packages/Trellis.Results) — Core `Result<T>` and `Maybe<T>` types
- [Trellis.Primitives](https://www.nuget.org/packages/Trellis.Primitives) — Value object base classes and built-in types
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — `Specification<T>`, `Entity<T>`, `Aggregate<T>`

## License

MIT — see [LICENSE](https://github.com/xavierjohn/Trellis/blob/main/LICENSE) for details.
