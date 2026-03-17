# Trellis.EntityFrameworkCore — EF Core Integration

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore)

Thin integration layer that eliminates repetitive EF Core boilerplate when using Trellis value objects and `Result<T>`.

## Table of Contents

- [Installation](#installation)
- [Convention-Based Value Converters](#convention-based-value-converters)
- [Money Property Convention](#money-property-convention)
- [Maybe\<T\> Property Convention](#maybetproperty-convention)
- [Result-Returning SaveChanges](#result-returning-savechanges)
- [Query Extensions](#query-extensions)
- [Database Exception Classification](#database-exception-classification)
- [How It Works](#how-it-works)
- [Related Packages](#related-packages)

## Installation

```bash
dotnet add package Trellis.EntityFrameworkCore
```

## Convention-Based Value Converters

### The Problem

Without `Trellis.EntityFrameworkCore`, every value object property requires an inline `HasConversion()` call:

```csharp
// ❌ Repetitive boilerplate for every property
builder.Property(c => c.Id)
    .HasConversion(id => id.Value, guid => CustomerId.Create(guid));
builder.Property(c => c.Name)
    .HasConversion(name => name.Value, str => CustomerName.Create(str));
builder.Property(c => c.Email)
    .HasConversion(email => email.Value, str => EmailAddress.Create(str));
// ... repeated for every value object in every entity
```

### The Solution

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
        // ✅ No HasConversion() — just configure keys, indexes, constraints
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
            b.Property(c => c.Email).HasMaxLength(254).IsRequired();
        });
    }
}
```

### What Gets Registered

| Value Object Base | Database Type | Converter |
|-------------------|---------------|-----------|
| `IScalarValue<TSelf, TPrimitive>` | `TPrimitive` (string, Guid, int, decimal) | `Value` → DB, `Create()` ← DB |
| `RequiredEnum<TSelf>` | `string` | `Value` → DB, `TryFromName()` ← DB |
| `Money` | Owned type: `decimal(18,3)` + `nvarchar(3)` | Auto-mapped as owned entity (Amount + Currency columns) |

This covers all built-in types: `RequiredString<T>`, `RequiredGuid<T>`, `RequiredInt<T>`, `RequiredDecimal<T>`, `RequiredEnum<T>`, `EmailAddress`, `Money`, and any custom `ScalarValueObject<TSelf, T>`.

### Multiple Assemblies

If your value objects span multiple assemblies, pass them all:

```csharp
configurationBuilder.ApplyTrellisConventions(
    typeof(CustomerId).Assembly,      // Your domain assembly
    typeof(SharedTypes).Assembly);    // Another assembly
// Trellis.Primitives is always included automatically
```

## Money Property Convention

`Money` properties on entities are automatically mapped as owned types — no `OwnsOne` configuration needed.
This also applies when `Money` is declared on owned entity types, including items inside `OwnsMany` collections.

```csharp
public class Order
{
    public OrderId Id { get; set; } = null!;
    public Money Price { get; set; } = null!;
    public Money ShippingCost { get; set; } = null!;
}
```

### Column Naming

| Property Name | Amount Column | Currency Column |
|---------------|---------------|------------------|
| `Price` | `Price` | `PriceCurrency` |
| `ShippingCost` | `ShippingCost` | `ShippingCostCurrency` |

Amount columns use `decimal(18,3)` precision. Currency columns use `nvarchar(3)` (ISO 4217). Scale 3 accommodates all ISO 4217 minor units (0 for JPY, 2 for USD/EUR, 3 for BHD/KWD/OMR/TND).

### Explicit Override

If you need custom column names or settings, use `OwnsOne` in `OnModelCreating` — explicit configuration takes precedence over the convention:

```csharp
modelBuilder.Entity<Order>(b =>
{
    b.OwnsOne(o => o.Price, money =>
    {
        money.Property(m => m.Amount).HasColumnName("UnitPrice").HasPrecision(19, 4);
        money.Property(m => m.Currency).HasColumnName("UnitCurrency");
    });
});
```

## Maybe\<T\> Property Convention

`Maybe<T>` is a `readonly struct` which EF Core cannot map as optional. The `Trellis.EntityFrameworkCore.Generator` source generator and `MaybeConvention` eliminate all boilerplate — just declare `partial Maybe<T>` properties:

```csharp
public partial class Customer
{
    public CustomerId Id { get; set; } = null!;
    public CustomerName Name { get; set; } = null!;

    public partial Maybe<PhoneNumber> Phone { get; set; }
    public partial Maybe<DateTime> SubmittedAt { get; set; }
}
```

No `OnModelCreating` configuration needed — `MaybeConvention` (registered by `ApplyTrellisConventions`) auto-discovers `Maybe<T>` properties, maps the generated `_camelCase` backing field as nullable, and sets the column name to the property name.

### Column Naming

| Property | Backing Field | Column Name |
|----------|---------------|-------------|
| `Phone` | `_phone` | `Phone` |
| `SubmittedAt` | `_submittedAt` | `SubmittedAt` |

### Querying Maybe\<T\> Properties

Because `MaybeConvention` ignores the `Maybe<T>` CLR property, use the query extensions for LINQ:

```csharp
var withoutPhone = await context.Customers.WhereNone(c => c.Phone).ToListAsync(ct);
var withPhone    = await context.Customers.WhereHasValue(c => c.Phone).ToListAsync(ct);
var matches      = await context.Customers.WhereEquals(c => c.Phone, phone).ToListAsync(ct);

var ordered      = await context.Customers
    .WhereHasValue(c => c.Phone)
    .OrderByMaybe(c => c.Phone)
    .ToListAsync(ct);
```

### Indexing and Bulk Updates

Use the CLR property helpers instead of string literals when you need indexes or `ExecuteUpdate` support:

```csharp
modelBuilder.Entity<Customer>(builder =>
{
    builder.HasKey(c => c.Id);
    builder.HasTrellisIndex(c => c.Phone);
    builder.HasTrellisIndex(c => new { c.Name, c.SubmittedAt });
});

await context.Customers
    .Where(c => c.Id == customerId)
    .ExecuteUpdateAsync(setters => setters.SetMaybeValue(c => c.Phone, phone), ct);

await context.Customers
    .Where(c => c.Id == customerId)
    .ExecuteUpdateAsync(setters => setters.SetMaybeNone(c => c.Phone), ct);
```

`HasTrellisIndex` resolves `Maybe<T>` properties to their mapped backing fields while leaving regular properties unchanged, so mixed composite indexes stay strongly typed.

### Mapping Diagnostics

You can inspect resolved `Maybe<T>` mappings at runtime to verify the generated field, column name, and provider type:

```csharp
var mappings = context.GetMaybePropertyMappings();
var debugView = context.ToMaybeMappingDebugString();
```

### TRLSGEN100

If a `Maybe<T>` property is not declared `partial`, the generator emits diagnostic `TRLSGEN100`.

## Result-Returning SaveChanges

Wraps `SaveChangesAsync` to return `Result<int>` instead of throwing on database conflicts:

```csharp
var result = await context.SaveChangesResultAsync(ct);

result.Match(
    count => Console.WriteLine($"Saved {count} changes"),
    error => Console.WriteLine($"Save failed: {error.Detail}"));

// Returns Result<Unit> when you don't need the count
var result = await context.SaveChangesResultUnitAsync(ct);
```

| Exception | Error Type |
|-----------|------------|
| `DbUpdateConcurrencyException` | `ConflictError` |
| Duplicate key (unique constraint) | `ConflictError` |
| Foreign key violation | `DomainError` |

## Query Extensions

### Maybe-Returning Queries

Returns `Maybe<T>` instead of null when a record might not exist:

```csharp
Maybe<Customer> customer = await context.Customers
    .FirstOrDefaultMaybeAsync(c => c.Id == customerId, ct);

customer.Match(
    c => Console.WriteLine($"Found: {c.Name}"),
    () => Console.WriteLine("Customer not found"));

Maybe<Order> order = await context.Orders
    .SingleOrDefaultMaybeAsync(o => o.Id == orderId, ct);
```

### Result-Returning Queries

Returns `Result<T>` with a meaningful error when a record must exist:

```csharp
Result<Customer> customer = await context.Customers
    .FirstOrDefaultResultAsync(
        c => c.Id == customerId,
        Error.NotFound("Customer", customerId),
        ct);
```

### Specification Pattern

Integrates with `Specification<T>` for composable query filters:

```csharp
var activeSpec = new ActiveCustomerSpec();
var highValueSpec = new HighValueCustomerSpec();

// Compose specifications
var activeCustomers = await context.Customers
    .Where(activeSpec)
    .ToListAsync(ct);

var vipCustomers = await context.Customers
    .Where(activeSpec.And(highValueSpec))
    .ToListAsync(ct);
```

## Database Exception Classification

Provider-agnostic exception classification for SQL Server, PostgreSQL, and SQLite:

```csharp
try
{
    await context.SaveChangesAsync(ct);
}
catch (DbUpdateException ex)
{
    if (DbExceptionClassifier.IsDuplicateKey(ex))
        // Handle unique constraint violation

    if (DbExceptionClassifier.IsForeignKeyViolation(ex))
        // Handle referential integrity violation

    var detail = DbExceptionClassifier.ExtractConstraintDetail(ex);
    // Provider-specific constraint info (e.g., constraint name)
}
```

> **Note:** You rarely need `DbExceptionClassifier` directly — `SaveChangesResultAsync` uses it internally to classify exceptions into appropriate `Error` types.

## How It Works

### ConfigureConventions (Pre-Convention Registration)

`ApplyTrellisConventions` runs in `ConfigureConventions`, which executes **before** EF Core's convention engine. This is critical because:

1. EF Core's convention engine classifies class-typed properties as **navigations** (relationships) by default
2. Properties classified as navigations cannot have value converters applied in `OnModelCreating`
3. By registering type-level converters in `ConfigureConventions`, EF Core knows to treat value objects as **scalars** from the start

### Type Detection

The scanner checks each type in the provided assemblies:

1. **`RequiredEnum<TSelf>`** — checked first because it implements `IScalarValue<TSelf, string>` but still needs a different converter because it reconstructs via `TryFromName()` instead of `Create()`
2. **`IScalarValue<TSelf, TPrimitive>`** — interface-based detection for all scalar value objects. The `TPrimitive` type argument determines the database column type

### Expression Tree Converters

`TrellisScalarConverter<TModel, TProvider>` and `TrellisEnumConverter<TModel>` build compiled expression trees:

```
To Database:    v => v.Value          (or v => v.Name for enums)
From Database:  v => TModel.Create(v) (or TryFromName(v, null).Value for enums)
```

Expression trees are preserved so EF Core can translate them for LINQ query translation.

## Related Packages

- [Trellis.Results](https://www.nuget.org/packages/Trellis.Results) — Core `Result<T>` and `Maybe<T>` types
- [Trellis.Primitives](https://www.nuget.org/packages/Trellis.Primitives) — Value object base classes and built-in types
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — `Specification<T>`, `Entity<T>`, `Aggregate<T>`
- [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) — ASP.NET Core integration

## License

MIT — see [LICENSE](https://github.com/xavierjohn/Trellis/blob/main/LICENSE) for details.
