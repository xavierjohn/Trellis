# Trellis.EntityFrameworkCore — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-ddd.md, trellis-api-primitives.md.

**Package:** `Trellis.EntityFrameworkCore` | **Namespace:** `Trellis.EntityFrameworkCore`

**Namespace: `Trellis.EntityFrameworkCore`**

### DbContext Extensions

`SaveChangesResultAsync()` and `SaveChangesResultUnitAsync()` wrap EF Core `SaveChanges` in `Result<T>`. Duplicate key violations become `ConflictError`; concurrency exceptions become `ConflictError`.

```csharp
Task<Result<int>> SaveChangesResultAsync(this DbContext context, CancellationToken cancellationToken = default)
Task<Result<int>> SaveChangesResultAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, CancellationToken cancellationToken = default)
Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
// DbUpdateConcurrencyException → Error.Conflict
// Duplicate key → Error.Conflict
// FK violation → Error.Domain
```

### Queryable Extensions

`FirstOrDefaultResultAsync` returns `NotFoundError` if missing; `FirstOrDefaultMaybeAsync` returns `Maybe<T>.None` if missing; `SingleOrDefaultMaybeAsync` for unique-or-none queries.

```csharp
Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Error notFoundError, CancellationToken cancellationToken = default)
Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, Error notFoundError, CancellationToken cancellationToken = default)
IQueryable<T> Where<T>(this IQueryable<T> query, Specification<T> specification)
```

### Value Converter Registration

`ApplyTrellisConventions()` in `ConfigureConventions` registers value converters for all `IScalarValue` types, `Money`, and composite `ValueObject` types. Also registers `AggregateETagConvention` for optimistic concurrency and `AggregateTransientPropertyConvention` to auto-ignore transient base-class properties. Call once — do NOT add manual `HasConversion` for Trellis types.

```csharp
// In ConfigureConventions (NOT OnModelCreating)
configurationBuilder.ApplyTrellisConventions(typeof(Order).Assembly);
// Auto-registers converters for all IScalarValue and RequiredEnum types
// Auto-maps Money properties as owned types (Amount + Currency columns)
// Auto-maps composite ValueObject types as owned types (e.g., Address → Street, City, State, ZipCode columns)
// MonetaryAmount maps to a single decimal column (scalar value object convention)
// Auto-marks Aggregate<TId>.ETag as IsConcurrencyToken()
// Auto-ignores Aggregate<TId>.IsChanged (transient property, not persisted)
```

### NativeAOT Compiled Model Support

`TrellisScalarConverter<TModel, TProvider>` is fully compatible with EF Core's compiled model generator (`dotnet ef dbcontext optimize`). The `MaterializeScalar` and `MaterializeSymbolic` methods are public static, allowing the generated code to reference them directly without reflection. This enables `PublishAot=true` for applications using Trellis value objects with EF Core.

```bash
# Generate compiled model for NativeAOT publishing
dotnet ef dbcontext optimize --output-dir CompiledModels --namespace MyApp.CompiledModels
```

### Aggregate ETag Convention and Interceptor

Optimistic concurrency is automatic for all `Aggregate<TId>` entities:

- **`AggregateETagConvention`** (registered by `ApplyTrellisConventions`): marks `ETag` as `IsConcurrencyToken()` on entities implementing `IAggregate`
- **`AggregateETagInterceptor`** (registered by `AddTrellisInterceptors()`): generates a new GUID-based ETag on `EntityState.Modified` aggregate entries before `SaveChanges`

No additional configuration is needed. When two processes modify the same aggregate concurrently, the second `SaveChangesResultAsync` returns `ConflictError`. At the HTTP layer, use `OptionalETag` for `If-Match` validation → `PreconditionFailedError` (412).

### Aggregate Transient Property Convention

`AggregateTransientPropertyConvention` (registered by `ApplyTrellisConventions`) automatically excludes transient base-class properties from the EF Core model for all `Aggregate<TId>` entities:

- **`IsChanged`** (from `IChangeTracking`): reflects in-memory state (uncommitted domain events), not persisted

No `builder.Ignore(o => o.IsChanged)` needed in `OnModelCreating`. The convention handles this automatically for all aggregate types, including derived aggregates that hide `IsChanged` via `new`.

### EntityTimestampInterceptor

Automatically sets `IEntity.CreatedAt` and `IEntity.LastModified` on every `SaveChanges` call for entities implementing the `IEntity` interface (from `Trellis.DomainDrivenDesign`). Registered by `AddTrellisInterceptors()`.

```csharp
public sealed class EntityTimestampInterceptor : SaveChangesInterceptor
{
    public EntityTimestampInterceptor(TimeProvider? timeProvider = null)
}
```

- Always uses `_timeProvider.GetUtcNow()` for timestamps; when no `TimeProvider` is supplied, `_timeProvider` defaults to `TimeProvider.System`
- Pass a custom `TimeProvider` via `AddTrellisInterceptors(timeProvider)` to override `TimeProvider.System` for deterministic testing
- Sets `CreatedAt` on `EntityState.Added` entries only when `CreatedAt` is still its default value, preserving any pre-set timestamps
- Sets `LastModified` on both `EntityState.Added` and `EntityState.Modified` entries

### Money Property Convention

`Money` properties on entities are automatically mapped as owned types — no `OwnsOne` configuration needed. This includes `Money` properties declared on owned entity types (e.g., items inside `OwnsMany` collections). Column naming convention:

| Property Name | Amount Column | Currency Column | Amount Type | Currency Type |
|---------------|---------------|-----------------|-------------|---------------|
| `Price` | `Price` | `PriceCurrency` | `decimal(18,3)` | `nvarchar(3)` |
| `ShippingCost` | `ShippingCost` | `ShippingCostCurrency` | `decimal(18,3)` | `nvarchar(3)` |

Explicit `OwnsOne` configuration takes precedence over the convention.

`Maybe<Money>` properties are also supported — `MaybeConvention` creates an optional ownership navigation with nullable Amount/Currency columns. No manual `OwnsOne` needed.

### Composite Value Object Convention

Any `ValueObject` subclass that does not implement `IScalarValue` is automatically registered as an EF Core owned type by `CompositeValueObjectConvention`. This covers user-defined types like `Address`, `DateRange`, `GeoCoordinate`, etc.

Composite value objects need a private parameterless constructor for EF Core materialization. Use the `[OwnedEntity]` attribute on a `partial` class to auto-generate it:

```csharp
[OwnedEntity]
public partial class Address : ValueObject
{
    public string Street { get; private set; }
    public string City { get; private set; }
    public string State { get; private set; }

    public Address(string street, string city, string state)
    {
        Street = street;
        City = city;
        State = state;
    }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
    }
}
// Generator emits: private Address() { Street = null!; City = null!; State = null!; }
```

**Diagnostics:**
- `TRLSGEN101` (Error) — `[OwnedEntity]` on a non-`partial` type
- `TRLSGEN102` (Warning) — `[OwnedEntity]` on a type that already has a parameterless constructor

```csharp
public partial class Customer : Aggregate<CustomerId>
{
    public Address ShippingAddress { get; set; } = null!;           // required owned type
    public partial Maybe<Address> BillingAddress { get; set; }      // optional owned type (nullable columns)
}
```

For `Maybe<T>` composites, columns are marked nullable and named `{PropertyName}_{OwnedPropertyName}`. For required composites, EF Core's default owned-type column naming applies. `Money` retains its specialized naming via `MoneyConvention`. Explicit `OwnsOne` takes precedence.

### Maybe\<T\> Property Mapping

`Maybe<T>` is a `readonly struct`. EF Core cannot mark non-nullable struct properties as optional — calling `IsRequired(false)` or setting `IsNullable = true` throws `InvalidOperationException`. Use C# 13 `partial` properties with the `Trellis.EntityFrameworkCore.Generator` source generator:

```csharp
// Entity — just declare partial Maybe<T> properties
public partial class Customer
{
    public CustomerId Id { get; set; } = null!;

    public partial Maybe<PhoneNumber> Phone { get; set; }

    public partial Maybe<DateTime> SubmittedAt { get; set; }
}

// OnModelCreating — no configuration needed for Maybe<T>, convention handles everything
modelBuilder.Entity<Customer>(b =>
{
    b.HasKey(c => c.Id);
});
```

The source generator emits a private `_camelCase` backing field and getter/setter for each `partial Maybe<T>` property. The `MaybeConvention` (registered by `ApplyTrellisConventions`) auto-discovers `Maybe<T>` properties, ignores the struct property, maps the backing field as nullable, and sets the column name to the property name. When `T` is a composite owned type (e.g., `Money`, `Address`, or any custom `ValueObject`), `MaybeConvention` creates an optional ownership navigation instead of a scalar column. For table-split composites (no nested owned types), `CompositeValueObjectConvention` marks all columns nullable; for composites with nested owned navigations, the convention maps the dependent to a separate table with NOT NULL columns — optionality is expressed by row presence/absence.

Backing field naming: `Phone` → `_phone`, `SubmittedAt` → `_submittedAt`, `AlternateEmail` → `_alternateEmail`.

If a `Maybe<T>` property is not declared `partial`, the generator emits diagnostic `TRLSGEN100`.

**Troubleshooting:** If the generator produces no output despite correct `partial` declarations, run a clean build (`dotnet clean` followed by `dotnet build`). Stale incremental build artifacts can prevent the generator from executing.

### Maybe\<T\> Queryable Extensions

> **Recommended approach:** Register `AddTrellisInterceptors()` in your DbContext options — this enables both the `MaybeQueryInterceptor` (for `Maybe<T>` properties) and the `ScalarValueQueryInterceptor` (for natural value object comparisons, string methods, and properties in LINQ). The helper methods below (`WhereNone`, `WhereHasValue`, etc.) are available as alternatives when the interceptor is not registered or for explicit control.

Because `MaybeConvention` ignores the `Maybe<T>` CLR property, EF Core cannot translate direct LINQ references to it. Use these extension methods instead of raw `EF.Property` calls:

```csharp
// WhereNone — WHERE backing_field IS NULL
IQueryable<TEntity> WhereNone<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// WhereHasValue — WHERE backing_field IS NOT NULL
IQueryable<TEntity> WhereHasValue<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// WhereEquals — WHERE backing_field = @value
IQueryable<TEntity> WhereEquals<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
    TInner value)

// WhereLessThan — WHERE backing_field < @value (TInner : IComparable<TInner>)
IQueryable<TEntity> WhereLessThan<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
    TInner value)

// WhereLessThanOrEqual — WHERE backing_field <= @value
IQueryable<TEntity> WhereLessThanOrEqual<TEntity, TInner>(...)

// WhereGreaterThan — WHERE backing_field > @value
IQueryable<TEntity> WhereGreaterThan<TEntity, TInner>(...)

// WhereGreaterThanOrEqual — WHERE backing_field >= @value
IQueryable<TEntity> WhereGreaterThanOrEqual<TEntity, TInner>(...)

// OrderByMaybe — ORDER BY backing_field ASC
IOrderedQueryable<TEntity> OrderByMaybe<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// OrderByMaybeDescending — ORDER BY backing_field DESC
IOrderedQueryable<TEntity> OrderByMaybeDescending<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// ThenByMaybe — THEN BY backing_field ASC
IOrderedQueryable<TEntity> ThenByMaybe<TEntity, TInner>(
    this IOrderedQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// ThenByMaybeDescending — THEN BY backing_field DESC
IOrderedQueryable<TEntity> ThenByMaybeDescending<TEntity, TInner>(
    this IOrderedQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// Usage — equality and null checks
var withoutPhone = await context.Customers.WhereNone(c => c.Phone).ToListAsync(ct);
var withPhone    = await context.Customers.WhereHasValue(c => c.Phone).ToListAsync(ct);
var matches      = await context.Customers.WhereEquals(c => c.Phone, phone).ToListAsync(ct);
var ordered      = await context.Customers.WhereHasValue(c => c.Phone).OrderByMaybe(c => c.Phone).ToListAsync(ct);

// Usage — comparison operators (for Maybe<DateTime>, Maybe<int>, etc.)
var cutoff = DateTime.UtcNow.AddDays(-7);
var overdue = await context.Orders
    .Where(o => o.Status == OrderStatus.Submitted)
    .WhereLessThan(o => o.SubmittedAt, cutoff)
    .ToListAsync(ct);
```

