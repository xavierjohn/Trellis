---
title: Entity Framework Core Integration
package: Trellis.EntityFrameworkCore
topics: [efcore, repository, unit-of-work, savechanges, owned-entity, maybe, etag, conventions]
related_api_reference: [trellis-api-efcore.md, trellis-api-core.md]
last_verified: 2026-05-31
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
| Retry an INSERT after a system-generated unique-key collision | `db.SaveChangesWithRetryAsync(shouldRetry, regenerate)` | [Retrying on system-generated unique-key collisions](#retrying-on-system-generated-unique-key-collisions) |
| Insert a row idempotently on a unique constraint (worker outboxes, "save unless exists" deduplication, redelivered events) | `db.TryInsertUniqueAsync(entity, ct)` | [Idempotent inserts on a unique constraint](#idempotent-inserts-on-a-unique-constraint) |
| Stage aggregate changes; let the pipeline commit | `RepositoryBase<TAggregate, TId>` + `AddTrellisUnitOfWork<TContext>()` | [Repositories and unit of work](#repositories-and-unit-of-work) |
| Commit staged changes outside the pipeline | `IUnitOfWork.CommitAsync(ct)` | [Repositories and unit of work](#repositories-and-unit-of-work) |
| Update a `Maybe<T>` scalar via `ExecuteUpdate` | `SetMaybeValue(...)` / `SetMaybeNone(...)` | [Bulk updates over Maybe](#bulk-updates-over-maybe) |
| Index a `Maybe<T>` property without TRLS016 | `entityTypeBuilder.HasTrellisIndex(x => x.M)` | [Indexing Maybe properties](#indexing-maybe-properties) |

## Use this guide when

- You persist Trellis aggregates with EF Core and want value-object mapping, ETag concurrency, and timestamp interceptors wired by convention.
- You want repository methods that return `Maybe<T>` / `Result<T>` instead of `null` and exceptions.
- You need a deterministic commit boundary — repositories stage, the mediator pipeline commits.

## Persistence-readiness checklist

Moving an aggregate from in-memory to EF Core surfaces a few constraints that don't exist in memory and aren't obvious until the mapping step. Check these before persisting a new aggregate:

- **`Maybe<T>` properties must be `partial` on a `partial` type.** A non-`partial` `Maybe<T>` property raises `TRLS035`; the declaring type must also be `partial` so the convention generator can emit the storage member (a `partial` property inside a non-`partial` type is a C# compile error). See [Querying Maybe properties](#querying-maybe-properties).
- **Model single-column value objects as scalars, not composites.** A *composite* `ValueObject` (a multi-member VO) in the scanned assembly is auto-mapped as an owned composite, so a composite VO that exposes *derived* members (a computed `Segments` collection, a `Parent`) makes EF try to map a collection. Model single-column values as a scalar `Required*<TSelf>` (mapped via a value converter, not owned) and expose derived data as **methods**, not mapped properties. See [Owned composites](#owned-composites).
- **Owned composites need a parameterless constructor.** Mark the `partial ValueObject` with `[OwnedEntity]` so the generator emits the private parameterless constructor EF requires; a hand-written owned type must declare one itself. See [Owned composites](#owned-composites).
- **Register the conventions and interceptors.** `ApplyTrellisConventions(...)` (or the source-generated `ApplyTrellisConventionsFor<TContext>()`) plus `AddTrellisInterceptors()` wire scalar / `Maybe<T>` / composite / ETag mapping. See [Conventions and interceptors](#conventions-and-interceptors).
- **Owned-collection children with a domain-assigned key are handled for you.** When an `OwnsMany` child declares its own primary key (for example `Guid Id` or `long SequenceNumber`), `ApplyTrellisConventions(...)` marks it `ValueGenerated.Never`, so the application-assigned value persists on every provider — no manual `ValueGeneratedNever()`, no SQL Server `IDENTITY` insert error (`544`), and no spurious 409 when a child is added to an already-loaded parent. A *keyless* owned type (EF Core synthesizes the surrogate key) still follows the provider rules for store-generated keys. See [Owned collections with a domain-assigned key](../api_reference/trellis-api-efcore.md#owned-collections-with-a-domain-assigned-key).

## Surface at a glance

`Trellis.EntityFrameworkCore` exposes one configuration entry point, one interceptor entry point, a query/save extension surface, an aggregate repository base, and a unit-of-work abstraction.

| Type / member | Kind | Purpose |
|---|---|---|
| `ModelConfigurationBuilderExtensions.ApplyTrellisConventions(params Assembly[])` | Conventions | Runtime scan; registers scalar converters, `Maybe<T>` / composite / `Money` / ETag / transient conventions. Always includes `Trellis.Core` (base `Required*` types), `Trellis.Primitives`, and `Trellis.Authorization` in the scan so built-in primitives (`EmailAddress`, `Url`, …) and `ActorId` work without an explicit hand-in. |
| `GeneratedTrellisConventions.ApplyTrellisConventionsFor<TContext>()` | Conventions (source-generated) | Compile-time discovery alternative; no reflection. |
| `DbContextOptionsBuilderExtensions.AddTrellisInterceptors([TimeProvider])` | Interceptors | Singleton `MaybeQueryInterceptor`, `ScalarValueQueryInterceptor`, `AggregateETagInterceptor`, `EntityTimestampInterceptor`. |
| `OwnedEntityAttribute` | Attribute | Marks a `partial ValueObject` as EF-owned; generator emits the private parameterless constructor. |
| `QueryableExtensions.FirstOrDefaultMaybeAsync<T>` / `SingleOrDefaultMaybeAsync<T>` | Query | Returns `Task<Maybe<T>>`; absence → `Maybe<T>.None`. |
| `QueryableExtensions.FirstOrDefaultResultAsync<T>` | Query | Returns `Task<Result<T>>`; absence → **the exact `Error` you supplied** (does not invent one). |
| `QueryableExtensions.Where(Specification<T>)` | Query | Applies a Trellis specification expression. |
| `MaybeQueryableExtensions.WhereHasValue` / `WhereNone` / `WhereEquals` / `WhereLessThan` / `WhereLessThanOrEqual` / `WhereGreaterThan` / `WhereGreaterThanOrEqual` / `OrderByMaybe` / `OrderByMaybeDescending` / `ThenByMaybe` / `ThenByMaybeDescending` | Query | Translate `Maybe<TInner>` predicates and ordering to the mapped storage member. |
| `MaybeUpdateExtensions.SetMaybeValue<T>` / `SetMaybeNone<T>` | Bulk update | `ExecuteUpdate` setters for scalar `Maybe<T>` properties. |
| `MaybeEntityTypeBuilderExtensions.HasTrellisIndex<T>` | Model builder | Indexes a `Maybe<T>` property by resolving to its storage member (avoids TRLS016). |
| `DbContextExtensions.SaveChangesResultAsync(...)` | Save | `Task<Result<int>>`. Maps `DbUpdateConcurrencyException` / duplicate-key / FK violations to `Error.Conflict` (`duplicate.key` / `referential.integrity` carry `ConstraintName` / `ConstraintTableName` telemetry from `DbExceptionClassifier.ExtractConstraintIdentity`). |
| `DbContextExtensions.SaveChangesResultUnitAsync(...)` | Save | `Task<Result<Unit>>` overload when row count is not needed. |
| `DbContextIdempotencyExtensions.TryInsertUniqueAsync<TEntity>(entity, ct)` | Save | `Task<Result<TEntity>>`. Adds `entity`, persists, and converts a unique-constraint violation into an `Error.Conflict` with reason code `"duplicate.key"` carrying `ConstraintName` / `ConstraintTableName`. FK / concurrency / cancellation / connection exceptions propagate. Requires a clean change tracker on entry. |
| `RepositoryBase<TAggregate, TId>` | Aggregate repo base | `FindByIdAsync` (Maybe), `QueryAsync(spec)`, `ExistsAsync`, `CountAsync`, `Add`, `Remove`, `RemoveByIdAsync` (`Task<Result<Unit>>`). Staging only — never calls `SaveChanges`. |
| `IUnitOfWork.CommitAsync(ct)` | Commit boundary | `Task<Result<Unit>>`. Implemented by `EfUnitOfWork<TContext>`. Scope-aware via `BeginScope()`: only the outermost scope's commit persists. |
| `TransactionalCommandBehavior<TMessage, TResponse>` | Pipeline behavior | Auto-commits after a successful `ICommand<TResponse>` handler; only fires for commands. Wraps each command in `using var scope = unitOfWork.BeginScope();` so a nested command (dispatched via `IMediator` from another handler) defers its commit until the outermost handler returns. |
| `UnitOfWorkServiceCollectionExtensions.AddTrellisUnitOfWork<TContext>()` / `AddTrellisUnitOfWorkWithoutBehavior<TContext>()` | DI | Registers `EfUnitOfWork<TContext>` (scoped) and inserts the commit behavior innermost. |
| `DbExceptionClassifier.IsDuplicateKey` / `IsForeignKeyViolation` / `ExtractConstraintDetail` / `ExtractConstraintIdentity` | Diagnostics | Cross-provider DB exception classification used by the save helpers. Recognizes SQL Server, PostgreSQL, SQLite, and MySQL/MariaDB (works with both `MySql.Data.MySqlClient` and `MySqlConnector`); provider exception types are detected by name so consumers don't take a transitive driver dependency. `ExtractConstraintIdentity(ex)` returns `(ConstraintName, TableName)` for structured logs and is the primitive `SaveChangesResultAsync` / `SaveChangesWithRetryAsync` / `TryInsertUniqueAsync` use to populate the new `Error.Conflict` telemetry fields. |
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
| `ApplyTrellisConventions(params Assembly[])` | Fallback when the `DbContext` is not in the current compilation, or when you need to pass extra assemblies for composite value object discovery. Always includes `Trellis.Core` (base `Required*` types), `Trellis.Primitives` (built-in primitives), and `Trellis.Authorization` (`ActorId` for `CreatedByActorId` audit fields). |
| `AddTrellisInterceptors([TimeProvider])` | Always. Registers the four singleton interceptors. The `TimeProvider` overload constructs a fresh `EntityTimestampInterceptor(timeProvider)`. |

Both convention paths register the same set: scalar converters, `MaybeConvention`, `CompositeValueObjectConvention`, `MoneyConvention`, `AggregateETagConvention`, `AggregateTransientPropertyConvention`, `ValueObjectMappingGuardConvention`.

> [!WARNING]
> `ApplyTrellisConventionsFor<TContext>()` only discovers `DbContext` types defined in the current compilation. Calling it for a context excluded from generation throws `InvalidOperationException`. Use the reflection-based overload in that case.

> [!WARNING]
> `ApplyTrellisConventions(...)` only discovers composite value objects in the assemblies you pass in (plus `Trellis.Core`, `Trellis.Primitives`, and `Trellis.Authorization`). If a composite type lives in another assembly, include it.

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

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City);
        components.Add(State);
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

The `MaybeQueryInterceptor` (registered by `AddTrellisInterceptors()`) also rewrites natural patterns inside `Where` / `Select` / `Specification.ToExpression()`: `o.X.HasValue`, `o.X.HasNoValue`, `o.X.Value`, `o.X.GetValueOrDefault(d)`, `o.X.HasValueWhere(t => ...)` (inline expression-bodied lambdas only — captured `Func<T,bool>` variables and method-group conversions are not translatable), `o.X == Maybe<T>.None`. The helpers above are still preferred when they exist.

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
| `DbUpdateConcurrencyException` | `new Error.Conflict(null, "concurrent-modification")` |
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

### Retrying on system-generated unique-key collisions

`SaveChangesWithRetryAsync` is for one specific shape: **INSERT a new entity whose natural key is generated by your code** (a short code, slug, license-plate code, public-handle token, etc.) where the cheapest path is to try, and if the random key happens to collide, regenerate and try again. It is **not** a general retry primitive — it only handles `Added` entries, and it relies on you supplying both a classifier and a regenerator.

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore;

public sealed class LinkRepository(AppDbContext db)
{
    public async Task<Result<Unit>> CreateAsync(Link link, CancellationToken ct)
    {
        db.Links.Add(link);
        return await db.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (entries, attempt, _) =>
            {
                foreach (var entry in entries)
                    if (entry.Entity is Link l)
                        l.ShortCode = ShortCode.Generate();
                return ValueTask.FromResult(true);
            },
            maxAttempts: 3,
            cancellationToken: ct);
    }
}
```

Footguns the API enforces or surfaces:

- **Only `Added` entries are retryable.** If `DbUpdateException.Entries` contains a `Modified` entry the method throws `InvalidOperationException` rather than attempt the fragile detach/re-`Modified` cycle (which would lose original values, per-property `IsModified` flags, and temporary-value metadata). Use a different strategy — typically `ExecuteUpdate` with a `WHERE` over the rowversion, or a load-modify-save cycle inside a tighter transaction — for update collisions.
- **Concurrency exceptions bypass `shouldRetry`.** `DbUpdateConcurrencyException` is mapped immediately to `new Error.Conflict(null, "concurrent-modification")`; regenerating a natural key cannot resolve a stale rowversion.
- **Make the classifier narrow.** Returning `true` for every `DbUpdateException` will retry on FK violations, NOT-NULL violations, and check-constraint failures — which `regenerate` cannot fix. Prefer `DbExceptionClassifier.IsDuplicateKey` or a constraint-name predicate.
- **Detach scope is precisely `ex.Entries`.** Sibling aggregates pending in the change tracker (outbox rows staged by a domain-event handler; an unrelated `Added` entity from the same logical operation; entities promoted via `db.Entry(x).State = Added` rather than `DbSet.Add`) are **not** detached. They will be saved together with the regenerated entries on the next attempt.
- **The `regenerate` callback returns a `ValueTask<bool>`.** Return `true` to continue; return `false` to abort the retry loop. Aborting leaves the conflicting entries detached (sibling aggregates remain tracked) and returns `Error.Conflict` with reason code `"retry.aborted"`.
- **`attempt` is 1-based and equals the regenerate-call number.** With `maxAttempts: 3` the call sequence on consecutive failures is: save → regenerate(attempt: 1) → save → regenerate(attempt: 2) → save → return `Error.Conflict` with reason code `"retry.exhausted"`. The final attempt does **not** call regenerate.
- **Detach is conditional on attempting a retry.** When `shouldRetry` returns false, OR when `maxAttempts` is exhausted, no detach happens — the change tracker is left untouched and the caller decides what to do.
- **Callback exception semantics.** A `shouldRetry` exception propagates with no detach having occurred. A `regenerate` exception propagates with the conflicting entries already detached.
- **Explicit transactions vary by provider.** SQLite accepts further commands on the same transaction after a unique-key violation; SQL Server may abort the transaction depending on the error and isolation level. If your handler runs inside an open transaction, test against the target provider before relying on retry — many callers will prefer to retry **outside** the transaction scope and accept the round-trip cost.
- **Unrecognised `DbUpdateException` shapes are rethrown.** When `shouldRetry` returns false, the method maps duplicate → `"duplicate.key"`, FK → `"referential.integrity"`, and **rethrows** anything else (connection failures wrapped in `DbUpdateException`, trigger failures, provider-specific violations). This matches `SaveChangesResultAsync` semantics. The `"duplicate.key"` / `"referential.integrity"` reason codes are only returned when `shouldRetry` declines a recognised non-retryable failure; the helper's own exhaust/abort paths use `"retry.exhausted"` / `"retry.aborted"` so a broader `shouldRetry` classifier does not surface a misleading `duplicate.key` code.

### Idempotent inserts on a unique constraint

`TryInsertUniqueAsync` is for the other common shape: **INSERT a row that may already exist because the underlying operation can be replayed** — a worker recording "I dispatched event X to destination Y", an idempotency-key row keyed by `(EventId, Destination)`, an outbox de-duplicator, a "save unless exists" cache write. The success path returns the row; the duplicate path returns a `Result.Fail<TEntity>(Error.Conflict)` instead of an exception so the caller can treat the second delivery as a successful no-op. This is complementary to `SaveChangesWithRetryAsync`, not a substitute — use `SaveChangesWithRetryAsync` when the retry shape is "regenerate the colliding key", and `TryInsertUniqueAsync` when the retry shape is "second writer accepts the first writer won".

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed class DispatchLogger(DispatchLogDbContext db, TimeProvider time)
{
    public async Task<Result<DeliveryOutcome>> RecordAsync(
        Guid eventId, Guid destinationId, CancellationToken ct)
    {
        var entry = new DispatchedDelivery
        {
            EventId = eventId,
            DestinationId = destinationId,
            DispatchedAt = time.GetUtcNow(),
        };

        var result = await db.TryInsertUniqueAsync(entry, ct);

        if (result.IsSuccess)
            return Result.Ok(DeliveryOutcome.Recorded);

        if (result.Error is Error.Conflict { ReasonCode: "duplicate.key" })
            return Result.Ok(DeliveryOutcome.AlreadyRecorded);

        return Result.Fail<DeliveryOutcome>(result.Error!);
    }
}

public enum DeliveryOutcome { Recorded, AlreadyRecorded }
```

Footguns the API enforces or surfaces:

- **The helper requires a clean change tracker.** `TryInsertUniqueAsync` throws `InvalidOperationException` when `context.ChangeTracker.HasChanges()` is `true` on entry, because the helper cannot otherwise tell whether the duplicate-key violation came from the entity it added or from a sibling pending change. Flush unrelated changes first, or use a freshly-resolved scoped `DbContext` instance.
- **Detach scope is the graph the call attached.** `context.Add(entity)` walks navigations and may attach owned and dependent entities as `Added`. On the duplicate path, the helper detaches every entry it newly attached (root plus owned / dependent / join entries) so a retry with a freshly-constructed entity does not re-flush stale dependents. If `context.Add` flipped an already-tracked entity from another state to `Added` (e.g., the caller passed an instance the context loaded earlier), the helper restores it to its prior state instead of detaching it, so the caller does not lose tracking of a row it did not stage for removal.
- **Only duplicate-key violations are caught.** Foreign-key violations, `DbUpdateConcurrencyException`, connection failures wrapped in `DbUpdateException`, and `OperationCanceledException` all propagate so retry policies and global handlers still see them. When `OperationCanceledException` is raised from `SaveChangesAsync` (mid-save cancellation), the helper still performs the same change-tracker cleanup it performs on the duplicate-key path before rethrowing, so the cancelled call leaves the change tracker exactly as the caller saw it on entry and a retry on the same DI-scoped context is not blocked by `HasChanges() == true`.
- **The duplicate `Error.Conflict` carries constraint identity.** `ConstraintName` and `ConstraintTableName` are populated from `DbExceptionClassifier.ExtractConstraintIdentity` on a best-effort basis. They are marked `[JsonIgnore]` — telemetry for structured logs, never serialized to API responses. The safe-for-clients message stays in `Detail` (`"A record with the same unique value already exists."`).
- **Cancellation is checked before mutating the change tracker.** A pre-cancelled token throws `OperationCanceledException` after argument validation, before `context.Add`, so the change tracker is not mutated when the caller has already cancelled.

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

A handler can also opt into a **persist-on-failure** outcome by returning `Result.FailAfterCommit<T>(error)` — staged changes are still committed alongside the failure result. This is the canonical shape for a worker handler that wants to persist a `permanently_failed` row and surface the underlying error to the caller. See [Persisting failure state from a worker handler](integration-mediator.md#persisting-failure-state-from-a-worker-handler) in the mediator article for the full recipe.

For background jobs or non-mediator code, inject `IUnitOfWork` directly and call `CommitAsync`. Use `AddTrellisUnitOfWorkWithoutBehavior<TContext>()` to skip the pipeline behavior registration.

### Nested commands and scope-aware commit

A command handler may dispatch another command via `IMediator` (typical for orchestration handlers that compose smaller use cases). Both invocations go through `TransactionalCommandBehavior`, which means without depth tracking the inner command would commit *its own* changes the moment its handler returned — even though those changes were staged inside the outer command's transaction. If the outer command then failed, the inner's writes would already be persisted.

`EfUnitOfWork<TContext>` solves this with `BeginScope()`. Each call increments a depth counter; the returned `IDisposable` decrements it on disposal. `CommitAsync` reads the depth and **defers** (returns `Result.Ok()` without touching the database) when depth > 1. Only the outermost scope's commit actually calls `SaveChangesAsync`. The behavior wraps every command in `using var scope = unitOfWork.BeginScope();`, so the pattern is automatic — handlers do not call `BeginScope` themselves.

```csharp
public async ValueTask<Result<Unit>> Handle(ShipOrder cmd, CancellationToken ct)
{
    // Outer command's staged work goes into the same DbContext as the inner's.
    var order = (await _orders.FindByIdAsync(cmd.OrderId, ct))
        .ToResult(new Error.NotFound(ResourceRef.For<Order>(cmd.OrderId)));

    // Inner command goes through the mediator pipeline → another scope is opened.
    // Its TransactionalCommandBehavior calls CommitAsync at depth 2 → no-op.
    await _mediator.Send(new RecordAuditEntry(cmd.OrderId), ct);

    return order.Bind(o => o.Ship());
    // TransactionalCommandBehavior commits at depth 1 → SaveChangesAsync persists
    // both the outer's Ship() and the inner's audit entry atomically.
}
```

> [!IMPORTANT]
> The unit of work is shared with the outer's `DbContext`, so per-scope rollback of staged changes is not supported. If an inner command returns `Result.Fail` but the outer handler ignores that failure and returns success, the outer's commit will persist any changes the inner staged before failing. Handlers that need to discard inner failures' staged work must detach the affected entities themselves.

> [!WARNING]
> The depth counter is per-`IUnitOfWork`-instance — i.e. per DI scope. **Concurrent commands on the same scoped `IUnitOfWork`** (e.g. `Task.WhenAll(mediator.Send(a), mediator.Send(b))` from inside a handler) are **not supported**: their scopes share the counter and one command's commit can suppress or get folded into the other's. This is consistent with EF Core's existing constraint that `DbContext` is not thread-safe — concurrent dispatch on a single request scope is unsafe regardless. To run commands in parallel, give each one its own DI scope via `IServiceScopeFactory.CreateScope()` so each resolves its own `IUnitOfWork` and `DbContext`.

> [!NOTE]
> **Custom `IUnitOfWork` implementations must implement `BeginScope()` with the same depth-aware semantics.** Mirror the `EfUnitOfWork<TContext>` shape: an `Interlocked.Increment`-counted depth field with a disposable releaser; `CommitAsync` returns `Result.Ok()` at depth > 1 and persists otherwise. The `Trellis.Asp` package's `SAMPLES.md` shows a complete custom `UnitOfWork` example with this shape.

## Optimistic concurrency

Once `ApplyTrellisConventions*` and `AddTrellisInterceptors()` are wired:

- the aggregate `ETag` is configured as a concurrency token,
- a new `ETag` is generated on **Added** and **Modified** aggregates,
- aggregate roots are also promoted when loaded dependents change, so concurrency works at the aggregate boundary.

A losing writer surfaces as `DbUpdateConcurrencyException` → `new Error.Conflict(null, "concurrent-modification")` from `SaveChangesResult*Async` (and therefore from `IUnitOfWork.CommitAsync` / the transactional pipeline behavior). You do **not** configure `AggregateETagConvention` or `AggregateETagInterceptor` directly — they are internal types reached through the supported public entry points.

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
- Acl layer `ValueConverter` pattern for providers that cannot translate `DateTimeOffset` in `ORDER BY` on inherited audit columns: [Provider-specific column mapping](../api_reference/trellis-api-efcore.md#provider-specific-column-mapping)
- `Result<T>`, `Maybe<T>`, `Error`, `Aggregate<TId>`, `Specification<T>`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Recipes (specifications with `Maybe<T>`, repository patterns, UoW): [`trellis-api-cookbook.md`](../api_reference/trellis-api-cookbook.md)
- Mediator pipeline behaviors and registration order: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- Analyzer rules referenced here (`TRLS013`, `TRLS015`, `TRLS016`, `TRLS022`): [`trellis-api-analyzers.md`](../api_reference/trellis-api-analyzers.md)
