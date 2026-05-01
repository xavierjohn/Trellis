---
title: Entity Framework Core Integration
package: Trellis.EntityFrameworkCore
topics: [efcore, repository, unit-of-work, savechanges, owned-entity, maybe, etag, conventions]
related_api_reference: [trellis-api-efcore.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Entity Framework Core Integration

`Trellis.EntityFrameworkCore` maps Trellis value objects, `Maybe<T>`, owned composites, and aggregate ETags into EF Core, and turns `SaveChangesAsync` exceptions into `Result<T>` failures so repositories stay on the railway.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Wire EF conventions for Trellis types (compile-time, AOT-friendly) | `configurationBuilder.ApplyTrellisConventionsFor<TContext>()` | [Conventions and interceptors](#conventions-and-interceptors) |
| Wire EF conventions via runtime assembly scan | `configurationBuilder.ApplyTrellisConventions(typeof(TContext).Assembly)` | [Conventions and interceptors](#conventions-and-interceptors) |
| Register `Maybe<T>` query rewriting, ETag, and timestamp interceptors | `optionsBuilder.AddTrellisInterceptors()` | [Conventions and interceptors](#conventions-and-interceptors) |
| Mark a composite value object as EF-owned | `[OwnedEntity]` on a `partial` class inheriting `ValueObject` | [Owned composites](#owned-composites) |
| Query for an optional row | `FirstOrDefaultMaybeAsync(predicate, ct)` | [Querying](#querying) |
| Query for a required row, fail with a typed error | `FirstOrDefaultResultAsync(predicate, error, ct)` | [Querying](#querying) |
| Filter / order an `IQueryable<T>` by a `Maybe<TInner>` property | `WhereHasValue` / `WhereEquals` / `OrderByMaybe` (and friends) | [Querying Maybe properties](#querying-maybe-properties) |
| Save and surface DB failures as `Error` (no UoW) | `db.SaveChangesResultAsync(ct)` / `db.SaveChangesResultUnitAsync(ct)` | [Saving](#saving) |
| Stage aggregate changes; let the pipeline commit | `RepositoryBase<TAggregate, TId>` + `AddTrellisUnitOfWork<TContext>()` | [Repositories and unit of work](#repositories-and-unit-of-work) |
| Commit staged changes outside the pipeline | `IUnitOfWork.CommitAsync(ct)` | [Repositories and unit of work](#repositories-and-unit-of-work) |
| Update a `Maybe<T>` scalar via `ExecuteUpdate` | `SetMaybeValue(...)` / `SetMaybeNone(...)` | [Bulk updates over Maybe](#bulk-updates-over-maybe) |
| Index a `Maybe<T>` property without TRLS016 | `entityTypeBuilder.HasTrellisIndex(x => x.M)` | [Indexing Maybe properties](#indexing-maybe-properties) |

## Use this guide when

- You persist Trellis aggregates with EF Core and want value-object mapping, ETag concurrency, and timestamp interceptors wired by convention.
- You want repository methods that return `Maybe<T>` / `Result<T>` instead of `null` and exceptions.
- You need a deterministic commit boundary — repositories stage, the mediator pipeline commits.

## Surface at a glance

`Trellis.EntityFrameworkCore` exposes one configuration entry point, one interceptor entry point, a query/save extension surface, an aggregate repository base, and a unit-of-work abstraction.

| Type / member | Kind | Purpose |
|---|---|---|
| `ModelConfigurationBuilderExtensions.ApplyTrellisConventions(params Assembly[])` | Conventions | Runtime scan; registers scalar converters, `Maybe<T>` / composite / `Money` / ETag / transient conventions. |
| `GeneratedTrellisConventions.ApplyTrellisConventionsFor<TContext>()` | Conventions (source-generated) | Compile-time discovery alternative; no reflection. |
| `DbContextOptionsBuilderExtensions.AddTrellisInterceptors([TimeProvider])` | Interceptors | Singleton `MaybeQueryInterceptor`, `ScalarValueQueryInterceptor`, `AggregateETagInterceptor`, `EntityTimestampInterceptor`. |
| `OwnedEntityAttribute` | Attribute | Marks a `partial ValueObject` as EF-owned; generator emits the private parameterless constructor. |
| `QueryableExtensions.FirstOrDefaultMaybeAsync<T>` / `SingleOrDefaultMaybeAsync<T>` | Query | Returns `Task<Maybe<T>>`; absence → `Maybe<T>.None`. |
| `QueryableExtensions.FirstOrDefaultResultAsync<T>` | Query | Returns `Task<Result<T>>`; absence → **the exact `Error` you supplied** (does not invent one). |
| `QueryableExtensions.Where(Specification<T>)` | Query | Applies a Trellis specification expression. |
| `MaybeQueryableExtensions.WhereHasValue` / `WhereNone` / `WhereEquals` / `WhereLessThan` / `WhereLessThanOrEqual` / `WhereGreaterThan` / `WhereGreaterThanOrEqual` / `OrderByMaybe` / `OrderByMaybeDescending` / `ThenByMaybe` / `ThenByMaybeDescending` | Query | Translate `Maybe<TInner>` predicates and ordering to the mapped storage member. |
| `MaybeUpdateExtensions.SetMaybeValue<T>` / `SetMaybeNone<T>` | Bulk update | `ExecuteUpdate` setters for scalar `Maybe<T>` properties. |
| `MaybeEntityTypeBuilderExtensions.HasTrellisIndex<T>` | Model builder | Indexes a `Maybe<T>` property by resolving to its storage member (avoids TRLS016). |
| `DbContextExtensions.SaveChangesResultAsync(...)` | Save | `Task<Result<int>>`. Maps `DbUpdateConcurrencyException` / duplicate-key / FK violations to `Error.Conflict`. |
| `DbContextExtensions.SaveChangesResultUnitAsync(...)` | Save | `Task<Result<Unit>>` overload when row count is not needed. |
| `RepositoryBase<TAggregate, TId>` | Aggregate repo base | `FindByIdAsync` (Maybe), `QueryAsync(spec)`, `ExistsAsync`, `CountAsync`, `Add`, `Remove`, `RemoveByIdAsync` (`Task<Result<Unit>>`). Staging only — never calls `SaveChanges`. |
| `IUnitOfWork.CommitAsync(ct)` | Commit boundary | `Task<Result<Unit>>`. Implemented by `EfUnitOfWork<TContext>`. |
| `TransactionalCommandBehavior<TMessage, TResponse>` | Pipeline behavior | Auto-commits after a successful `ICommand<TResponse>` handler; only fires for commands. |
| `UnitOfWorkServiceCollectionExtensions.AddTrellisUnitOfWork<TContext>()` / `AddTrellisUnitOfWorkWithoutBehavior<TContext>()` | DI | Registers `EfUnitOfWork<TContext>` (scoped) and inserts the commit behavior innermost. |
| `DbExceptionClassifier.IsDuplicateKey` / `IsForeignKeyViolation` / `ExtractConstraintDetail` | Diagnostics | Cross-provider DB exception classification used by the save helpers. |
| `MaybeModelExtensions.GetMaybePropertyMappings()` / `ToMaybeMappingDebugString()` | Diagnostics | Inspect resolved `Maybe<T>` storage members. |
| `TrellisPersistenceMappingException` | Exception | Thrown when a persisted scalar value object value fails materialization. |

Full signatures: [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md).

## Installation

```bash
dotnet add package Trellis.EntityFrameworkCore
```

The package bundles `Trellis.EntityFrameworkCore.Generator.dll` under `analyzers/dotnet/cs/`. Installing the package is enough to attach the `[OwnedEntity]` and `ApplyTrellisConventionsFor<TContext>` source generators — there is no separate generator NuGet package.

## Quick start

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

namespace MyApp.Data;

public sealed class CustomerId : RequiredGuid<CustomerId>;
public sealed class CustomerName : RequiredString<CustomerName>;

public sealed class Customer : Aggregate<CustomerId>
{
    public CustomerName Name { get; private set; } = null!;
    public EmailAddress Email { get; private set; } = null!;

    private Customer() : base(CustomerId.NewUniqueV7()) { }

    public static Customer Create(CustomerName name, EmailAddress email) =>
        new() { Name = name, Email = email };
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(100);
            builder.Property(x => x.Email).HasMaxLength(254);
        });
    }
}

// Composition root
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString)
        .AddTrellisInterceptors());

services.AddTrellisBehaviors();
services.AddTrellisUnitOfWork<AppDbContext>();
```

`ApplyTrellisConventionsFor<TContext>()` configures the model. `AddTrellisInterceptors()` configures runtime save/query behavior. `AddTrellisUnitOfWork<TContext>()` registers `IUnitOfWork` and the auto-commit pipeline behavior. You usually want all three.

## Conventions and interceptors

Two configuration entry points and one interceptor registration cover model setup.

| Member | When to use |
|---|---|
| `ApplyTrellisConventionsFor<TContext>()` | Default. Source-generated, compile-time discovery. Walks reachable types from `TContext.DbSet<T>` properties. No reflection, no `MakeGenericType`. |
| `ApplyTrellisConventions(params Assembly[])` | Fallback when the `DbContext` is not in the current compilation, or when you need to pass extra assemblies for composite value object discovery. Always includes `Trellis.Primitives`. |
| `AddTrellisInterceptors([TimeProvider])` | Always. Registers the four singleton interceptors. The `TimeProvider` overload constructs a fresh `EntityTimestampInterceptor(timeProvider)`. |

Both convention paths register the same set: scalar converters, `MaybeConvention`, `CompositeValueObjectConvention`, `MoneyConvention`, `AggregateETagConvention`, `AggregateTransientPropertyConvention`, `ValueObjectMappingGuardConvention`.

> [!WARNING]
> `ApplyTrellisConventionsFor<TContext>()` only discovers `DbContext` types defined in the current compilation. Calling it for a context excluded from generation throws `InvalidOperationException`. Use the reflection-based overload in that case.

> [!WARNING]
> `ApplyTrellisConventions(...)` only discovers composite value objects in the assemblies you pass in (plus `Trellis.Primitives`). If a composite type lives in another assembly, include it.

## Owned composites

Composite value objects use **owned-type** mapping, not scalar conversion. Mark them with `[OwnedEntity]` so the generator emits the private parameterless constructor EF Core needs for materialization.

```csharp
using Trellis;
using Trellis.EntityFrameworkCore;

namespace MyApp.Domain;

[OwnedEntity]
public partial class Address : ValueObject
{
    public string Street { get; private set; }
    public string City   { get; private set; }
    public string State  { get; private set; }

    public Address(string street, string city, string state)
    {
        Street = street;
        City   = city;
        State  = state;
    }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
    }
}
```

Rules enforced by analyzers and conventions:

- The class must be `partial` and inherit `ValueObject`.
- Use `{ get; private set; }` — `{ get; init; }` is flagged by `TRLS022` (round-trip not guaranteed).
- A `Maybe<T>` over an owned composite stays inline when EF Core can model it safely; when the owned value contains nested owned types or non-nullable value-type members, Trellis switches to a separate owned table to avoid invalid nullable inline mapping.

## Querying

Choose the return type by what absence means.

| Method | Returns | Use when |
|---|---|---|
| `FirstOrDefaultMaybeAsync(predicate, ct)` | `Task<Maybe<T>>` | Absence is data; the caller decides what it means. |
| `SingleOrDefaultMaybeAsync(predicate, ct)` | `Task<Maybe<T>>` | Same, but throws if more than one matches. |
| `FirstOrDefaultResultAsync(predicate, notFoundError, ct)` | `Task<Result<T>>` | The repository owns the failure; pass the exact `Error` to return. |
| `Where(Specification<T>)` | `IQueryable<T>` | Compose a reusable Trellis specification into a query. |

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed class CustomerRepository(AppDbContext db)
{
    public Task<Maybe<Customer>> GetByEmailAsync(EmailAddress email, CancellationToken ct) =>
        db.Customers.FirstOrDefaultMaybeAsync(x => x.Email == email, ct);

    public Task<Result<Customer>> GetRequiredAsync(CustomerId id, CancellationToken ct) =>
        db.Customers.FirstOrDefaultResultAsync(
            x => x.Id == id,
            new Error.NotFound(ResourceRef.For<Customer>(id)) { Detail = $"Customer {id} was not found." },
            ct);
}
```

> [!IMPORTANT]
> `FirstOrDefaultResultAsync(...)` returns **the exact `Error` you pass in**. It does not synthesize an `Error.NotFound`.

### Querying Maybe properties

Prefer the `MaybeQueryableExtensions` helpers over raw `GetValueOrDefault(...)` expressions — they translate to the mapped storage member directly and side-step `TRLS013`.

| Helper | SQL semantics |
|---|---|
| `WhereHasValue(x => x.M)` | `WHERE storage IS NOT NULL` |
| `WhereNone(x => x.M)` | `WHERE storage IS NULL` |
| `WhereEquals(x => x.M, value)` | `WHERE storage = value` |
| `WhereLessThan` / `WhereLessThanOrEqual` / `WhereGreaterThan` / `WhereGreaterThanOrEqual` | Comparison against `value` (requires `IComparable<TInner>`). |
| `OrderByMaybe` / `OrderByMaybeDescending` / `ThenByMaybe` / `ThenByMaybeDescending` | Order by the mapped storage member. |

```csharp
using Trellis.EntityFrameworkCore;

var dueSoon = await db.Tasks
    .WhereHasValue(t => t.DueDate)
    .WhereLessThanOrEqual(t => t.DueDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)))
    .OrderByMaybe(t => t.DueDate)
    .ToListAsync(ct);
```

For projections that unwrap `Maybe<T>`, filter with `WhereHasValue` (or `.Where(x => x.M.HasValue)` — `TRLS013` recognises that exact prior shape) **before** the projection.

The `MaybeQueryInterceptor` (registered by `AddTrellisInterceptors()`) also rewrites natural patterns inside `Where` / `Select` / `Specification.ToExpression()`: `o.X.HasValue`, `o.X.HasNoValue`, `o.X.Value`, `o.X.GetValueOrDefault(d)`, `o.X == Maybe<T>.None`. The helpers above are still preferred when they exist.

### Indexing Maybe properties

EF Core's `HasIndex(x => x.M)` cannot resolve a `Maybe<T>` selector to its storage member, so it triggers `TRLS016`. Use the Trellis helper:

```csharp
modelBuilder.Entity<Order>().HasTrellisIndex(x => x.PromisedDate);
modelBuilder.Entity<Order>().HasTrellisIndex(x => new { x.CustomerId, x.PromisedDate });
```

## Saving

Two save helpers, distinguished only by the success payload.

| Helper | Returns | Use when |
|---|---|---|
| `SaveChangesResultAsync(ct)` and `SaveChangesResultAsync(acceptAllChangesOnSuccess, ct)` | `Task<Result<int>>` | You need the affected row count. |
| `SaveChangesResultUnitAsync(ct)` and `SaveChangesResultUnitAsync(acceptAllChangesOnSuccess, ct)` | `Task<Result<Unit>>` | Success/failure is enough. |

Failure mapping (identical for both):

| EF Core failure | Trellis result |
|---|---|
| `DbUpdateConcurrencyException` | `new Error.Conflict(null, "concurrency.modified")` |
| Duplicate-key `DbUpdateException` | `new Error.Conflict(null, "duplicate.key")` |
| Foreign-key `DbUpdateException` | `new Error.Conflict(null, "referential.integrity")` |

Connection failures, timeouts, and `OperationCanceledException` are **not** caught — they propagate.

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed class CustomerRepository(AppDbContext db)
{
    public async Task<Result<Unit>> AddAsync(Customer customer, CancellationToken ct)
    {
        db.Customers.Add(customer);
        return await db.SaveChangesResultUnitAsync(ct);
    }
}
```

> [!NOTE]
> Analyzer `TRLS015` flags direct `SaveChangesAsync` calls in non-UoW contexts; use the result-returning helpers instead. In a UoW context (see below), repositories should not call save helpers at all — let the pipeline commit.

### Bulk updates over Maybe

For `ExecuteUpdate` over scalar `Maybe<T>` properties, use the dedicated setters; they map to the storage member directly. Composite owned `Maybe<T>` is not supported and will throw.

```csharp
using Trellis.EntityFrameworkCore;

await db.Tasks
    .Where(t => t.Status == "open")
    .ExecuteUpdateAsync(s => s
        .SetMaybeValue(t => t.SnoozedUntil, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
        .SetMaybeNone(t => t.AssignedTo),
        ct);
```

## Repositories and unit of work

The preferred pattern for command-driven applications: repositories stage changes, the mediator pipeline commits.

`RepositoryBase<TAggregate, TId>` provides:

| Member | Returns | Notes |
|---|---|---|
| `FindByIdAsync(id, ct)` | `Task<Maybe<TAggregate>>` | Tracked. Override `BuildFindByIdQuery()` to add `.Include(...)`. |
| `QueryAsync(spec, ct)` | `Task<IReadOnlyList<TAggregate>>` | No-tracking by default via `BuildQueryBase()`. |
| `ExistsAsync(id, ct)` / `ExistsAsync(spec, ct)` | `Task<bool>` | Lightweight existence check; respects `BuildQueryBase()` filters. |
| `CountAsync(spec, ct)` | `Task<int>` | Counts matches. |
| `Add(aggregate)` | `void` | Stages insert. No-op if already tracked. |
| `Remove(aggregate)` | `void` | Stages delete. |
| `RemoveByIdAsync(id, ct)` | `Task<Result<Unit>>` | `DbSet.FindAsync` → stage delete; missing row → `Error.NotFound`. Respects EF 8 global query filters. |

`RepositoryBase` **never** calls `SaveChanges`. Commit is owned by `IUnitOfWork.CommitAsync(ct)`, which returns `Task<Result<Unit>>` and is implemented by `EfUnitOfWork<TContext>` over `SaveChangesResultUnitAsync`.

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed class OrderRepository(AppDbContext db) : RepositoryBase<Order, OrderId>(db)
{
    protected override IQueryable<Order> BuildFindByIdQuery() =>
        DbSet.Include(o => o.LineItems);
}

public sealed class ShipOrderHandler(OrderRepository orders) : ICommandHandler<ShipOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(ShipOrderCommand cmd, CancellationToken ct) =>
        (await orders.FindByIdAsync(cmd.OrderId, ct))
            .ToResult(new Error.NotFound(ResourceRef.For<Order>(cmd.OrderId)) { Detail = "Order not found." })
            .Bind(order => order.Ship());
    // No SaveChanges call. TransactionalCommandBehavior commits on success.
}
```

DI registration:

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString)
        .AddTrellisInterceptors());

services.AddTrellisBehaviors();           // outer behaviors first
services.AddTrellisUnitOfWork<AppDbContext>(); // commit behavior goes innermost
```

> [!IMPORTANT]
> Call `AddTrellisUnitOfWork<TContext>()` **after** `AddTrellisBehaviors()`. The transactional behavior is inserted after the last `IPipelineBehavior<,>` registration so it runs innermost — closest to the handler — keeping commit failures visible to logging, tracing, and exception behaviors.

`TransactionalCommandBehavior<TMessage, TResponse>` only fires for `ICommand<TResponse>` (queries are skipped at the type-constraint level). On handler success it calls `unitOfWork.CommitAsync(ct)`; if commit fails, it returns `TResponse.CreateFailure(error)`. EF Core's implicit transaction around `SaveChanges` makes the staged changes commit atomically.

For background jobs or non-mediator code, inject `IUnitOfWork` directly and call `CommitAsync`. Use `AddTrellisUnitOfWorkWithoutBehavior<TContext>()` to skip the pipeline behavior registration.

## Optimistic concurrency

Once `ApplyTrellisConventions*` and `AddTrellisInterceptors()` are wired:

- the aggregate `ETag` is configured as a concurrency token,
- a new `ETag` is generated on **Added** and **Modified** aggregates,
- aggregate roots are also promoted when loaded dependents change, so concurrency works at the aggregate boundary.

A losing writer surfaces as `DbUpdateConcurrencyException` → `new Error.Conflict(null, "concurrency.modified")` from `SaveChangesResult*Async` (and therefore from `IUnitOfWork.CommitAsync` / the transactional pipeline behavior). You do **not** configure `AggregateETagConvention` or `AggregateETagInterceptor` directly — they are internal types reached through the supported public entry points.

## Composition

Once the read returns `Maybe<T>` or `Result<T>`, it composes with the rest of Trellis:

```csharp
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed class ShipOrderHandler(OrderRepository orders) : ICommandHandler<ShipOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(ShipOrderCommand cmd, CancellationToken ct) =>
        (await orders.FindByIdAsync(cmd.OrderId, ct))
            .ToResult(new Error.NotFound(ResourceRef.For<Order>(cmd.OrderId)) { Detail = "Order not found." })
            .Ensure(order => !order.IsCancelled,
                new Error.Conflict(ResourceRef.For<Order>(cmd.OrderId), "order.cancelled"))
            .Bind(order => order.Ship());
    // TransactionalCommandBehavior commits on Ok.
}
```

For non-pipeline scenarios, the equivalent is `await uow.CommitAsync(ct)` after the domain `Bind` chain returns `Ok`.

## Practical guidance

- **Pick the return type by intent.** `Maybe<T>` when absence is data; `Result<T>` when the repository owns the failure; `Result<Unit>` for commands; `bool` for existence checks.
- **Stage in repositories, commit in the pipeline.** Do not call `SaveChangesResult*Async` from inside a repository when `AddTrellisUnitOfWork<TContext>()` is registered — that double-commits or hides commit failures from outer behaviors.
- **Prefer `ApplyTrellisConventionsFor<TContext>()`.** Compile-time discovery, no reflection, no `MakeGenericType`. Fall back to the assembly-scan overload only when the `DbContext` lives in another compilation.
- **Use the `Maybe` query helpers, not raw `GetValueOrDefault(...)` expressions.** They translate to the mapped storage member directly and avoid `TRLS013` / `TRLS016`.
- **`[OwnedEntity]` classes are `partial` with `{ get; private set; }`.** `{ get; init; }` is flagged by `TRLS022`.
- **Pass `CancellationToken` everywhere.** Every helper, repository method, and `CommitAsync` accepts one.

## Cross-references

- API surface: [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md)
- `Result<T>`, `Maybe<T>`, `Error`, `Aggregate<TId>`, `Specification<T>`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Recipes (specifications with `Maybe<T>`, repository patterns, UoW): [`trellis-api-cookbook.md`](../api_reference/trellis-api-cookbook.md)
- Mediator pipeline behaviors and registration order: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- Analyzer rules referenced here (`TRLS013`, `TRLS015`, `TRLS016`, `TRLS022`): [`trellis-api-analyzers.md`](../api_reference/trellis-api-analyzers.md)
