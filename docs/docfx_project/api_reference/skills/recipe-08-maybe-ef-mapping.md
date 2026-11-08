---
name: recipe-08-maybe-ef-mapping
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Maps Maybe and nullable value objects with EF Core, including date filters. Use when persisting optional value objects or querying Maybe of DateTime properties."
---
# Recipe 8 — EF Core: `MaybePropertyMapping` for nullable value objects

**Problem.** Persist a `Maybe<EmailAddress>` property with the EF Core `MaybeConvention`, then verify the generated mapping in a startup diagnostics check.

```csharp
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed partial class EmailAddress : RequiredString<EmailAddress>;

public sealed partial class Customer : Aggregate<CustomerId>
{
    public Customer(CustomerId id) : base(id) { }

    public partial Maybe<EmailAddress> Email { get; set; }   // TRLS035 if not 'partial'
}

// Configure
public sealed class AppDbContext : DbContext
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(AppDbContext).Assembly);
}

// Diagnostics — print the generated storage members for every Maybe<T> in the model
public static class ModelDiagnostics
{
    public static void DumpMaybeMappings(DbContext db)
    {
        IReadOnlyList<MaybePropertyMapping> mappings = db.GetMaybePropertyMappings();
        foreach (var m in mappings)
            Console.WriteLine($"{m.EntityTypeName}.{m.PropertyName} → {m.MappedBackingFieldName} ({m.StoreType.Name})");
    }
}
```

**What it shows.** `Maybe<T>` properties are routed through `MaybeConvention`, which generates a backing field (`_email` for `Email`) that EF Core maps to a nullable column. The CLR property remains `Maybe<EmailAddress>` everywhere in the domain. `MaybePropertyMapping` is the diagnostic record that exposes both names — useful for `HasIndex` on the storage member.

For storage-level inspection, the same record exposes `StorageKind`, `TableName`,
`Schema`, and `Columns.Items` (property paths, actual table/column names, provider column
types, and physical nullability). Composite mappings include recursively owned columns,
even when those values target different tables. `StorageReason` records Trellis's
separate-table fallback reason when known, not a guess about explicit overrides.
`db.ToMaybeMappingDebugString()` renders all of these diagnostics.

> For **composite** value objects (multi-field `[OwnedEntity]` types like `ShippingAddress`) — and for `Maybe<T>` where `T` is composite — see [Recipe 13](recipe-13-composite-value-object.md). `Recipe 8` covers scalar `Maybe<T>` only.

**Anti-pattern → fix (TRLS016).**

```csharp
// WRONG — HasIndex against the CLR Maybe<T> property silently fails
modelBuilder.Entity<Customer>().HasIndex(c => c.Email);   // TRLS016

// WRONG — explicit Property() configuration on a Maybe<T> CLR property.
// MaybeConvention generates a private backing field (e.g., _email) and maps THAT.
// Calling builder.Property(c => c.Email) tries to map Maybe<EmailAddress> as a column,
// which is not a supported store type — fails at model validation with
// "The property 'Customer.Email' could not be mapped because the database provider
//  does not support the type 'Maybe<EmailAddress>'."
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Email).IsRequired();          // ❌ — runtime error
        builder.Property(c => c.Email).HasMaxLength(254);     // ❌ — runtime error
    }
}

// FIX — say nothing about Maybe<T> in IEntityTypeConfiguration. The convention owns it.
// If you need column metadata (max length, column name, etc.), configure the *backing field*
// via the diagnostic name from MaybePropertyMapping, or use HasTrellisIndex for indexes.

// FIX 1 — strongly-typed Trellis index helper
modelBuilder.Entity<Customer>().HasTrellisIndex(c => new { c.Status, c.Email });

// FIX 2 — string-based HasIndex against the storage member
modelBuilder.Entity<Customer>().HasIndex("Status", "_email");
```

## Filtering on `Maybe<T>` properties in LINQ and `Specification<T>`

Once `MaybeConvention` maps the storage member, the **`MaybeQueryInterceptor`** (registered by `optionsBuilder.AddTrellisInterceptors()`) lets you write natural LINQ against the CLR `Maybe<T>` property — no `EF.Property<T?>(o, "_x")` boilerplate, no separate query helpers. The interceptor rewrites the expression tree before EF Core compiles it, translating `o.Maybe.HasValue`, `o.Maybe.Value`, `o.Maybe.GetValueOrDefault(d)`, `o.Maybe.HasValueWhere(t => ...)`, and `o.Maybe == Maybe<T>.None` to the storage-member access.