### AddTrellisInterceptors

Registers the `MaybeQueryInterceptor`, `ScalarValueQueryInterceptor`, `AggregateETagInterceptor`, and `EntityTimestampInterceptor` as singletons, enabling natural LINQ syntax with `Maybe<T>` properties and natural value object operations (comparisons, string methods, properties) without `.Value`.

```csharp
// Generic overload
DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)

// Non-generic overload
DbContextOptionsBuilder AddTrellisInterceptors(this DbContextOptionsBuilder optionsBuilder)

// Generic overload with TimeProvider (for testable EntityTimestampInterceptor)
DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(
    this DbContextOptionsBuilder<TContext> optionsBuilder,
    TimeProvider? timeProvider)

// Non-generic overload with TimeProvider
DbContextOptionsBuilder AddTrellisInterceptors(
    this DbContextOptionsBuilder optionsBuilder,
    TimeProvider? timeProvider)

// Usage
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString)
           .AddTrellisInterceptors());

// With custom TimeProvider for testing
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString)
           .AddTrellisInterceptors(fakeTimeProvider));
```

### ScalarValueQueryInterceptor

Automatically rewrites scalar value object expressions in LINQ so EF Core can translate them. Handles `.Value` property access, string methods (`StartsWith`, `Contains`, `EndsWith`), properties (`Length`), and comparisons — converting them to the provider type via the existing `implicit operator T(ScalarValueObject<TSelf, T>)`.

```csharp
// With interceptor registered, natural value object syntax works in LINQ:

// RequiredString — comparisons and string methods without .Value
context.Customers.Where(c => c.Name == "Alice")                       // → Name = 'Alice'
context.Customers.Where(c => c.Name.StartsWith("Al"))                 // → Name LIKE 'Al%'
context.Customers.Where(c => c.Name.Contains("lic"))                  // → Name LIKE '%lic%'
context.Customers.Where(c => c.Name.Length > 3)                       // → LEN(Name) > 3
context.Customers.OrderBy(c => c.Name)                                // → ORDER BY Name
context.Customers.OrderByDescending(c => c.Name)                      // → ORDER BY Name DESC

// All scalar value objects — comparisons without .Value
context.Orders.Where(o => o.DueDate < cutoffDate)                     // → DueDate < @cutoffDate

// Specifications with natural domain syntax:
public override Expression<Func<TodoItem, bool>> ToExpression() =>
    todo => todo.Status == TodoStatus.Active
         && todo.DueDate < _asOf;                                      // no .Value needed

// .Value still needed for:
// - Select projections to primitives: .Select(c => c.Name.Value)
// - Provider-type methods not exposed on the VO (e.g., string.Substring)
// See the EF Core integration guide for the full LINQ support matrix.
```

### TrellisPersistenceMappingException

Thrown by Trellis value converters when a persisted database value cannot be converted back to a value object (e.g., an invalid string in the database for an `EmailAddress` column). Provides diagnostic context for debugging data corruption.

```csharp
public sealed class TrellisPersistenceMappingException : InvalidOperationException
{
    public Type ValueObjectType { get; }     // e.g., typeof(EmailAddress)
    public object? PersistedValue { get; }   // the raw DB value that failed
    public string FactoryMethod { get; }     // e.g., "TryCreate"
    public string Detail { get; }            // validation failure detail
}
```

