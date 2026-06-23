---
package: Trellis.EntityFrameworkCore
namespaces: [Trellis.EntityFrameworkCore]
types: [DbContextExtensions, DbContextIdempotencyExtensions, DbContextOptionsBuilderExtensions, DbContextRetryExtensions, DbExceptionClassifier, "EfUnitOfWork<TContext>", EntityTimestampInterceptor, IUnitOfWork, MaybeEntityTypeBuilderExtensions, MaybeModelExtensions, MaybePropertyMapping, MaybeQueryableExtensions, MaybeQueryInterceptor, MaybeUpdateExtensions, ModelConfigurationBuilderExtensions, OwnedEntityAttribute, QueryableExtensions, "RepositoryBase<TAggregate,TId>", ScalarValueQueryInterceptor, "TransactionalCommandBehavior<TMessage,TResponse>", TrellisPersistenceMappingException, "TrellisScalarConverter<TModel,TProvider>", UnitOfWorkServiceCollectionExtensions]
version: v3
last_verified: 2026-05-31
audience: [llm]
---
# Trellis.EntityFrameworkCore

**Package:** `Trellis.EntityFrameworkCore` (bundles the `Trellis.EntityFrameworkCore.Generator.dll` source generator at `analyzers/dotnet/cs/` — installing `Trellis.EntityFrameworkCore` attaches the `Maybe<T>` / `[OwnedEntity]` generator automatically; there is no separate `Trellis.EntityFrameworkCore.Generator` NuGet package).
**Namespace:** `Trellis.EntityFrameworkCore`  
**Purpose:** EF Core conventions, interceptors, converters, and query/update helpers for Trellis aggregates, value objects, and `Maybe<T>`.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#trellis-cross-package-cookbook) — recipes using this package.

## Use this file when

- You are persisting aggregates with `RepositoryBase<TAggregate, TId>`.
- You are querying `Maybe<T>` properties with `MaybeQueryableExtensions` or raw expressions that require `MaybeQueryInterceptor`.
- You are applying EF Core conventions with `ApplyTrellisConventions` or registering transactional command behavior with `AddTrellisUnitOfWork<TContext>`.
- You need idempotent inserts via `DbContextIdempotencyExtensions` or retry-on-collision helpers via `DbContextRetryExtensions`.

## Patterns Index

Use this table to find the canonical Trellis API for the most common EF Core tasks. Search this section first before writing custom expressions over `Maybe<T>` properties or hand-rolled `SaveChangesAsync` wrappers — the helpers below are interceptor-aware and analyzer-checked.