```csharp
// Specification — exactly the shape you'd write for an aggregate query.
public sealed class OverdueOrderSpecification(DateTime asOf) : Specification<Order>
{
    private readonly DateTime _threshold = asOf.AddDays(-7);

    // Natural multi-clause guard — analyzer-clean (TRLS003 recognises HasValue
    // anywhere in the connected `&&` subtree to the left of the `.Value` access),
    // safe in FakeRepository (HasValue short-circuits before Value), and translated
    // verbatim by MaybeQueryInterceptor in EF.
    public override Expression<Func<Order, bool>> ToExpression() =>
        o => o.Status == OrderStatus.Submitted
             && o.SubmittedAt.HasValue
             && o.SubmittedAt.Value < _threshold;
}

// Repository / DbContext usage — the spec composes through IQueryable.Where.
var overdue = await context.Orders
    .Where(new OverdueOrderSpecification(timeProvider.GetUtcNow().DateTime).ToExpression())
    .ToListAsync(ct);
```

**Why this works in both EF and `FakeRepository<T, TId>`** — the compiled `Func<Order, bool>` that `FakeRepository` evaluates in memory uses C#'s short-circuit `&&`, so `Value` is never read when `HasValue` is `false`. In EF, the interceptor rewrites both clauses to the mapped storage member and emits idiomatic SQL (`status = 'Submitted' AND submitted_at IS NOT NULL AND submitted_at < @threshold`). **One Specification, one predicate, identical semantics in production and in tests.**

**HasValueWhere shorthand.** The same parity holds when the two-clause guard is collapsed into `Maybe<T>.HasValueWhere(predicate)`. In-memory it expands to `HasValue && predicate(Value)` (predicate is never invoked on None); in EF the interceptor rewrites it to the same `IS NOT NULL AND ...` SQL. Use whichever form reads better — they are interchangeable.

```csharp
// Equivalent specification using HasValueWhere — same SQL, same FakeRepository semantics.
public override Expression<Func<Order, bool>> ToExpression() =>
    o => o.Status == OrderStatus.Submitted
         && o.SubmittedAt.HasValueWhere(t => t < _threshold);
```

**Sharing the spec with `FakeRepository`.** Pass the same `Specification<T>` instance through `QueryAsync` — never duplicate the predicate by hand in a fake adapter. Duplicating the predicate is the most expensive class of test bug to catch in code review (the fake passes while the real query silently returns the wrong rows).

```csharp
public Task<IReadOnlyList<Order>> FindOverdueAsync(DateTime asOf, CancellationToken ct) =>
    fake.QueryAsync(new OverdueOrderSpecification(asOf), ct);
```

> **Prerequisite.** The interceptor only runs when the `DbContext` is configured with `optionsBuilder.AddTrellisInterceptors()`. Without it, EF Core sees `Maybe<T>` as an unmapped CLR type and either drops the predicate silently or fails translation — while the `FakeRepository` tests continue to pass. This is the failure mode that creates "fake says yes, production says no". Always wire interceptors in `AddDbContext`.

```csharp
// Sentinel alternative — for predicates where you'd rather encode "absence acts as
// the most-permissive value" than carry the explicit HasValue clause. Reads as
// "if no SubmittedAt, treat as never overdue (DateTime.MaxValue)".
public override Expression<Func<Order, bool>> ToExpression() =>
    o => o.Status == OrderStatus.Submitted
         && o.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < _threshold;
```

For ad-hoc `IQueryable<T>` calls (outside a `Specification<T>`), the strongly-typed `IQueryable<T>` extensions in `MaybeQueryableExtensions` — `WhereHasValue` (with or without a typed predicate), `WhereNone`, `WhereEquals`, `OrderByMaybe`, etc. — target the storage member directly via `EF.Property`, without requiring the Maybe interceptor. The predicate overload accepts an expression tree, including a reusable `Expression<Func<TInner, bool>>`, not a compiled delegate. C# validates the chosen operators; the provider must still translate them, and translation failures never trigger client-side filtering. Scalar value-object `.Value` access still requires `AddTrellisInterceptors()`.

```csharp
// Equivalent ad-hoc query without a Specification (interceptor not required for this form):
var overdue = await context.Orders
    .Where(o => o.Status == OrderStatus.Submitted)
    .WhereHasValue(o => o.SubmittedAt, value => value < threshold)
    .ToListAsync(ct);
```