### Maybe\<T\> Query Interceptor

Automatically rewrites `Maybe<T>` property accesses in LINQ expression trees to EF Core-translatable storage member references. Enables natural LINQ syntax and `Specification<T>` patterns with `Maybe<T>` properties.

```csharp
// Registration — one call, singleton handled internally
optionsBuilder.UseSqlite(connectionString).AddTrellisInterceptors();

// With interceptor registered, these LINQ expressions work directly:
context.Customers.Where(c => c.Phone.HasValue)                                    // → IS NOT NULL
context.Customers.Where(c => c.Phone.HasNoValue)                                  // → IS NULL
context.Orders.Where(o => o.SubmittedAt.HasValue && o.SubmittedAt.Value < cutoff)  // → column IS NOT NULL AND column < @cutoff

// Specifications with Maybe<T> properties also work:
public override Expression<Func<Order, bool>> ToExpression() =>
    order => order.Status == OrderStatus.Submitted
          && order.SubmittedAt.HasValue
          && order.SubmittedAt.Value < _cutoff;
```

### Maybe\<T\> Index, Update, and Diagnostics Helpers

`HasTrellisIndex` resolves `Maybe<T>` properties to backing field names for type-safe index creation. `SetMaybeValue`/`SetMaybeNone` for bulk updates via `ExecuteUpdate`. `TRLS021` analyzer warns when `HasIndex` is used with `Maybe<T>` properties.

```csharp
// HasTrellisIndex — resolves Maybe<T> properties to mapped backing fields
IndexBuilder<TEntity> HasTrellisIndex<TEntity>(
    this EntityTypeBuilder<TEntity> entityTypeBuilder,
    Expression<Func<TEntity, object?>> propertySelector)

// Usage — single Maybe<T> property
builder.HasTrellisIndex(o => o.SubmittedAt);

// Usage — composite index mixing regular + Maybe<T> properties
builder.HasTrellisIndex(o => new { o.Status, o.SubmittedAt });
// Resolves to: HasIndex("Status", "_submittedAt") — type-safe, no string typos

// Notes
// - Accepts direct property access on the lambda parameter only
// - Rejects nested selectors such as e => e.Customer.Phone
// - Validates Maybe<T> backing fields exist on the CLR hierarchy or are already mapped
// - Supports inherited Maybe<T> backing fields declared on base entity types

// ExecuteUpdate helpers
UpdateSettersBuilder<TEntity> SetMaybeValue<TEntity, TInner>(
    this UpdateSettersBuilder<TEntity> updateSettersBuilder,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
    TInner value)

UpdateSettersBuilder<TEntity> SetMaybeNone<TEntity, TInner>(
    this UpdateSettersBuilder<TEntity> updateSettersBuilder,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// Note: SetMaybeValue/SetMaybeNone throw InvalidOperationException for composite
// owned types like Money. Use tracked entity updates (load, modify, SaveChangesAsync) instead.

// Diagnostics
IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this IModel model)
IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this DbContext dbContext)
string ToMaybeMappingDebugString(this IModel model)
string ToMaybeMappingDebugString(this DbContext dbContext)
```

`MaybePropertyMapping` describes the entity type, CLR property name, generated backing field, nullable store type, column name, and resolved provider type for each discovered `Maybe<T>` mapping.

### Exception Classification

How `SaveChangesResultAsync` classifies EF Core exceptions: `DbUpdateConcurrencyException` → `ConflictError`, duplicate key → `ConflictError`, FK violation → `DomainError`.

```csharp
bool DbExceptionClassifier.IsDuplicateKey(DbUpdateException ex)       // SQL Server, PostgreSQL, SQLite
bool DbExceptionClassifier.IsForeignKeyViolation(DbUpdateException ex)
string? DbExceptionClassifier.ExtractConstraintDetail(DbUpdateException ex)
```

---
