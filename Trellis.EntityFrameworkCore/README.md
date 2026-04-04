# Trellis.EntityFrameworkCore — EF Core Integration

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore)

Thin integration layer that eliminates repetitive EF Core boilerplate when using Trellis value objects and `Result<T>`.

## Table of Contents

- [Installation](#installation)
- [Convention-Based Value Converters](#convention-based-value-converters)
- [Composite Value Object Convention](#composite-value-object-convention)
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
| `Money` | Structured owned type: `decimal(18,3)` + `nvarchar(3)` | Auto-mapped as owned entity (Amount + Currency columns) |
| Custom composite `ValueObject` | Structured owned type: one column per property | Auto-mapped as owned entity (no `OwnsOne` needed) |

This covers converter-based scalar/symbolic types (`RequiredString<T>`, `RequiredGuid<T>`, `RequiredInt<T>`, `RequiredDecimal<T>`, `RequiredEnum<T>`, `EmailAddress`, and any custom `ScalarValueObject<TSelf, T>`) plus `Money` and other composite `ValueObject` types as structured owned types.

### Multiple Assemblies

If your value objects span multiple assemblies, pass them all:

```csharp
configurationBuilder.ApplyTrellisConventions(
    typeof(CustomerId).Assembly,      // Your domain assembly
    typeof(SharedTypes).Assembly);    // Another assembly
// Trellis.Primitives is always included automatically
```

## Composite Value Object Convention

Composite value objects — types extending `ValueObject` but not implementing `IScalarValue` — are automatically registered as EF Core owned types. No `OwnsOne` configuration needed.

```csharp
public class Address : ValueObject
{
    public string Street { get; private set; }
    public string City { get; private set; }
    public string State { get; private set; }
    public string ZipCode { get; private set; }

    private Address() { Street = City = State = ZipCode = null!; } // EF Core
    public Address(string street, string city, string state, string zipCode)
    { Street = street; City = city; State = state; ZipCode = zipCode; }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    { yield return Street; yield return City; yield return State; yield return ZipCode; }
}

public class Customer
{
    public CustomerId Id { get; set; } = null!;
    public Address ShippingAddress { get; set; } = null!;     // required (4 NOT NULL columns)
    public Address BillingAddress { get; set; } = null!;      // required (4 NOT NULL columns)
}
```

`Maybe<T>` is also supported for optional composite value objects:

```csharp
public partial class Customer
{
    public Address ShippingAddress { get; set; } = null!;           // required
    public partial Maybe<Address> BillingAddress { get; set; }      // optional (4 nullable columns)
}
```

Column naming uses EF Core's default owned-type convention. `Money` retains its specialized column naming via `MoneyConvention`.

Explicit `OwnsOne` in `OnModelCreating` takes precedence over the convention for custom column names or settings.

> [!NOTE]
> Optional composite value objects with nested owned types (e.g., `Maybe<Address>` where `Address` has a `Money` property) are supported. The convention maps these to a **separate table** (`{OwnerType}_{PropertyName}`) instead of nullable columns on the owner. All columns remain NOT NULL; optionality is expressed by row presence/absence. Explicit `OwnsOne` with `ToTable()` can override the table name.

## Money Property Convention

`Money` properties on entities are automatically mapped as owned types — no `OwnsOne` configuration needed.
That behavior is intentional because `Money` is a structured value object, not a scalar wrapper with a single persisted `Value`.
This also applies when `Money` is declared on owned entity types, including items inside `OwnsMany` collections.

`Maybe<Money>` is also supported — it auto-configures as an optional owned type with nullable Amount/Currency columns:

```csharp
public partial class Penalty : Aggregate<PenaltyId>
{
    public Money Fine { get; set; } = null!;              // required (2 NOT NULL columns)
    public partial Maybe<Money> FinePaid { get; set; }    // optional (2 nullable columns)
}
// Columns: FinePaid (nullable decimal), FinePaidCurrency (nullable nvarchar)
```

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

No `OnModelCreating` configuration needed — `MaybeConvention` (registered by `ApplyTrellisConventions`) auto-discovers `Maybe<T>` properties, maps the generated `_camelCase` storage member as nullable, and sets the column name to the property name. When `T` is a composite owned type (e.g., `Money`), it creates an optional ownership navigation instead of a scalar column — see the [Money Property Convention](#money-property-convention) section above.

### Column Naming

| Property | Storage Member | Column Name |
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

`HasTrellisIndex` resolves `Maybe<T>` properties to their mapped storage members while leaving regular properties unchanged, so mixed composite indexes stay strongly typed.

### Mapping Diagnostics

You can inspect resolved `Maybe<T>` mappings at runtime to verify the generated storage member, column name, and provider type:

```csharp
var mappings = context.GetMaybePropertyMappings();
var debugView = context.ToMaybeMappingDebugString();
```

### TRLSGEN100

If a `Maybe<T>` property is not declared `partial`, the generator emits diagnostic `TRLSGEN100`.

## Aggregate Conventions

`ApplyTrellisConventions` registers two conventions for `Aggregate<TId>` types:

- **`AggregateETagConvention`** — marks `ETag` as `IsConcurrencyToken()` with `MaxLength(50)` for optimistic concurrency
- **`AggregateTransientPropertyConvention`** — auto-ignores the transient `IsChanged` base-class property that reflects in-memory state and must not be persisted

No manual `builder.Ignore(o => o.IsChanged)` is needed in `OnModelCreating`. The convention handles `IsChanged` automatically for all aggregate types, including derived aggregates that hide it via `new`.

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

1. **Symbolic value objects** such as `RequiredEnum<TSelf>` — detected from the base type and mapped with a string provider plus `TryFromName()` reconstruction
2. **Scalar value objects** implementing `IScalarValue<TSelf, TPrimitive>` — interface-based detection where the `TPrimitive` type argument determines the database column type
3. **Composite value objects** extending `ValueObject` without `IScalarValue` — registered as owned types via `CompositeValueObjectConvention`

### Expression Tree Converters

`TrellisScalarConverter<TModel, TProvider>` builds compiled expression trees for both scalar and symbolic value objects:

```
To Database:    v => v.Value
From Database:  v => TryCreate(v, null) or TryFromName(v, null)
```

Expression trees are preserved so EF Core can translate them for LINQ query translation.
If persisted data is invalid, materialization throws `TrellisPersistenceMappingException` with the value object type, persisted value, factory method, and validation detail.

## Related Packages

- [Trellis.Results](https://www.nuget.org/packages/Trellis.Results) — Core `Result<T>` and `Maybe<T>` types
- [Trellis.Primitives](https://www.nuget.org/packages/Trellis.Primitives) — Value object base classes and built-in types
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — `Specification<T>`, `Entity<T>`, `Aggregate<T>`
- [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) — ASP.NET Core integration

## License

MIT — see [LICENSE](https://github.com/xavierjohn/Trellis/blob/main/LICENSE) for details.