| Goal | Use this | See |
|---|---|---|
| Filter an `IQueryable<T>` by a `Maybe<TInner>` property (`<`, `<=`, `>`, `>=`, `==`, `HasValue`, `None`) | `MaybeQueryableExtensions.WhereLessThan` / `WhereLessThanOrEqual` / `WhereGreaterThan` / `WhereGreaterThanOrEqual` / `WhereEquals` / `WhereHasValue` / `WhereNone` | [`MaybeQueryableExtensions`](#maybequeryableextensions) |
| Order an `IQueryable<T>` by a `Maybe<TInner>` property | `OrderByMaybe` / `OrderByMaybeDescending` / `ThenByMaybe` / `ThenByMaybeDescending` | [`MaybeQueryableExtensions`](#maybequeryableextensions) |
| Make `Maybe<T>.GetValueOrDefault(d)` and similar expressions translate in EF queries (alternative to the helpers above when you must write a raw expression) | Register `AddTrellisInterceptors()` on the `DbContextOptionsBuilder`. The `MaybeQueryInterceptor` rewrites supported `Maybe<T>` calls to SQL. Prefer the `WhereXxx` helpers above when available. | [`DbContextOptionsBuilderExtensions`](#dbcontextoptionsbuilderextensions), [`MaybeQueryInterceptor`](#maybequeryinterceptor) |
| Index a `Maybe<T>` property (avoids TRLS016 by mapping to the storage member) | `entityTypeBuilder.HasTrellisIndex(x => x.M)` (or composite `x => new { x.M, x.Other }`) | [`MaybeEntityTypeBuilderExtensions`](#maybeentitytypebuilderextensions) |
| Save changes and get a `Result<int>` / `Result<Unit>` instead of throwing | `db.SaveChangesResultAsync()` / `db.SaveChangesResultUnitAsync()` (analyzer TRLS015 enforces in non-UoW contexts) | [`DbContextExtensions`](#dbcontextextensions) |
| Save with retry-on-collision for system-generated unique keys (short codes, slugs, tokens) — detaches only the conflicting entries, calls back to regenerate the key in place, re-Adds, retries | `db.SaveChangesWithRetryAsync(shouldRetry, regenerate, maxAttempts: 3)` (only `Added` entries; sibling aggregates and entries promoted via `entry.State = Added` are preserved; concurrency exceptions bypass `shouldRetry`) | [`DbContextRetryExtensions`](#dbcontextretryextensions) |
| Insert a row idempotently on a unique constraint (worker outboxes, "save unless exists" deduplication, redelivered events) — converts a unique-key violation into a failed `Result<TEntity>` carrying an `Error.Conflict` with reason code `"duplicate.key"` and provider-extracted `ConstraintName` / `ConstraintTableName`, and detaches the introduced graph on the duplicate path | `db.TryInsertUniqueAsync(entity, ct)` (requires a clean change tracker; FK / concurrency / cancellation / connection exceptions propagate) | [`DbContextIdempotencyExtensions`](#dbcontextidempotencyextensions) |
| Update a `Maybe<T>` property via EF Core `ExecuteUpdate` | `MaybeUpdateExtensions.SetMaybeValue(...)` (set Some) / `SetMaybeNone(...)` (clear) | [`MaybeUpdateExtensions`](#maybeupdateextensions) |
| Mark a composite value object as EF-owned (replaces `OwnsOne`/`OwnsMany` boilerplate) | `[OwnedEntity]` on the value-object class. Init-only setters are flagged by TRLS022 — use `{ get; private set; }`. | [`OwnedEntityAttribute`](#ownedentityattribute) |
| Wire Trellis EF conventions in `ConfigureConventions` (preferred — compile-time, no reflection) | `configurationBuilder.ApplyTrellisConventionsFor<TContext>()` (source-generated) | [`GeneratedTrellisConventions`](#generatedtrellisconventions-source-generated) |
| Wire Trellis EF conventions via runtime assembly scan (fallback) | `configurationBuilder.ApplyTrellisConventions(typeof(TContext).Assembly)` | [`ModelConfigurationBuilderExtensions`](#modelconfigurationbuilderextensions) |
| Wire `MaybeQueryInterceptor`, `EntityTimestampInterceptor`, ETag, and scalar-value interceptors in one call | `optionsBuilder.AddTrellisInterceptors()` (overloads accept a `TimeProvider`) | [`DbContextOptionsBuilderExtensions`](#dbcontextoptionsbuilderextensions) |
| Inspect / debug discovered `Maybe<T>` mappings | `dbContext.GetMaybePropertyMappings()` / `dbContext.ToMaybeMappingDebugString()` | [`MaybeModelExtensions`](#maybemodelextensions) |
| Project an aggregate to a DTO and unwrap `Maybe<T>` safely (avoids TRLS013) | Filter with `.Where(x => x.M.HasValue)` *before* the projection (TRLS013 recognises this exact prior-Where shape). For EF query composition over `Maybe<T>`, prefer `MaybeQueryableExtensions.WhereHasValue` / `WhereXxx` so the SQL is correct, then project. | [`MaybeQueryableExtensions`](#maybequeryableextensions) |
| Classify an EF/DB exception | `DbExceptionClassifier.IsDuplicateKey(ex)` / `IsForeignKeyViolation(ex)` / `ExtractConstraintDetail(ex)` / `ExtractConstraintIdentity(ex)`. To map DB exceptions to a Trellis `Error` automatically, use `db.SaveChangesResultAsync()` / `SaveChangesResultUnitAsync()` (or, for idempotent inserts, `db.TryInsertUniqueAsync()`) instead of catching and classifying by hand. | [`DbExceptionClassifier`](#dbexceptionclassifier), [`DbContextExtensions`](#dbcontextextensions), [`DbContextIdempotencyExtensions`](#dbcontextidempotencyextensions) |
| Wrap an aggregate-store repository with `Result<T>` returns | Inherit `RepositoryBase<TAggregate, TId>` | [`RepositoryBase<TAggregate, TId>`](#repositorybasetaggregate-tid) |
| Stage commands in a unit of work and flush once per request | `IUnitOfWork` + `EfUnitOfWork<TContext>` + `TransactionalCommandBehavior<,>` (registered via `AddTrellisUnitOfWork<TContext>()`) | [`IUnitOfWork`](#iunitofwork), [`EfUnitOfWork<TContext>`](#efunitofworktcontext), [`TransactionalCommandBehavior<TMessage, TResponse>`](#transactionalcommandbehaviortmessage-tresponse) |
| Paginate an `IQueryable<T>` with forward-only cursor-based seek (decodes the cursor, applies the seek `WHERE`, over-fetches, and slices through `PageBuilder` in one call) | `IQueryable<T>.ToPageAsync(pageSize, cursor, keySelector, …)` | [`PaginationQueryableExtensions`](#paginationqueryableextensions) |

## Common traps

- Do not hide overdue/date predicates inside repositories when the domain needs a reusable concept. Put the predicate in a `Specification<T>` and let repositories consume it.
- For EF `IQueryable` predicates over `Maybe<T>`, prefer `MaybeQueryableExtensions.WhereXxx` helpers over sentinel `GetValueOrDefault(...)` expressions when there is a matching helper.
- Under `AddTrellisUnitOfWork<TContext>()`, repositories stage changes only; the mediator transaction behavior commits.
- **Composite value objects need a parameterless constructor for EF Core materialization.** `[OwnedEntity]` (on a `partial` class with `{ get; private set; }` properties) generates a private one. `[OwnedEntity]` is **optional**: composite value objects are registered as owned types by convention (from `ValueObject` inheritance), not by the attribute — the attribute exists *only* to generate the parameterless constructor. So you have two equivalent options: annotate with `[OwnedEntity]`, **or** declare a private parameterless constructor yourself (as `Money` does). A mapped composite value object with neither fails fast at model-build with an actionable `TrellisPersistenceMappingException` naming the type — replacing EF Core's cryptic "No suitable constructor was found".
  - **`[OwnedEntity]` is convenient but not EF-free; the hand-written constructor is.** The attribute is a type in `Trellis.EntityFrameworkCore`, so a class that *applies* `[OwnedEntity]` makes its assembly reference `Trellis.EntityFrameworkCore` — and EF Core comes along as a transitive dependency. `ReferenceOutputAssembly="false"` does not strip a reference to a type you actually use, so it does not change this. Referencing the package **analyzer-only** (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`) is still the right way to pull *just the bundled generators* — the `[OwnedEntity]` constructor generator and the `partial Maybe<T>` generator — into a domain layer, and is required if that layer uses `partial Maybe<T>` properties; but a layer that *applies* `[OwnedEntity]` is not EF-free regardless. For a domain value object that must stay genuinely free of any EF Core dependency, declare the `private` parameterless constructor by hand (as `Money` does) — that uses no type from `Trellis.EntityFrameworkCore` and needs no reference to it at all.
- **`Maybe<T>` natural-form equality (`c.Phone == Maybe.From(value)`, `c.Phone == Maybe<T>.None`).** Translates correctly when `AddTrellisInterceptors()` is registered. The companion `MaybeEvaluatableExpressionFilterPlugin` keeps the three literal operand shapes — `Maybe<T>.None`, `default(Maybe<T>)`, and `Maybe.From(value)` — un-funcletized so `MaybeExpressionRewriter` translates `== Maybe.From(value)` to `_field = @p` and `== Maybe<T>.None` to `_field IS NULL`. **Do not** compare against a captured `Maybe<T>` local (e.g., `var m = Maybe.From(value); .Where(c => c.Phone == m)`); funcletization extracts the local to a `QueryParameterExpression` and the rewriter throws `InvalidOperationException` (a strict improvement over the historic silent `IS NULL` miss-query). Inline `Maybe.From(value)` at the comparison site, or use `MaybeQueryableExtensions.WhereEquals(c => c.Phone, value)`.
- **Do not project a bare `Maybe<T>` property in a `.Select(c => c.Phone)` clause inside EF queries.** `MaybeQueryInterceptor` rewrites bare `c.Phone` to `EF.Property<T?>(c, "_phone")`, which changes the projection's lambda return type from `Maybe<T>` to `T?` and produces an EF translation error. Project the storage value instead via `.Select(c => c.Phone.GetValueOrDefault(default))` (with `AddTrellisInterceptors()` registered) or fetch the entity and read `Phone` after materialization.

### `Maybe<T>` query shape decision table

Use this table before writing predicates over `Maybe<T>` so fake repositories, EF SQL translation, and analyzers agree.

| Code location | Preferred shape | Required setup / caveat |
|---|---|---|
| Reusable `Specification<T>.ToExpression()` used by both EF and `FakeRepository<T,TId>` | Use a natural expression that does not duplicate fake-only logic, usually `x.M.GetValueOrDefault(sentinel) < value` or a parenthesized immediate guard `(x.M.HasValue && x.M.Value < value)`. | EF translation requires `ApplyTrellisConventions(...)` or `ApplyTrellisConventionsFor<TContext>()` plus `optionsBuilder.AddTrellisInterceptors()`. |
| Ad-hoc EF `IQueryable<T>` filtering or ordering | Prefer `WhereHasValue`, `WhereNone`, `WhereEquals`, `WhereLessThan`, `WhereGreaterThanOrEqual`, `OrderByMaybe`, etc. | These helpers target the mapped storage member directly and do not require the `MaybeQueryInterceptor` for that specific predicate. |
| Projection after filtering for presence | Filter first with `.Where(x => x.M.HasValue)` or `.WhereHasValue(x => x.M)`, then project the value. | This is the shape TRLS013 can recognize; projecting `.Value` before a presence filter is unsafe. |
| `ExecuteUpdate` over `Maybe<T>` | Use `SetMaybeValue(...)` or `SetMaybeNone(...)`. | Composite owned `Maybe<T>` values are not supported by the scalar update helpers. |

Never write a different predicate for `FakeRepository` than for EF. If a reusable concept matters to the domain, put it in a `Specification<T>` and run the same expression in both paths.

## Types

### `DbContextOptionsBuilderExtensions`

```csharp
public static class DbContextOptionsBuilderExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder) where TContext : DbContext` | `DbContextOptionsBuilder<TContext>` | Registers singleton `MaybeQueryInterceptor`, `ScalarValueQueryInterceptor`, internal `AggregateETagInterceptor`, and singleton `EntityTimestampInterceptor`. |
| `public static DbContextOptionsBuilder AddTrellisInterceptors(this DbContextOptionsBuilder optionsBuilder)` | `DbContextOptionsBuilder` | Non-generic overload for the same singleton interceptor set. |
| `public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, TimeProvider? timeProvider) where TContext : DbContext` | `DbContextOptionsBuilder<TContext>` | Registers the same interceptor set, but creates a **new** `EntityTimestampInterceptor(timeProvider)` for this call. |
| `public static DbContextOptionsBuilder AddTrellisInterceptors(this DbContextOptionsBuilder optionsBuilder, TimeProvider? timeProvider)` | `DbContextOptionsBuilder` | Non-generic overload that creates a new `EntityTimestampInterceptor(timeProvider)` for this call. |

Repeated calls with the same (or default) `TimeProvider` are idempotent; a subsequent call with a conflicting `TimeProvider` throws `InvalidOperationException`. Calling `AddTrellisInterceptors` multiple times on the same options builder registers each Trellis interceptor exactly once. Consumer-supplied interceptors registered separately via `optionsBuilder.AddInterceptors(...)` are preserved.

### `ModelConfigurationBuilderExtensions`

```csharp
public static class ModelConfigurationBuilderExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static ModelConfigurationBuilder ApplyTrellisConventions(this ModelConfigurationBuilder configurationBuilder, params Assembly[] assemblies)` | `ModelConfigurationBuilder` | Scans the supplied assemblies plus `Trellis.Core`, `Trellis.Primitives`, and `Trellis.Authorization`, registers scalar converters (including `ActorId` so `CreatedByActorId` audit fields work with no extra hand-in), collects composite value objects from those assemblies only, and adds internal conventions for `Maybe<T>`, composite value objects, `Money`, aggregate ETags, transient aggregate properties, and constructor-bound value-object properties (scalar and symbolic enum). |
| `public static ModelConfigurationBuilder ApplyTrellisConventionsCore(this ModelConfigurationBuilder configurationBuilder, IEnumerable<(Type ClrType, Type ProviderType)> scalars, IEnumerable<Type> composites)` | `ModelConfigurationBuilder` | Low-level helper used by the reflection-based `ApplyTrellisConventions` overload. Registers the supplied scalar converters via `Type.MakeGenericType`, then delegates to `AddTrellisCoreConventions`. |
| `public static ModelConfigurationBuilder AddTrellisScalarConverter<TClr, TProvider>(this ModelConfigurationBuilder configurationBuilder) where TClr : class where TProvider : notnull` | `ModelConfigurationBuilder` | Reflection-free strongly typed helper that registers `TrellisScalarConverter<TClr, TProvider>` for `TClr` properties. Emitted by the source generator; no `MakeGenericType` at runtime. |
| `public static ModelConfigurationBuilder AddTrellisCoreConventions(this ModelConfigurationBuilder configurationBuilder, IEnumerable<Type> composites)` | `ModelConfigurationBuilder` | Adds the fixed Trellis conventions (`ScalarValueObjectPropertyConvention`, `MaybeConvention`, `CompositeValueObjectConvention`, `MoneyConvention`, `AggregateETagConvention`, `AggregateTransientPropertyConvention`, `ValueObjectMappingGuardConvention`). `composites` is an array of pre-closed `Type` tokens supplied by the caller. |

### `GeneratedTrellisConventions` (source-generated)

Installing `Trellis.EntityFrameworkCore` also attaches the bundled `Trellis.EntityFrameworkCore.Generator.dll` analyzer. In the consuming project, that generator emits:

```csharp
namespace Trellis.EntityFrameworkCore;

public static class GeneratedTrellisConventions
{
    public static ModelConfigurationBuilder ApplyTrellisConventionsFor<TContext>(
        this ModelConfigurationBuilder configurationBuilder)
        where TContext : DbContext;
}
```

Use it from `ConfigureConventions` when you want compile-time discovery instead of runtime assembly scanning:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
    configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();
```

The generator walks every concrete `DbContext` in the current compilation, follows instance `DbSet<T>` properties (any accessibility — `public`, `internal`, `private`, etc., as long as the entity type is accessible to the generator), recursively discovers reachable Trellis value objects through entity properties, unwraps `Maybe<T>`, nullable types, arrays, and common collection navigations, and emits explicit calls to `AddTrellisScalarConverter<TClr, TProvider>` plus `AddTrellisCoreConventions(...)`.

Scope limits:

- `TContext` must be a concrete, accessible `DbContext` defined in the current compilation.
- The reachability walk starts at that context's accessible `DbSet<T>` properties.
- Calling `ApplyTrellisConventionsFor<TContext>()` for a skipped context throws `InvalidOperationException`.
- This removes Trellis' assembly scan and `MakeGenericType` path, but the EF Core package itself still opts out of NativeAOT/trimming support.

### `DbContextExtensions`

```csharp
public static class DbContextExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Result<int>> SaveChangesResultAsync(this DbContext context, CancellationToken cancellationToken = default)` | `Task<Result<int>>` | Convenience overload for `SaveChangesResultAsync(context, true, cancellationToken)`. |
| `public static Task<Result<int>> SaveChangesResultAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)` | `Task<Result<int>>` | Wraps `SaveChangesAsync`; maps `DbUpdateConcurrencyException` to `new Error.Conflict(null, "concurrent_modification")`, duplicate-key `DbUpdateException` to `new Error.Conflict(null, "duplicate.key")`, and foreign-key `DbUpdateException` to `new Error.Conflict(null, "referential.integrity")`. |
| `public static Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, CancellationToken cancellationToken = default)` | `Task<Result<Unit>>` | Saves changes and discards the row count. |
| `public static Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)` | `Task<Result<Unit>>` | Saves changes with explicit `acceptAllChangesOnSuccess`. |

### `DbContextRetryExtensions`

```csharp
public static class DbContextRetryExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Result<Unit>> SaveChangesWithRetryAsync(this DbContext db, Func<DbUpdateException, bool> shouldRetry, Func<IReadOnlyList<EntityEntry>, int, CancellationToken, ValueTask<bool>> regenerate, int maxAttempts = 3, CancellationToken cancellationToken = default)` | `Task<Result<Unit>>` | Saves changes. On `DbUpdateException` classified retryable by `shouldRetry`, detaches **only** the entries reported by `DbUpdateException.Entries` (never the full change tracker — sibling aggregates pending in the change tracker, including entries promoted via `entry.State = Added`, are preserved), invokes `regenerate(entries, attempt, ct)` so the caller can mutate the conflicting entities' natural keys in place, re-`Add`s them, and retries up to `maxAttempts` total attempts (initial + retries). `DbUpdateConcurrencyException` is mapped to `new Error.Conflict(null, "concurrent_modification")` **without** calling `shouldRetry` (regenerating a natural key cannot resolve a stale rowversion). When `shouldRetry` returns false, non-retryable `DbUpdateException`s are mapped exactly like `SaveChangesResultAsync`: duplicate → `"duplicate.key"`, FK → `"referential.integrity"`, unrecognised → rethrown. Only `Added` entries are supported — if `ex.Entries` contains a non-`Added` entry, throws `InvalidOperationException` (Modified retries lose original values, `IsModified` flags, and temporary-value metadata across the detach/re-attach cycle). When `regenerate` returns false, conflicting entries remain detached and the method returns `Error.Conflict` with reason code `"retry.aborted"`. When `maxAttempts` is exhausted, no detach is performed on the final attempt and the method returns `Error.Conflict` with reason code `"retry.exhausted"`. The helper's exhaust/abort paths use these neutral reason codes (distinct from the caller-classifier-related `"duplicate.key"` / `"referential.integrity"` codes) so a broader `shouldRetry` classifier does not surface a misleading `duplicate.key` code on exhaust/abort. `attempt` is 1-based and equals the regenerate-call number. The `duplicate.key` / `referential.integrity` `Error.Conflict` values returned by this helper carry `ConstraintName` / `ConstraintTableName` telemetry fields populated by `DbExceptionClassifier.ExtractConstraintIdentity` (see [`DbExceptionClassifier`](#dbexceptionclassifier)). |

### `DbContextIdempotencyExtensions`

```csharp
public static class DbContextIdempotencyExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Result<TEntity>> TryInsertUniqueAsync<TEntity>(this DbContext context, TEntity entity, CancellationToken cancellationToken = default) where TEntity : class` | `Task<Result<TEntity>>` | Adds `entity` to `context` and persists it; converts a unique-constraint violation into `Result.Fail<TEntity>(new Error.Conflict(null, "duplicate.key") { Detail = "A record with the same unique value already exists.", ConstraintName, ConstraintTableName })`. On the duplicate path, every entry the call newly attached as `Added` (root plus any owned / dependent entries from the navigation graph) is detached, and any already-tracked entry that `context.Add` flipped to `Added` (e.g., a row the context loaded earlier) is restored to its prior state — so the change tracker is left exactly as the caller saw it on entry. On success, returns `Result.Ok(entity)` with EF-populated generated values (PK, row version, sequence columns) in place on the same instance. Foreign-key violations, `DbUpdateConcurrencyException`, connection-level exceptions, and `OperationCanceledException` propagate so retry policies see them. The helper requires a clean `DbContext` on entry: throws `InvalidOperationException` when `context.ChangeTracker.HasChanges()` is `true` so a duplicate-key violation cannot be mis-attributed to the inserted entity. Constraint identity comes from `DbExceptionClassifier.ExtractConstraintIdentity`. |

### `QueryableExtensions`

```csharp
public static class QueryableExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default) where T : class` | `Task<Maybe<T>>` | Returns the first match or `Maybe<T>.None`. |
| `public static Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class` | `Task<Maybe<T>>` | Returns the first predicate match or `Maybe<T>.None`. |
| `public static Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default) where T : class` | `Task<Maybe<T>>` | Returns the single match or `Maybe<T>.None`; throws if more than one element matches. |
| `public static Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class` | `Task<Maybe<T>>` | Returns the single predicate match or `Maybe<T>.None`; throws if more than one element matches. |
| `public static Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Error notFoundError, CancellationToken cancellationToken = default) where T : class` | `Task<Result<T>>` | Returns the first match or **the exact `notFoundError` supplied by the caller**. |
| `public static Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, Error notFoundError, CancellationToken cancellationToken = default) where T : class` | `Task<Result<T>>` | Returns the first predicate match or **the exact `notFoundError` supplied by the caller**. |
| `public static IQueryable<T> Where<T>(this IQueryable<T> query, Specification<T> specification) where T : class` | `IQueryable<T>` | Applies a Trellis specification expression to the query. |

### `PaginationQueryableExtensions`

```csharp
public static class PaginationQueryableExtensions
```

EF Core seek-pagination helper that composes with the storage-agnostic `Trellis.Core` primitives (`PageSize`, `Cursor`, `CursorCodec`, `PageBuilder`, `Page<T>`). The method owns the `OrderBy(keySelector)`, the cursor decoding, the seek `WHERE` predicate, the `Take(Applied + 1)` over-fetch, and the slice — callers supply a pre-filtered `IQueryable<T>` and the sort-key projection.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Result<Page<T>>> ToPageAsync<T, TKey>(this IQueryable<T> source, PageSize pageSize, Cursor? cursor, Expression<Func<T, TKey>> keySelector, string? cursorFieldName = null, CancellationToken cancellationToken = default) where T : class where TKey : notnull, IComparable<TKey>, IParsable<TKey>` | `Task<Result<Page<T>>>` | Materializes one forward-only seek page. Returns `Result.Fail<Page<T>>(Error.InvalidInput.ForField(cursorFieldName ?? "cursor", "cursor.malformed", …))` on a malformed cursor and never throws on a bad client input. Throws `ArgumentNullException` for `null` `source` or `keySelector`, and `ArgumentOutOfRangeException` for a non-validated `pageSize` (e.g. `default(PageSize)`) before any SQL round-trip. The seek predicate uses `Expression.GreaterThan` for numeric and `DateTime`/`DateTimeOffset` keys and routes through `IComparable<TKey>.CompareTo` for `Guid` and `string` keys; see the `PaginationQueryableExtensions` XML docs for the full provider-support note. |

**Single-key seek requires a stable, unique ascending key.** With a non-unique key, rows that share the boundary value with the last item on the previous page are silently skipped on the next page — use a primary-key surrogate (or the upcoming composite `(CreatedAt, Id)` overload) when the natural sort key is not unique.

**Value-object Id projection** (`c => c.Id.Value`) requires `AddTrellisInterceptors()` on the `DbContextOptionsBuilder` so the `ScalarValueQueryInterceptor` rewrites the projection for EF translation. See [`DbContextOptionsBuilderExtensions`](#dbcontextoptionsbuilderextensions) and [`ScalarValueQueryInterceptor`](#scalarvaluequeryinterceptor).

### `RepositoryBase<TAggregate, TId>`

```csharp
public abstract class RepositoryBase<TAggregate, TId>
    where TAggregate : Aggregate<TId>
    where TId : notnull
```

Abstract generic repository base class for EF Core aggregate persistence. Provides standard read and staging methods. Repositories stage changes to the change tracker; the `IUnitOfWork` (typically driven by a pipeline behavior) is responsible for committing staged changes.

#### Properties

| Name | Type | Description |
| --- | --- | --- |
| `protected DbSet<TAggregate> DbSet` | `DbSet<TAggregate>` | The EF Core `DbSet` for this aggregate type. |
| `protected DbContext Context` | `DbContext` | The underlying `DbContext`. Use sparingly — prefer repository methods. |

#### Read Methods

| Signature | Returns | Description |
| --- | --- | --- |
| `public virtual Task<Maybe<TAggregate>> FindByIdAsync(TId id, CancellationToken ct = default)` | `Task<Maybe<TAggregate>>` | Finds a tracked aggregate by ID. Returns `Maybe<T>.None` if not found. |
| `public virtual Task<IReadOnlyList<TAggregate>> QueryAsync(Specification<TAggregate> spec, CancellationToken ct = default)` | `Task<IReadOnlyList<TAggregate>>` | Queries aggregates matching the specification (no-tracking by default). |
| `public virtual Task<bool> ExistsAsync(TId id, CancellationToken ct = default)` | `Task<bool>` | Lightweight check for existence by ID (no-tracking, no materialization). |
| `public virtual Task<bool> ExistsAsync(Specification<TAggregate> spec, CancellationToken ct = default)` | `Task<bool>` | Checks whether any aggregate matches the specification. |
| `public virtual Task<int> CountAsync(Specification<TAggregate> spec, CancellationToken ct = default)` | `Task<int>` | Counts aggregates matching the specification. |

#### Staging Methods (never call SaveChanges)

| Signature | Returns | Description |
| --- | --- | --- |
| `public virtual void Add(TAggregate aggregate)` | `void` | Stages a new aggregate for insertion. No-op if already tracked. |
| `public virtual void Remove(TAggregate aggregate)` | `void` | Stages an aggregate for deletion. |
| `public virtual Task<Result<Unit>> RemoveByIdAsync(TId id, CancellationToken ct = default)` | `Task<Result<Unit>>` | Looks up by ID via `DbSet.FindAsync` (avoids Include graphs) and stages for deletion. Returns not-found if absent. |

#### Virtual Hooks

| Signature | Description |
| --- | --- |
| `protected virtual IQueryable<TAggregate> BuildFindByIdQuery()` | Override to add `.Include()` or filters to the find-by-ID query. Defaults to `DbSet`. |
| `protected virtual IQueryable<TAggregate> BuildQueryBase()` | Override to add `.Include()` or filters to specification queries. Defaults to `DbSet.AsNoTracking()`. |
| `public virtual Task<IReadOnlyList<TAggregate>> QueryAsync(Specification<TAggregate> spec, CancellationToken ct)` | Override the public method itself when you need to add `.OrderBy(...)` / paging / `.AsSplitQuery()` etc. on top of the spec. **Use the `override` keyword** — declaring a same-named method without `override` triggers `CS0108: hides inherited member`. Inherited from the public method table above. |

#### Usage

```csharp
public class OrderRepository(DbContext context) : RepositoryBase<Order, OrderId>(context)
{
    protected override IQueryable<Order> BuildFindByIdQuery() =>
        DbSet.Include(o => o.LineItems);
}

// In a command handler (pipeline auto-commits on success):
var maybe = await _orders.FindByIdAsync(cmd.OrderId, ct);
return maybe
    .ToResult(new Error.NotFound(ResourceRef.For<Order>(cmd.OrderId)) { Detail = "Order not found." })
    .Bind(order => order.Ship());
// Tracked changes are committed automatically by TransactionalCommandBehavior.
```

### `IUnitOfWork`

```csharp
public interface IUnitOfWork
```

Abstraction over the commit boundary for staged changes. Repositories stage changes; calling `CommitAsync` persists them. In the standard Trellis pipeline, commit is handled automatically by `TransactionalCommandBehavior`. Inject `IUnitOfWork` directly only in non-pipeline scenarios (background jobs, integration tests).

| Signature | Returns | Description |
| --- | --- | --- |
| `Task<Result<Unit>> CommitAsync(CancellationToken ct = default)` | `Task<Result<Unit>>` | Persists all staged changes. Surfaces concurrency, duplicate-key, and FK errors as `Error` instead of exceptions. Implementations must defer (return success without persisting) when called inside a nested `BeginScope` scope so a successful inner command does not commit a partially-completed outer command's staged changes. |
| `IDisposable BeginScope()` | `IDisposable` | Begins a unit-of-work scope; nested scopes track depth so only the outermost scope's `CommitAsync` actually persists. The Trellis pipeline's `TransactionalCommandBehavior` wraps every command in a scope. Custom `IUnitOfWork` implementations are required to implement depth-aware scope tracking; `EfUnitOfWork<TContext>` does this with an internal counter. **Caveat:** if an inner command returns failure but the outer ignores it and returns success, the outer's commit will persist any changes the inner staged before failing — per-scope rollback of staged changes is not supported. |

### `EfUnitOfWork<TContext>`

```csharp
public class EfUnitOfWork<TContext> : IUnitOfWork, ITrackedAggregateSource
    where TContext : DbContext
```

EF Core implementation of `IUnitOfWork`. Delegates to `DbContextExtensions.SaveChangesResultUnitAsync` which maps `DbUpdateConcurrencyException` → `new Error.Conflict(null, "concurrent_modification")`, duplicate-key → `new Error.Conflict(null, "duplicate.key")`, and FK violations → `new Error.Conflict(null, "referential.integrity")`. Tracks scope depth via an internal counter; `CommitAsync` defers (returns success without persisting) when depth > 1.

Also implements [`ITrackedAggregateSource`](trellis-api-core.md#itrackedaggregatesource): captures the `IAggregate` instances tracked by the `ChangeTracker` immediately before each `SaveChangesAsync` call. The snapshot is cleared before save and only assigned on success, so `CommittedAggregates` is empty before any commit, empty after a failed or thrown commit, unchanged during deferred nested commits, and replaced on the next successful outer commit. Consumed by `Trellis.Mediator.TrackedAggregateDomainEventDispatchBehavior<,>`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public EfUnitOfWork(TContext context)` | — | Captures the resolved `TContext` instance. Throws `ArgumentNullException` when `context` is null. Registered as scoped by `AddTrellisUnitOfWork<TContext>()`. |
| `public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)` | `Task<Result<Unit>>` | At depth 0/1, clears the tracked-aggregate snapshot, snapshots all `IAggregate` change-tracker entries, calls `context.SaveChangesResultUnitAsync(cancellationToken)`, and (only on success) assigns the snapshot to `CommittedAggregates`. At depth > 1 (inside a nested scope), returns `Result.Ok()` without touching the database or the snapshot. |
| `public IDisposable BeginScope()` | `IDisposable` | Increments the scope-depth counter; the returned `IDisposable.Dispose()` decrements it. Thread-safe via `Interlocked`. |
| `IReadOnlyList<IAggregate> ITrackedAggregateSource.CommittedAggregates { get; }` | `IReadOnlyList<IAggregate>` | Snapshot of aggregates the most recent successful commit persisted. Empty by default. |

### `TransactionalCommandBehavior<TMessage, TResponse>`

```csharp
public sealed class TransactionalCommandBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult, IFailureFactory<TResponse>
```

Pipeline behavior that auto-commits staged changes after a successful command handler. Only applies to `ICommand<TResponse>` messages — queries are skipped at the type-constraint level and incur no overhead. If the handler returns a failure, no commit occurs and staged changes are discarded with the `DbContext`. EF Core wraps each `SaveChanges` call in an implicit transaction, so all staged changes within a single handler commit atomically.

> **Persist-on-failure outcomes.** If `TResponse` implements `IPersistOnFailure` and the per-instance `PersistOnFailure` flag is `true` — the canonical producer is `Result.FailAfterCommit<T>(error)` — the commit step runs even though the result is a failure. This enables the worker-handler pattern of persisting a `permanently_failed` state row alongside the failure outcome. On commit failure for a persist-on-failure outcome, the commit error replaces the handler error in the returned response.

> **Important:** This behavior is **not** registered by `Trellis.Mediator.ServiceCollectionExtensions.AddTrellisBehaviors()`. Consumers of `Trellis.EntityFrameworkCore` must register it explicitly via `AddTrellisUnitOfWork<TContext>()` (see below) **after** `AddTrellisBehaviors()` so it lands innermost — closest to the handler — and commit failures remain visible to outer logging/tracing/exception behaviors.

| Signature | Returns | Description |
| --- | --- | --- |
| `public TransactionalCommandBehavior(IUnitOfWork unitOfWork)` | — | Captures the scoped `IUnitOfWork` resolved alongside the handler. Throws `ArgumentNullException` when `unitOfWork` is null. |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Wraps the invocation in `using var scope = unitOfWork.BeginScope();` so a successful inner command's commit is deferred until the outermost scope unwinds. Awaits the inner handler; on success **or** persist-on-failure (`result is IPersistOnFailure { PersistOnFailure: true }`), calls `unitOfWork.CommitAsync(cancellationToken)`; if the commit reports an `Error`, returns `TResponse.CreateFailure(error)` (overwriting the original handler error for persist-on-failure outcomes). On ordinary failure, returns the failure as-is without committing. |

### `UnitOfWorkServiceCollectionExtensions`

```csharp
public static class UnitOfWorkServiceCollectionExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellisUnitOfWork<TContext>(this IServiceCollection services) where TContext : DbContext` | `IServiceCollection` | Registers `EfUnitOfWork<TContext>` as `IUnitOfWork` (and forwards it as `ITrackedAggregateSource` for `Trellis.Mediator.TrackedAggregateDomainEventDispatchBehavior<,>`) and adds the open-generic `TransactionalCommandBehavior<,>` pipeline behavior. See *Behavioral notes: AddTrellisUnitOfWork<TContext>* below. |
| `public static IServiceCollection AddTrellisUnitOfWorkWithoutBehavior<TContext>(this IServiceCollection services) where TContext : DbContext` | `IServiceCollection` | Registers `EfUnitOfWork<TContext>` without the pipeline behavior (also forwarded as `ITrackedAggregateSource`). Use for manual commit control or non-Mediator scenarios. |

### Behavioral notes: `AddTrellisUnitOfWork<TContext>`

`AddTrellisUnitOfWork<TContext>()` inserts `TransactionalCommandBehavior<,>` after the last existing `IPipelineBehavior<,>` registration, making it innermost in the mediator pipeline. It is idempotent for an existing open-generic `TransactionalCommandBehavior<,>`: a second call is a no-op. Call it **after** `AddTrellisBehaviors()` so that commit failures are visible to outer behaviors such as logging and tracing. It **throws `InvalidOperationException`** when a closed-generic `IPipelineBehavior<TMessage,TResponse> → TransactionalCommandBehavior<TMessage,TResponse>` is already registered, with or without an open-generic registration, because installing the open generic alongside would resolve both descriptors for matching commands and produce two commits per command. The exception message names the supported resolutions: remove the closed registration and let the helper install the open generic, or call `AddTrellisUnitOfWorkWithoutBehavior<TContext>()` to keep the explicit closed registrations and skip open-generic installation.

### `EntityTimestampInterceptor`

```csharp
public sealed class EntityTimestampInterceptor : SaveChangesInterceptor
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public EntityTimestampInterceptor(TimeProvider? timeProvider = null)` | — | Uses the supplied `TimeProvider`, or `TimeProvider.System` when `null`. |
| `public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)` | `InterceptionResult<int>` | Sets `CreatedAt` and `LastModified` for added entities, sets `LastModified` for modified entities, and also updates `LastModified` on unchanged aggregate roots when loaded dependents are added, modified, or deleted. |
| `public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)` | `ValueTask<InterceptionResult<int>>` | Async equivalent of `SavingChanges`; includes unchanged aggregate-root promotion when loaded dependents change. |

> **Note:** `EntityTimestampInterceptor` writes the CLR property values, but the column mapping for the stored representation is still the Acl's responsibility. See [Provider-specific column mapping](#provider-specific-column-mapping) when a provider cannot translate `DateTimeOffset` (or any other CLR type) on `CreatedAt` / `LastModified`.

### `MaybeQueryableExtensions`

```csharp
public static class MaybeQueryableExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IQueryable<TEntity> WhereNone<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull` | `IQueryable<TEntity>` | Filters to rows whose mapped `Maybe<TInner>` storage member is `NULL`. |
| `public static IQueryable<TEntity> WhereHasValue<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull` | `IQueryable<TEntity>` | Filters to rows whose mapped `Maybe<TInner>` storage member is not `NULL`. |
| `public static IQueryable<TEntity> WhereEquals<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull` | `IQueryable<TEntity>` | Filters to rows whose mapped `Maybe<TInner>` storage member equals `value`. |
| `public static IQueryable<TEntity> WhereLessThan<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull, IComparable<TInner>` | `IQueryable<TEntity>` | Filters to rows whose mapped `Maybe<TInner>` storage member is less than `value`. |
| `public static IQueryable<TEntity> WhereLessThanOrEqual<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull, IComparable<TInner>` | `IQueryable<TEntity>` | Filters to rows whose mapped `Maybe<TInner>` storage member is less than or equal to `value`. |
| `public static IQueryable<TEntity> WhereGreaterThan<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull, IComparable<TInner>` | `IQueryable<TEntity>` | Filters to rows whose mapped `Maybe<TInner>` storage member is greater than `value`. |
| `public static IQueryable<TEntity> WhereGreaterThanOrEqual<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull, IComparable<TInner>` | `IQueryable<TEntity>` | Filters to rows whose mapped `Maybe<TInner>` storage member is greater than or equal to `value`. |
| `public static IOrderedQueryable<TEntity> OrderByMaybe<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull` | `IOrderedQueryable<TEntity>` | Orders by the mapped `Maybe<TInner>` storage member ascending. |
| `public static IOrderedQueryable<TEntity> OrderByMaybeDescending<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull` | `IOrderedQueryable<TEntity>` | Orders by the mapped `Maybe<TInner>` storage member descending. |
| `public static IOrderedQueryable<TEntity> ThenByMaybe<TEntity, TInner>(this IOrderedQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull` | `IOrderedQueryable<TEntity>` | Adds a secondary ascending ordering for the mapped `Maybe<TInner>` storage member. |
| `public static IOrderedQueryable<TEntity> ThenByMaybeDescending<TEntity, TInner>(this IOrderedQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull` | `IOrderedQueryable<TEntity>` | Adds a secondary descending ordering for the mapped `Maybe<TInner>` storage member. |

### `MaybeUpdateExtensions`

```csharp
public static class MaybeUpdateExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static UpdateSettersBuilder<TEntity> SetMaybeValue<TEntity, TInner>(this UpdateSettersBuilder<TEntity> updateSettersBuilder, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull` | `UpdateSettersBuilder<TEntity>` | Sets a mapped scalar `Maybe<TInner>` property inside `ExecuteUpdate`; throws for composite owned types. |
| `public static UpdateSettersBuilder<TEntity> SetMaybeNone<TEntity, TInner>(this UpdateSettersBuilder<TEntity> updateSettersBuilder, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull` | `UpdateSettersBuilder<TEntity>` | Clears a mapped scalar `Maybe<TInner>` property inside `ExecuteUpdate`; throws for composite owned types. |

### `MaybeEntityTypeBuilderExtensions`

```csharp
public static class MaybeEntityTypeBuilderExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IndexBuilder<TEntity> HasTrellisIndex<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder, Expression<Func<TEntity, object?>> propertySelector) where TEntity : class` | `IndexBuilder<TEntity>` | Creates an index using CLR selectors and resolves any `Maybe<T>` selectors to the actual generated storage-member mapping. |

### `MaybeModelExtensions`

```csharp
public static class MaybeModelExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this IModel model)` | `IReadOnlyList<MaybePropertyMapping>` | Returns all discovered `Maybe<T>` mappings from an EF Core model. |
| `public static IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this DbContext dbContext)` | `IReadOnlyList<MaybePropertyMapping>` | Convenience overload for `dbContext.Model`. |
| `public static string ToMaybeMappingDebugString(this IModel model)` | `string` | Produces a multi-line debug summary of `Maybe<T>` mappings. |
| `public static string ToMaybeMappingDebugString(this DbContext dbContext)` | `string` | Convenience overload for `dbContext.Model`. |

### `MaybePropertyMapping`

```csharp
public sealed record MaybePropertyMapping(
    string EntityTypeName,
    Type EntityClrType,
    string PropertyName,
    string MappedBackingFieldName,
    Type InnerType,
    Type StoreType,
    bool IsMapped,
    bool IsNullable,
    string? ColumnName,
    Type? ProviderClrType);
```

Diagnostic record describing how a `Maybe<T>` property resolved to an EF Core mapped backing field. Returned by `MaybeModelExtensions.GetMaybePropertyMappings(...)` and rendered by `ToMaybeMappingDebugString(...)`.

| Name | Type | Description |
| --- | --- | --- |
| `EntityTypeName` | `string` | EF Core entity type name. |
| `EntityClrType` | `Type` | CLR type for the entity. |
| `PropertyName` | `string` | Original `Maybe<T>` CLR property name. |
| `MappedBackingFieldName` | `string` | Generated or configured storage-member (private backing field) name used by the EF model. |
| `InnerType` | `Type` | `T` from `Maybe<T>`. |
| `StoreType` | `Type` | CLR type EF Core persists for the storage member. |
| `IsMapped` | `bool` | `true` when a backing field or owned navigation mapping exists. |
| `IsNullable` | `bool` | `true` when the EF mapping is nullable/optional. |
| `ColumnName` | `string?` | Representative relational column name, if available. |
| `ProviderClrType` | `Type?` | Provider CLR type after conversion, if available. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public MaybePropertyMapping(string EntityTypeName, Type EntityClrType, string PropertyName, string MappedBackingFieldName, Type InnerType, Type StoreType, bool IsMapped, bool IsNullable, string? ColumnName, Type? ProviderClrType)` | — | Positional record constructor. Instances are produced by `MaybeModelExtensions`; consumer code typically reads them rather than constructing them. |
| — | — | No additional methods beyond compiler-generated record members (`Equals`, `GetHashCode`, `ToString`, `Deconstruct`, `with`-clone). |

### `DbExceptionClassifier`

```csharp
public static class DbExceptionClassifier
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static bool IsDuplicateKey(DbUpdateException ex)` | `bool` | Detects duplicate-key violations across SQL Server (errors 2601/2627), PostgreSQL (SQLSTATE 23505), SQLite (`UNIQUE constraint failed` / `PRIMARY KEY constraint failed`), MySQL/MariaDB (error 1062 or `Duplicate entry` message — works with both `MySql.Data.MySqlClient` and `MySqlConnector`), and generic message-based fallbacks. Provider exception types are detected by name, so consumers do not take a transitive dependency on any particular driver. SQLSTATE 23000 is intentionally **not** trusted on its own for MySQL because that code is reused for foreign-key violations. |
| `public static bool IsForeignKeyViolation(DbUpdateException ex)` | `bool` | Detects foreign-key violations across SQL Server (error 547), PostgreSQL (SQLSTATE 23503), SQLite (`FOREIGN KEY constraint failed`), MySQL/MariaDB (errors 1451/1452 or `Cannot add or update a child row` / `Cannot delete or update a parent row` message), and generic message-based fallbacks. Provider exception types are detected by name. The MySQL message-prefix detection runs unconditionally rather than gated on SQLSTATE 23000 (which is shared with duplicate-key violations and so is unreliable on its own). |
| `public static string? ExtractConstraintDetail(DbUpdateException ex)` | `string?` | Returns a logging-oriented detail string such as the PostgreSQL constraint name or the provider message. |
| `public static (string? ConstraintName, string? TableName) ExtractConstraintIdentity(DbUpdateException ex)` | `(string?, string?)` | Returns the constraint name and the qualified table the violated constraint belongs to, parsed from the inner provider exception. Typed extraction first for PostgreSQL (`Npgsql.PostgresException.ConstraintName` / `TableName` / `SchemaName` via reflection so the package stays provider-agnostic; output is `"schema.table"` when both are present), then message-based parsing for SQL Server 2627 / 2601 / FK 547 (both `FOREIGN KEY` and parent-side `REFERENCE` constraint forms), SQLite `UNIQUE constraint failed: <Table>.<Column>` / `PRIMARY KEY` (constraint name is `null` because SQLite does not name the constraint), and MySQL `Duplicate entry '...' for key '<table>.<key>'`. Returns `(null, null)` on any unexpected exception so telemetry extraction never breaks the caller. Used by `SaveChangesResultAsync`, `SaveChangesWithRetryAsync`, and `TryInsertUniqueAsync` to populate `Error.Conflict.ConstraintName` / `ConstraintTableName`. |

### `TrellisPersistenceMappingException`

```csharp
public sealed class TrellisPersistenceMappingException : InvalidOperationException
```

| Name | Type | Description |
| --- | --- | --- |
| `ValueObjectType` | `Type` | Value object type that failed materialization. |
| `PersistedValue` | `object?` | Database value that could not be materialized. |
| `FactoryMethod` | `string` | Factory method name used during materialization. |
| `Detail` | `string` | Validation or mapping detail that explains the failure. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisPersistenceMappingException()` | — | Initializes an empty exception. |
| `public TrellisPersistenceMappingException(string message)` | — | Initializes the exception with a message. |
| `public TrellisPersistenceMappingException(string message, Exception innerException)` | — | Initializes the exception with a message and inner exception. |
| `public TrellisPersistenceMappingException(Type valueObjectType, object? persistedValue, string factoryMethod, string detail, Exception? innerException = null)` | — | Initializes the exception with full materialization context. |

### `TrellisScalarConverter<TModel, TProvider>`

```csharp
public class TrellisScalarConverter<TModel, TProvider> : ValueConverter<TModel, TProvider>
where TModel : class
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisScalarConverter()` | — | Builds expressions that persist `Value` and materialize via `TryCreate` or `TryFromName`; invalid persisted data throws `TrellisPersistenceMappingException`. |

### `OwnedEntityAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OwnedEntityAttribute : Attribute;
```

### `MaybeQueryInterceptor`

```csharp
public sealed class MaybeQueryInterceptor : IQueryExpressionInterceptor
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)` | `Expression` | Rewrites query expressions so natural `Maybe<T>` access translates to mapped storage members. Supported patterns inside `Where`/`Select`/`Specification.ToExpression()`: `o.X.HasValue`, `o.X.HasNoValue`, `o.X.Value`, `o.X.GetValueOrDefault(d)`, `o.X == Maybe<T>.None` / `o.X != Maybe<T>.None` (and converse operand order), `o.X == Maybe.From(value)` / `o.X != Maybe.From(value)` (and converse operand order), `o.X == default(Maybe<T>)`, and `o.X.HasValueWhere(t => ...predicate-body-on-t...)`. `HasValueWhere` requires an inline expression-bodied lambda; captured `Func<T,bool>` variables and method-group conversions fall through and EF Core reports the translation failure. See cookbook [Recipe 8](trellis-api-cookbook.md#recipe-8--ef-core-maybepropertymapping-for-nullable-value-objects) for the Specification walkthrough. |

### `ScalarValueQueryInterceptor`

```csharp
public sealed class ScalarValueQueryInterceptor : IQueryExpressionInterceptor
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)` | `Expression` | Rewrites scalar value object expressions so comparisons, ordering, and string/property access translate without explicit `.Value`. |

## Extension methods

### `DbContextOptionsBuilderExtensions`

```csharp
public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder) where TContext : DbContext
public static DbContextOptionsBuilder AddTrellisInterceptors(this DbContextOptionsBuilder optionsBuilder)
public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, TimeProvider? timeProvider) where TContext : DbContext
public static DbContextOptionsBuilder AddTrellisInterceptors(this DbContextOptionsBuilder optionsBuilder, TimeProvider? timeProvider)
```

### `ModelConfigurationBuilderExtensions`

```csharp
public static ModelConfigurationBuilder ApplyTrellisConventions(this ModelConfigurationBuilder configurationBuilder, params Assembly[] assemblies)
```

### `DbContextExtensions`

```csharp
public static Task<Result<int>> SaveChangesResultAsync(this DbContext context, CancellationToken cancellationToken = default)
public static Task<Result<int>> SaveChangesResultAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
public static Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, CancellationToken cancellationToken = default)
public static Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
```

### `DbContextRetryExtensions`

```csharp
public static Task<Result<Unit>> SaveChangesWithRetryAsync(
    this DbContext db,
    Func<DbUpdateException, bool> shouldRetry,
    Func<IReadOnlyList<EntityEntry>, int, CancellationToken, ValueTask<bool>> regenerate,
    int maxAttempts = 3,
    CancellationToken cancellationToken = default)
```

### `DbContextIdempotencyExtensions`

```csharp
public static Task<Result<TEntity>> TryInsertUniqueAsync<TEntity>(
    this DbContext context,
    TEntity entity,
    CancellationToken cancellationToken = default)
    where TEntity : class
```

### `QueryableExtensions`

```csharp
public static Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default) where T : class
public static Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class
public static Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default) where T : class
public static Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class
public static Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Error notFoundError, CancellationToken cancellationToken = default) where T : class
public static Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, Error notFoundError, CancellationToken cancellationToken = default) where T : class
public static IQueryable<T> Where<T>(this IQueryable<T> query, Specification<T> specification) where T : class
```

### `PaginationQueryableExtensions`

```csharp
public static Task<Result<Page<T>>> ToPageAsync<T, TKey>(
    this IQueryable<T> source,
    PageSize pageSize,
    Cursor? cursor,
    Expression<Func<T, TKey>> keySelector,
    string? cursorFieldName = null,
    CancellationToken cancellationToken = default)
    where T : class
    where TKey : notnull, IComparable<TKey>, IParsable<TKey>
```

### `MaybeQueryableExtensions`

See [`MaybeQueryableExtensions`](#maybequeryableextensions) above for the full type reference.

### `MaybeUpdateExtensions`

```csharp
public static UpdateSettersBuilder<TEntity> SetMaybeValue<TEntity, TInner>(this UpdateSettersBuilder<TEntity> updateSettersBuilder, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull
public static UpdateSettersBuilder<TEntity> SetMaybeNone<TEntity, TInner>(this UpdateSettersBuilder<TEntity> updateSettersBuilder, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull
```

### `MaybeEntityTypeBuilderExtensions`

```csharp
public static IndexBuilder<TEntity> HasTrellisIndex<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder, Expression<Func<TEntity, object?>> propertySelector) where TEntity : class
```

### `MaybeModelExtensions`

```csharp
public static IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this IModel model)
public static IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this DbContext dbContext)
public static string ToMaybeMappingDebugString(this IModel model)
public static string ToMaybeMappingDebugString(this DbContext dbContext)
```

## Internal types

- `AggregateETagConvention` is internal. `ApplyTrellisConventions` uses it to mark `IAggregate.ETag` as a concurrency token and set `HasMaxLength(50)`.
- `ScalarValueObjectPropertyConvention` is internal. It maps **constructor-bound** (get-only) Trellis value-object properties — both scalar value objects and symbolic enum (`RequiredEnum<T>`) value objects — the idiomatic aggregate/entity shape where the value object is supplied only through the constructor (e.g. `public CustomerId CustomerId { get; }` set in the constructor). EF Core discovers a *settable* value-object property through its setter, but a get-only one is not auto-discovered, so EF Core's constructor binding fails with `Cannot bind '<param>' in '<Type>(...)'`. This convention adds the matching property at entity-type-added time so the registered scalar converter applies and the constructor parameter binds — the automated equivalent of an explicit `builder.Property(x => x.CustomerId).HasConversion(...)` in `OnModelCreating`.
- `AggregateETagInterceptor` is internal. `AddTrellisInterceptors()` uses it to generate new `Guid.NewGuid().ToString("N")` ETags for `Added` and `Modified` aggregates, promote `Unchanged` aggregate roots when loaded dependents are `Added`, `Modified`, or `Deleted`, and sync `OriginalValue` after save when `acceptAllChangesOnSuccess` is `false`.
- `AggregateTransientPropertyConvention` is internal. It explicitly ignores `IAggregate.IsChanged`.
- `MaybeConvention` is internal. It ignores the `Maybe<T>` CLR property, requires the generated `_camelCase` storage member, maps scalar `Maybe<T>` properties to nullable backing-field columns, and maps `Maybe<T>` where `T` is already owned as an optional ownership navigation.
- `CompositeValueObjectConvention` is internal. It only registers composite value objects discovered in the assemblies passed to `ApplyTrellisConventions` (plus built-in Trellis primitives scanning for scalar value objects). For **required** composite owned types, the inner columns are mapped onto the parent table using the owner navigation name as the prefix (e.g., `ShippingAddress_City`). EF Core itself produces those names for owned-type table-splitting and explicit `OwnsOne` (it prepends `{navigation}_` per ownership level — see `RelationalPropertyExtensions.GetDefaultColumnName(property, storeObject)`), and applies the prefix automatically to any owned property with no explicit column-name annotation. The convention therefore defers to EF for plain scalars and only **removes** the bare name that `MaybeConvention` stamps on inner `Maybe<T>` scalar backing fields (which would otherwise bypass the prefix), and hands nested `Money` the chained prefix for `MoneyConvention` — keeping two same-type composites on one entity (e.g., `BillingAddress` + `ShippingAddress`) from colliding. This applies only to composites that **table-split into their owner**: a composite owned in its **own** table (an owned collection, an explicit `ToTable`, or the separate-table fallback below) is left untouched, because no navigation prefix applies there and `MaybeConvention`'s clean `{PropertyName}` is already the correct column (removing it would leak the raw `_camelCase` backing-field name). Removal uses the convention configuration source, so user-configured column names are preserved. For `Maybe<T>` composite owned types the navigation is a backing field (EF would prefix with the field name), so the convention instead sets the clean `{PropertyName}_` prefix explicitly and marks the columns nullable — but only when table-splitting is valid; it switches to a separate table named `{OwnerTypeName}_{PropertyName}` when nested owned navigations exist **or** when the owned type contains non-nullable value-type properties.
- `MoneyConvention` is internal. It registers `Money` as an owned type, names the amount column `{PropertyName}`, names the currency column `{PropertyName}Currency`, sets `decimal(18,3)` precision/scale for `Amount`, and handles optional `Maybe<Money>` columns through the annotation written by `MaybeConvention`. A required `Money` nested inside a composite value object chains the owner prefix (e.g., `Destination_DeliveryFee` / `Destination_DeliveryFeeCurrency`).
- `ValueObjectMappingGuardConvention` is internal. Runs after `MaybeConvention` and `MoneyConvention` during model finalization and throws an actionable `InvalidOperationException` when an entity still has a scalar property whose CLR type is `Money` or `Maybe<T>` — the typical cause is an explicit `builder.Property(x => x.SomeMoneyOrMaybe)` call in `OnModelCreating` that bypasses the auto-mapping conventions. Replaces EF Core's cryptic "*property could not be mapped because the database provider does not support this type*" error with a message that names the offending entity + property and points to the correct pattern (do nothing for `Money`; declare `partial Maybe<T>` for Maybe).
- `OwnedCollectionKeyConvention` is internal. For an owned collection (`OwnsMany`) whose child declares its **own** primary key — a domain-assigned key such as `Guid Id` or `long SequenceNumber`, as opposed to the surrogate key EF Core synthesizes for a keyless owned type — it marks that key `ValueGenerated.Never` so the application-supplied value is persisted as-is. It applies the setting while the model is built (when ownership is established and when the child's primary key changes) rather than at finalization, so provider conventions — notably SQL Server's IDENTITY strategy — observe it before deciding store generation. This keeps an integer key from becoming an `IDENTITY` column (whose explicit insert throws `SqlException 544`) and stops a non-default key on a child added to an already-loaded parent from being mis-tracked as an `UPDATE` (zero rows ⇒ a spurious `DbUpdateConcurrencyException`). EF Core's synthesized shadow surrogate keys and the foreign-key columns linking a child to its owner are left untouched, and an explicit `ValueGeneratedOnAdd()` in `OnModelCreating` opts out (the setting uses the convention configuration source).
- `MaybePartialPropertyGenerator`, `OwnedEntityGenerator`, and `ApplyTrellisConventionsForGenerator` are compiler-time helpers shipped in the `Trellis.EntityFrameworkCore.Generator.dll` assembly, which is bundled inside `Trellis.EntityFrameworkCore.nupkg` at `analyzers/dotnet/cs/` — there is no separate `Trellis.EntityFrameworkCore.Generator` NuGet package. `TRLS035` is reported only for non-partial auto-properties of type `Maybe<T>` whose containing type is partial. `TRLS036`, `TRLS037`, and `TRLS038` come from `[OwnedEntity]` validation and generation. (These IDs were `TRLSGEN100`–`TRLSGEN103` in v1; the unified `TRLS###` namespace is canonical — see `TrellisDiagnosticIds`.)

## Behavioral notes

### Source-generator state

`Trellis.EntityFrameworkCore` ships with a Roslyn source generator (`Trellis.EntityFrameworkCore.Generator.dll`, bundled at `analyzers/dotnet/cs/`). The current generator emits:

- `Maybe<T>` partial-property bodies with private `_camelCase` backing fields that EF Core can map through reflection-free conventions.
- `[OwnedEntity]` validation/generation diagnostics (`TRLS035`–`TRLS038`).
- `GeneratedTrellisConventions.ApplyTrellisConventionsFor<TContext>()`, which calls `AddTrellisScalarConverter<TClr, TProvider>` and `AddTrellisCoreConventions(...)` for value-object types reachable from the current compilation's accessible `DbSet<T>` roots.

`ApplyTrellisConventionsFor<TContext>()` is the reflection-free convention path. `ApplyTrellisConventions(typeof(SomeRootType).Assembly)` remains the broadest runtime scan and is still the right fallback when the context is private, generic, abstract, or otherwise skipped by source generation. The generated path removes Trellis' assembly scan and `MakeGenericType` converter construction; it does not make EF Core itself NativeAOT-supported.

### `Maybe<T>` storage, owned types, and migrations

`MaybeConvention` and `CompositeValueObjectConvention` together control how `Maybe<T>` properties are stored. Knowing the rules helps when authoring EF migrations:

- **Scalar `Maybe<T>` (e.g., `Maybe<DateTimeOffset>`, `Maybe<EmailAddress>`).** The CLR `Maybe<T>` property is ignored; the source-generated `_camelCase` backing field is mapped as a **nullable column** named after the property (or the explicit `HasColumnName(...)` if configured). Migrations show this as a single nullable column. Use `MaybeUpdateExtensions.SetMaybeValue` / `SetMaybeNone` inside `ExecuteUpdate` and `MaybeQueryableExtensions.WhereHasValue` / `WhereNone` / etc. for predicates — these rewrite to the mapped storage member so the SQL targets the actual column.
- **Array- and reference-typed `Maybe<T>` (e.g., `Maybe<byte[]>`).** Inner array and reference types map the same way as scalar `Maybe<T>`: the generated `_camelCase` backing field becomes the mapped, nullable persisted property (for `Maybe<byte[]>`, a nullable `varbinary`/`BLOB` per provider), with presence/absence expressed by the column being non-null/null. This is subject to the provider supporting the underlying type (arrays in particular). Use the same `WhereHasValue`/`WhereNone` and `SetMaybeValue`/`SetMaybeNone` helpers — do not hand-map the `Maybe<T>` CLR property.
- **Composite `Maybe<T>` where `T` is an `[OwnedEntity]`/composite `ValueObject`.** `CompositeValueObjectConvention` decides between two storage shapes:
  - **Table-splitting (default).** When the owned type contains only nullable value-type properties (or reference properties) and has no nested owned navigations, every column is mapped onto the parent table as nullable columns. `Maybe<T>.None` ⇒ all columns `NULL`.
  - **Separate table.** When the owned type contains **non-nullable value-type properties** or **nested owned navigations**, `Maybe<T>` switches to a separate table named `{OwnerTypeName}_{PropertyName}` to preserve nullability semantics. Migrations will produce a child table with FK to the parent. Switching the inner shape of an owned type between these two regimes therefore generates a non-trivial migration (column drop + table create, or vice-versa) — review the generated migration and provide custom `Up`/`Down` data-copy steps when production data exists.
- **`Maybe<Money>` specifically.** `MoneyConvention` honors the nullability annotation written by `MaybeConvention` so the amount/currency columns are emitted as nullable when the property is `Maybe<Money>`.
- **Indexes — `HasTrellisIndex` vs `HasIndex`.** Use `MaybeEntityTypeBuilderExtensions.HasTrellisIndex(x => new { x.SubmittedAt, ... })` for **any** index selector that includes a `Maybe<T>` property — it resolves each `Maybe<T>` selector to the mapped `_camelCase` storage member before calling EF's `HasIndex`, so the index lands on the actual column instead of the unmapped CLR property (and avoids `TRLS016`). Plain `HasIndex` is correct only when every member in the selector is an ordinary mapped property; a selector that mixes ordinary and `Maybe<T>` members must use `HasTrellisIndex`.
- **Inspection.** Call `db.GetMaybePropertyMappings()` (or `db.ToMaybeMappingDebugString()`) at startup to verify each `Maybe<T>` property resolved to the expected backing field, column, and nullability before generating a migration.



### Configure conventions, interceptors, and `Maybe<T>` querying

```csharp
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

[OwnedEntity]
public partial class Address : ValueObject
{
    public string Street { get; private set; } = null!;
    public string City   { get; private set; } = null!;

    private Address(string street, string city)
    {
        Street = street;
        City   = city;
    }

    public static Result<Address> TryCreate(string street, string city, string? fieldName = null)
    {
        var violations = new List<FieldViolation>(2);
        var prefix = string.IsNullOrWhiteSpace(fieldName) ? null : fieldName;
        if (string.IsNullOrWhiteSpace(street))
            violations.Add(new FieldViolation(Pointer(prefix, "street"), "required") { Detail = "Street is required." });
        if (string.IsNullOrWhiteSpace(city))
            violations.Add(new FieldViolation(Pointer(prefix, "city"), "required") { Detail = "City is required." });
        return violations.Count > 0
            ? Result.Fail<Address>(new Error.InvalidInput(EquatableArray.Create(violations.ToArray())))
            : Result.Ok(new Address(street.Trim(), city.Trim()));
    }

    private static InputPointer Pointer(string? owner, string leaf) =>
        owner is null ? InputPointer.ForProperty(leaf) : new InputPointer($"/{owner}/{leaf}");

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
    }
}

public sealed class CustomerId : ScalarValueObject<CustomerId, Guid>, IScalarValue<CustomerId, Guid>
{
    private CustomerId(Guid value) : base(value) { }

    public static Result<CustomerId> TryCreate(Guid value, string? fieldName = null) =>
        value == Guid.Empty
            ? Result.Fail<CustomerId>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "customerId"), "required") { Detail = "Customer ID is required." })))
            : Result.Ok(new CustomerId(value));

    public static Result<CustomerId> TryCreate(string? value, string? fieldName = null) =>
        Guid.TryParse(value, out var guid)
            ? TryCreate(guid, fieldName)
            : Result.Fail<CustomerId>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "customerId"), "must_be_guid") { Detail = "Customer ID must be a GUID." })));
}

public partial class Customer : Aggregate<CustomerId>
{
    public string Name { get; private set; }
    public Address ShippingAddress { get; private set; }
    public partial Maybe<DateTimeOffset> SubmittedAt { get; set; }

    private Customer(CustomerId id, string name, Address shippingAddress) : base(id)
    {
        Name = name;
        ShippingAddress = shippingAddress;
    }

    public static Customer Create(string name, Address shippingAddress) =>
        new(CustomerId.Create(Guid.NewGuid()), name, shippingAddress);
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(Customer).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Customer>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.HasTrellisIndex(x => new { x.Name, x.SubmittedAt });
        });
}

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite("Data Source=customers.db")
    .AddTrellisInterceptors()
    .Options;

await using var db = new AppDbContext(options);

var result = await db.Customers.FirstOrDefaultResultAsync(
    x => x.Name == "missing",
    new Error.NotFound(ResourceRef.For("Customer")) { Detail = "Customer not found." });

var submittedCustomers = await db.Customers
    .WhereHasValue(x => x.SubmittedAt)
    .OrderByMaybe(x => x.SubmittedAt)
    .ToListAsync();
```

### Inspect `Maybe<T>` mappings

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore;

IReadOnlyList<MaybePropertyMapping> mappings = db.GetMaybePropertyMappings();
string debug = db.ToMaybeMappingDebugString();
```

### Provider-specific column mapping

The Acl layer owns storage-provider compatibility for every persisted column — **including columns Trellis writes via interceptors**, such as `CreatedAt`, `LastModified`, `ETag`, and any property inherited from `Aggregate<TId>` or `Entity<TId>`. `ApplyTrellisConventions(...)` (and its source-generated equivalent `ApplyTrellisConventionsFor<TContext>()`) defines the domain-to-EF shape for Trellis-known types, but it does not make every CLR type natively queryable, sortable, or projectable on every storage provider.

When a provider rejects a translated operation on one of these properties — for example a runtime translation exception of the shape *"`<Provider>` does not support expressions of type 'X' in ORDER BY clauses"*, or a comparison/predicate translation failure — the layer-correct fix is to register a `ValueConverter` on the affected property in `DbContext.OnModelCreating(...)` **after** `base.OnModelCreating(modelBuilder)`. Trellis stays provider-agnostic on purpose; provider quirks are absorbed in the Acl.

Manual `Property(...).HasConversion(...)` is **discouraged** when it duplicates Trellis-supported value-object conventions (those are handled by `ApplyTrellisConventions`), but **appropriate** when it adapts an already-mapped property to a provider-specific storage or query capability that Trellis intentionally leaves to the Acl. Choose a converter target that preserves the property's intended semantic and is sortable/comparable on the provider — for instant-only audit columns like `CreatedAt` / `LastModified`, a UTC-normalized scalar (ISO-8601 text or Unix-epoch `long`) preserves the instant and sorts correctly across rows written from different timezones; for columns where the original `DateTimeOffset.Offset` carries semantic meaning, that normalization is the wrong choice and you should pick a different storage shape (a side column for the offset, or a provider with native `datetimeoffset` support).

```csharp
using System;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

// Example: SQLite cannot ORDER BY DateTimeOffset. Convert the framework-written
// audit columns to UTC ISO-8601 TEXT so server-side ordering on CreatedAt /
// LastModified works without materializing the result set first.
//
// NOTE: This converter normalizes every value to UTC on write. Round-trips
// preserve the instant but read back with Offset == TimeSpan.Zero, not the
// original offset. That trade-off is intentional for audit columns written by
// EntityTimestampInterceptor (which always writes a UTC DateTimeOffset via
// TimeProvider.GetUtcNow()) and is required for correct cross-row ordering
// when writes may come from clients in different timezones. Do not reuse this
// converter for columns where the original DateTimeOffset.Offset is
// semantically required.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var dtoConverter = new ValueConverter<DateTimeOffset, string>(
            v => v.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var name in new[] { "CreatedAt", "LastModified" })
            {
                var property = entityType.FindProperty(name);
                if (property?.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(dtoConverter);
            }
        }
    }
}
```

The same pattern applies to other CLR-type / provider mismatches — for example `decimal` on a provider that only supports `double`, or a value object on a document store that cannot project nested types. Identify a sortable/comparable scalar that preserves the value, register the converter in the Acl, and keep the repository query server-side rather than falling back to `ToListAsync()` + in-memory ordering.

The same Acl boundary applies to [`PaginationQueryableExtensions.ToPageAsync`](#paginationqueryableextensions). The helper emits `Expression.GreaterThan` for numeric and `DateTime`/`DateTimeOffset` keys (which translate natively on every common provider) and routes through `IComparable<TKey>.CompareTo` for `Guid` and `string` keys (which SQL Server translates to native `ORDER BY` / `>` semantics, and which other providers translate to their dialect equivalents). If a provider rejects the `CompareTo` shape for a particular key type, register a `ValueConverter` on the Id column that converts to a sortable scalar (e.g. `Guid` → `string` byte-canonical form on Postgres) so the seek predicate translates correctly.

### Provider-specific behavior: owned collections on SQLite

> **Note:** This documents a stock EF Core + SQLite interaction, **not** a Trellis behavior. It is included here only because Trellis projects commonly test against SQLite while deploying to SQL Server, so the symptom can surface in a Trellis test suite.

An owned collection (`OwnsMany`) whose per-row key is a **store-generated integer** — the default when the owned type is keyless and EF Core synthesizes a key for it — fails to insert on **SQLite** with:

```text
Microsoft.Data.Sqlite.SqliteException: SQLite Error 19: 'NOT NULL constraint failed: <OwnedTable>.Id'
```

**Why.** EF Core identifies each owned-collection row by a *composite* primary key `(<OwnerForeignKey>, <Id>)`, where the synthesized `<Id>` is an `int` marked `ValueGenerated.OnAdd`. On **SQL Server** that column is emitted as `int NOT NULL IDENTITY`, which auto-increments even as part of a composite key, so inserts succeed. **SQLite** only auto-increments a *single-column* `INTEGER PRIMARY KEY` (the rowid alias); an integer that is merely *part of* a composite primary key is never auto-populated, so it stays `NULL` and the `NOT NULL` constraint fails. The same failure reproduces with a plain non-Trellis POCO owned collection and no Trellis conventions registered — confirming it is provider behavior, not a framework defect — and **SQL Server (the provider the production schema targets) is unaffected.**

**Fix.** Give the owned collection an explicit **client-generated** key so row identity does not depend on database identity. A `Guid` key is generated client-side by EF Core on every provider (sequential on SQL Server, random on SQLite), so the INSERT carries a value on SQLite and SQL Server alike:

```csharp
builder.OwnsMany(o => o.Addresses, a =>
{
    // Client-generated Guid key: no dependency on database IDENTITY/rowid, so the
    // owned-collection row inserts on SQLite as well as on SQL Server.
    a.Property<Guid>("Id").ValueGeneratedOnAdd();
    a.HasKey("Id");
});
```

Alternatively, run integration tests against the same provider you deploy to (for example SQL Server LocalDB), which sidesteps the SQLite-only rowid limitation entirely.

### Owned collections with a domain-assigned key

The SQLite caveat above concerns a **keyless** owned type, where EF Core synthesizes a store-generated surrogate key. When the owned type instead declares its **own** primary key that the domain assigns — for example `public Guid Id { get; private set; }` or `public long SequenceNumber { get; private set; }` on an `OwnsMany` child — `OwnedCollectionKeyConvention` (registered by `ApplyTrellisConventions`) automatically marks that key `ValueGenerated.Never`. EF Core otherwise treats every key as store-generated, which is wrong for a key the application supplies and breaks persistence in two provider-specific ways:

- On **SQL Server**, an integer key is emitted as an `IDENTITY` column, so saving the domain-assigned value throws `SqlException 544` ("Cannot insert explicit value for identity column … when IDENTITY_INSERT is set to OFF").
- On **any provider**, a child carrying a non-default key that is added to an already-loaded parent is read as an existing row, so EF Core emits `UPDATE … WHERE Id = @id`, matches zero rows, and throws a spurious `DbUpdateConcurrencyException` (which `SaveChangesResultAsync` maps to `Error.Conflict` ⇒ HTTP 409).

Marking the key `ValueGenerated.Never` tells EF Core the application owns the value, fixing both — so you do **not** need to call `ValueGeneratedNever()` by hand for a domain-keyed owned collection. This default applies to **any** key the owned child declares as a CLR (or field-backed) property — including an integer key you might otherwise expect EF Core to generate — so on the rare owned collection whose row key really is store-generated, opt out with an explicit `ValueGeneratedOnAdd()` (or `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]`) in `OnModelCreating`. EF Core's synthesized shadow surrogate key for a keyless owned type is left untouched and keeps store generation.

## Cross-references

- [Trellis DDD primitives in `Trellis.Core` (API reference)](trellis-api-core.md#domain-driven-design) — `IEntity`, `IAggregate`, `Aggregate<TId>`, `Entity<TId>`, `ValueObject`, `ScalarValueObject<TSelf, T>`, and `Specification<T>`
- [Trellis.Core API reference](trellis-api-core.md#patterns-index) — `Result`, `Result<T>`, `Maybe<T>`, `Error`, `IScalarValue<TSelf, TPrimitive>`, and `EntityTagValue`
- [Trellis.Primitives API reference](trellis-api-primitives.md#built-in-primitives-table) — `Money`, `RequiredEnum<T>`, and built-in value objects commonly scanned by `ApplyTrellisConventions`
