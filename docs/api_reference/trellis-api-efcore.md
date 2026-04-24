# Trellis.EntityFrameworkCore

**Package:** `Trellis.EntityFrameworkCore` (since Phase 2 of the v2 redesign, this single package also bundles the `Trellis.EntityFrameworkCore.Generator.dll` source generator at `analyzers/dotnet/cs/` — installing `Trellis.EntityFrameworkCore` attaches the `Maybe<T>` / `[OwnedEntity]` generator automatically; there is no separate `Trellis.EntityFrameworkCore.Generator` NuGet package).
**Namespace:** `Trellis.EntityFrameworkCore`  
**Purpose:** EF Core conventions, interceptors, converters, and query/update helpers for Trellis aggregates, value objects, and `Maybe<T>`.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

## Types

### `DbContextOptionsBuilderExtensions`

```csharp
public static class DbContextOptionsBuilderExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder) where TContext : DbContext` | `DbContextOptionsBuilder<TContext>` | Registers singleton `MaybeQueryInterceptor`, `ScalarValueQueryInterceptor`, internal `AggregateETagInterceptor`, and singleton `EntityTimestampInterceptor`. |
| `public static DbContextOptionsBuilder AddTrellisInterceptors(this DbContextOptionsBuilder optionsBuilder)` | `DbContextOptionsBuilder` | Non-generic overload for the same singleton interceptor set. |
| `public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, TimeProvider? timeProvider) where TContext : DbContext` | `DbContextOptionsBuilder<TContext>` | Registers the same interceptor set, but creates a **new** `EntityTimestampInterceptor(timeProvider)` for this call. |
| `public static DbContextOptionsBuilder AddTrellisInterceptors(this DbContextOptionsBuilder optionsBuilder, TimeProvider? timeProvider)` | `DbContextOptionsBuilder` | Non-generic overload that creates a new `EntityTimestampInterceptor(timeProvider)` for this call. |

### `ModelConfigurationBuilderExtensions`

```csharp
public static class ModelConfigurationBuilderExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static ModelConfigurationBuilder ApplyTrellisConventions(this ModelConfigurationBuilder configurationBuilder, params Assembly[] assemblies)` | `ModelConfigurationBuilder` | Scans the supplied assemblies plus `Trellis.Primitives`, registers scalar converters, collects composite value objects from those assemblies only, and adds internal conventions for `Maybe<T>`, composite value objects, `Money`, aggregate ETags, and transient aggregate properties. |
| `public static ModelConfigurationBuilder ApplyTrellisConventionsCore(this ModelConfigurationBuilder configurationBuilder, IEnumerable<(Type ClrType, Type ProviderType)> scalars, IEnumerable<Type> composites)` | `ModelConfigurationBuilder` | Low-level helper used by the reflection-based `ApplyTrellisConventions` overload. Registers the supplied scalar converters via `Type.MakeGenericType` (not AOT/trim-friendly), then delegates to `AddTrellisCoreConventions`. |
| `public static ModelConfigurationBuilder AddTrellisScalarConverter<TClr, TProvider>(this ModelConfigurationBuilder configurationBuilder) where TClr : class where TProvider : notnull` | `ModelConfigurationBuilder` | AOT/trim-friendly strongly-typed helper that registers `TrellisScalarConverter<TClr, TProvider>` for `TClr` properties. Emitted by the source generator; no `MakeGenericType` at runtime. |
| `public static ModelConfigurationBuilder AddTrellisCoreConventions(this ModelConfigurationBuilder configurationBuilder, IEnumerable<Type> composites)` | `ModelConfigurationBuilder` | Adds the fixed Trellis conventions (`MaybeConvention`, `CompositeValueObjectConvention`, `MoneyConvention`, `AggregateETagConvention`, `AggregateTransientPropertyConvention`). AOT/trim-friendly: `composites` is an array of pre-closed `Type` tokens; no runtime reflection. |

### `GeneratedTrellisConventions` (source-generator note)

The Trellis EF Core package itself does **not** ship a `GeneratedTrellisConventions` class or an `ApplyTrellisConventionsFor<TContext>` extension. The package source contains only:

- `ApplyTrellisConventions(this ModelConfigurationBuilder, params Assembly[])` — reflection-based assembly scanner (above).
- `ApplyTrellisConventionsCore(this ModelConfigurationBuilder, IEnumerable<(Type ClrType, Type ProviderType)>, IEnumerable<Type>)` — low-level helper used by `ApplyTrellisConventions` and intended as the integration target for a future source-generated entry point.
- `AddTrellisScalarConverter<TClr, TProvider>` and `AddTrellisCoreConventions(IEnumerable<Type>)` — AOT/trim-friendly building blocks the planned generator will call.

Today, consumers should call `ApplyTrellisConventions(typeof(SomeRootType).Assembly)` from `ConfigureConventions`. A source-generated `ApplyTrellisConventionsFor<TContext>` extension (emitted into the consuming project) is referenced in XML doc comments inside `ModelConfigurationBuilderExtensions.cs` as a planned successor for AOT/trimming scenarios but is not currently emitted by the shipped generator assembly.

### `DbContextExtensions`

```csharp
public static class DbContextExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Result<int>> SaveChangesResultAsync(this DbContext context, CancellationToken cancellationToken = default)` | `Task<Result<int>>` | Convenience overload for `SaveChangesResultAsync(context, true, cancellationToken)`. |
| `public static Task<Result<int>> SaveChangesResultAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)` | `Task<Result<int>>` | Wraps `SaveChangesAsync`; maps `DbUpdateConcurrencyException` to `Error.Conflict("concurrency.modified")`, duplicate-key `DbUpdateException` to `Error.Conflict("duplicate.key")`, and foreign-key `DbUpdateException` to `Error.Conflict("referential.integrity")`. |
| `public static Task<Result> SaveChangesResultUnitAsync(this DbContext context, CancellationToken cancellationToken = default)` | `Task<Result>` | Saves changes and discards the row count. |
| `public static Task<Result> SaveChangesResultUnitAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)` | `Task<Result>` | Saves changes with explicit `acceptAllChangesOnSuccess`. |

### `QueryableExtensions`

```csharp
public static class QueryableExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default) where T : class` | `Task<Maybe<T>>` | Returns the first match or `Maybe<T>.None`. |
| `public static Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class` | `Task<Maybe<T>>` | Returns the first predicate match or `Maybe<T>.None`. |
| `public static Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default) where T : class` | `Task<Maybe<T>>` | Returns the single match or `Maybe<T>.None`; throws if more than one element matches. |
| `public static Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class` | `Task<Maybe<T>>` | Returns the single predicate match or `Maybe<T>.None`; throws if more than one element matches. |
| `public static Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Error notFoundError, CancellationToken cancellationToken = default) where T : class` | `Task<Result<T>>` | Returns the first match or **the exact `notFoundError` supplied by the caller**. |
| `public static Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, Error notFoundError, CancellationToken cancellationToken = default) where T : class` | `Task<Result<T>>` | Returns the first predicate match or **the exact `notFoundError` supplied by the caller**. |
| `public static IQueryable<T> Where<T>(this IQueryable<T> query, Specification<T> specification) where T : class` | `IQueryable<T>` | Applies a Trellis specification expression to the query. |

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
| `public virtual Task<Result> RemoveByIdAsync(TId id, CancellationToken ct = default)` | `Task<Result>` | Looks up by ID via `DbSet.FindAsync` (avoids Include graphs) and stages for deletion. Returns not-found if absent. |

#### Virtual Hooks

| Signature | Description |
| --- | --- |
| `protected virtual IQueryable<TAggregate> BuildFindByIdQuery()` | Override to add `.Include()` or filters to the find-by-ID query. Defaults to `DbSet`. |
| `protected virtual IQueryable<TAggregate> BuildQueryBase()` | Override to add `.Include()` or filters to specification queries. Defaults to `DbSet.AsNoTracking()`. |

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
    .ToResult(new Error.NotFound(new ResourceRef("Order")) { Detail = "Order not found." })
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
| `Task<Result> CommitAsync(CancellationToken ct = default)` | `Task<Result>` | Persists all staged changes. Surfaces concurrency, duplicate-key, and FK errors as `Error` instead of exceptions. |

### `EfUnitOfWork<TContext>`

```csharp
public class EfUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext
```

EF Core implementation of `IUnitOfWork`. Delegates to `DbContextExtensions.SaveChangesResultUnitAsync` which maps `DbUpdateConcurrencyException` → `Error.Conflict("concurrency.modified")`, duplicate-key → `Error.Conflict("duplicate.key")`, and FK violations → `Error.Conflict("referential.integrity")`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public EfUnitOfWork(TContext context)` | — | Captures the resolved `TContext` instance. Registered as scoped by `AddTrellisUnitOfWork<TContext>()`. |
| `public Task<Result> CommitAsync(CancellationToken cancellationToken = default)` | `Task<Result>` | Calls `context.SaveChangesResultUnitAsync(cancellationToken)`. |

### `TransactionalCommandBehavior<TMessage, TResponse>`

```csharp
public sealed class TransactionalCommandBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult, IFailureFactory<TResponse>
```

Pipeline behavior that auto-commits staged changes after a successful command handler. Only applies to `ICommand<TResponse>` messages — queries are skipped at the type-constraint level and incur no overhead. If the handler returns a failure, no commit occurs and staged changes are discarded with the `DbContext`. EF Core wraps each `SaveChanges` call in an implicit transaction, so all staged changes within a single handler commit atomically.

> **Important:** This behavior is **not** registered by `Trellis.Mediator.ServiceCollectionExtensions.AddTrellisBehaviors()`. Consumers of `Trellis.EntityFrameworkCore` must register it explicitly via `AddTrellisUnitOfWork<TContext>()` (see below) **after** `AddTrellisBehaviors()` so it lands innermost — closest to the handler — and commit failures remain visible to outer logging/tracing/exception behaviors.

| Signature | Returns | Description |
| --- | --- | --- |
| `public TransactionalCommandBehavior(IUnitOfWork unitOfWork)` | — | Captures the scoped `IUnitOfWork` resolved alongside the handler. |
| `public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)` | `ValueTask<TResponse>` | Awaits the inner handler. On success, calls `unitOfWork.CommitAsync(cancellationToken)`; if the commit reports an `Error`, returns `TResponse.CreateFailure(error)`. On handler failure, returns the failure as-is without committing. |

### `UnitOfWorkServiceCollectionExtensions`

```csharp
public static class UnitOfWorkServiceCollectionExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellisUnitOfWork<TContext>(this IServiceCollection services) where TContext : DbContext` | `IServiceCollection` | Registers `EfUnitOfWork<TContext>` as `IUnitOfWork` and adds the `TransactionalCommandBehavior` pipeline behavior. The behavior is inserted after the last existing `IPipelineBehavior<,>` registration (innermost position). Call this **after** `AddTrellisBehaviors()` so that commit failures are visible to outer behaviors (logging, tracing). |
| `public static IServiceCollection AddTrellisUnitOfWorkWithoutBehavior<TContext>(this IServiceCollection services) where TContext : DbContext` | `IServiceCollection` | Registers `EfUnitOfWork<TContext>` without the pipeline behavior. Use for manual commit control or non-Mediator scenarios. |

### `EntityTimestampInterceptor`

```csharp
public sealed class EntityTimestampInterceptor : SaveChangesInterceptor
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public EntityTimestampInterceptor(TimeProvider? timeProvider = null)` | — | Uses the supplied `TimeProvider`, or `TimeProvider.System` when `null`. |
| `public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)` | `InterceptionResult<int>` | Sets `CreatedAt` and `LastModified` for added entities, sets `LastModified` for modified entities, and also updates `LastModified` on unchanged aggregate roots when loaded dependents are added, modified, or deleted. |
| `public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)` | `ValueTask<InterceptionResult<int>>` | Async equivalent of `SavingChanges`; includes unchanged aggregate-root promotion when loaded dependents change. |

### `MaybeQueryableExtensions`

```csharp
public static class MaybeQueryableExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

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

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static UpdateSettersBuilder<TEntity> SetMaybeValue<TEntity, TInner>(this UpdateSettersBuilder<TEntity> updateSettersBuilder, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull` | `UpdateSettersBuilder<TEntity>` | Sets a mapped scalar `Maybe<TInner>` property inside `ExecuteUpdate`; throws for composite owned types. |
| `public static UpdateSettersBuilder<TEntity> SetMaybeNone<TEntity, TInner>(this UpdateSettersBuilder<TEntity> updateSettersBuilder, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull` | `UpdateSettersBuilder<TEntity>` | Clears a mapped scalar `Maybe<TInner>` property inside `ExecuteUpdate`; throws for composite owned types. |

### `MaybeEntityTypeBuilderExtensions`

```csharp
public static class MaybeEntityTypeBuilderExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IndexBuilder<TEntity> HasTrellisIndex<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder, Expression<Func<TEntity, object?>> propertySelector) where TEntity : class` | `IndexBuilder<TEntity>` | Creates an index using CLR selectors and resolves any `Maybe<T>` selectors to the actual generated storage-member mapping. |

### `MaybeModelExtensions`

```csharp
public static class MaybeModelExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

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

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static bool IsDuplicateKey(DbUpdateException ex)` | `bool` | Detects duplicate-key violations across SQL Server, PostgreSQL, SQLite, and generic message-based fallbacks. |
| `public static bool IsForeignKeyViolation(DbUpdateException ex)` | `bool` | Detects foreign-key violations across SQL Server, PostgreSQL, SQLite, and generic message-based fallbacks. |
| `public static string? ExtractConstraintDetail(DbUpdateException ex)` | `string?` | Returns a logging-oriented detail string such as the PostgreSQL constraint name or the provider message. |

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

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisScalarConverter()` | — | Builds expressions that persist `Value` and materialize via `TryCreate` or `TryFromName`; invalid persisted data throws `TrellisPersistenceMappingException`. |

### `OwnedEntityAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OwnedEntityAttribute : Attribute;
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No methods. |

### `MaybeQueryInterceptor`

```csharp
public sealed class MaybeQueryInterceptor : IQueryExpressionInterceptor
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)` | `Expression` | Rewrites query expressions so natural `Maybe<T>` access translates to mapped storage members. |

### `ScalarValueQueryInterceptor`

```csharp
public sealed class ScalarValueQueryInterceptor : IQueryExpressionInterceptor
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

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
public static Task<Result> SaveChangesResultUnitAsync(this DbContext context, CancellationToken cancellationToken = default)
public static Task<Result> SaveChangesResultUnitAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
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

### `MaybeQueryableExtensions`

```csharp
public static IQueryable<TEntity> WhereNone<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull
public static IQueryable<TEntity> WhereHasValue<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull
public static IQueryable<TEntity> WhereEquals<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull
public static IQueryable<TEntity> WhereLessThan<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull, IComparable<TInner>
public static IQueryable<TEntity> WhereLessThanOrEqual<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull, IComparable<TInner>
public static IQueryable<TEntity> WhereGreaterThan<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull, IComparable<TInner>
public static IQueryable<TEntity> WhereGreaterThanOrEqual<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector, TInner value) where TEntity : class where TInner : notnull, IComparable<TInner>
public static IOrderedQueryable<TEntity> OrderByMaybe<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull
public static IOrderedQueryable<TEntity> OrderByMaybeDescending<TEntity, TInner>(this IQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull
public static IOrderedQueryable<TEntity> ThenByMaybe<TEntity, TInner>(this IOrderedQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull
public static IOrderedQueryable<TEntity> ThenByMaybeDescending<TEntity, TInner>(this IOrderedQueryable<TEntity> source, Expression<Func<TEntity, Maybe<TInner>>> propertySelector) where TEntity : class where TInner : notnull
```

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
- `AggregateETagInterceptor` is internal. `AddTrellisInterceptors()` uses it to generate new `Guid.NewGuid().ToString("N")` ETags for `Added` and `Modified` aggregates, promote `Unchanged` aggregate roots when loaded dependents are `Added`, `Modified`, or `Deleted`, and sync `OriginalValue` after save when `acceptAllChangesOnSuccess` is `false`.
- `AggregateTransientPropertyConvention` is internal. It explicitly ignores `IAggregate.IsChanged`.
- `MaybeConvention` is internal. It ignores the `Maybe<T>` CLR property, requires the generated `_camelCase` storage member, maps scalar `Maybe<T>` properties to nullable backing-field columns, and maps `Maybe<T>` where `T` is already owned as an optional ownership navigation.
- `CompositeValueObjectConvention` is internal. It only registers composite value objects discovered in the assemblies passed to `ApplyTrellisConventions` (plus built-in Trellis primitives scanning for scalar value objects). For `Maybe<T>` composite owned types, it uses nullable owned columns only when table-splitting is valid; it switches to a separate table named `{OwnerTypeName}_{PropertyName}` when nested owned navigations exist **or** when the owned type contains non-nullable value-type properties.
- `MoneyConvention` is internal. It registers `Money` as an owned type, names the amount column `{PropertyName}`, names the currency column `{PropertyName}Currency`, sets `decimal(18,3)` precision/scale for `Amount`, and handles optional `Maybe<Money>` columns through the annotation written by `MaybeConvention`.
- `MaybePartialPropertyGenerator` and `OwnedEntityGenerator` are compiler-time helpers shipped in the `Trellis.EntityFrameworkCore.Generator.dll` assembly, which is bundled inside `Trellis.EntityFrameworkCore.nupkg` at `analyzers/dotnet/cs/` since Phase 2 of the v2 redesign — there is no separate `Trellis.EntityFrameworkCore.Generator` NuGet package. `TRLS035` is reported only for non-partial auto-properties of type `Maybe<T>` whose containing type is partial. `TRLS036`, `TRLS037`, and `TRLS038` come from `[OwnedEntity]` validation and generation. (These IDs were `TRLSGEN100`–`TRLSGEN103` in v1; the unified `TRLS###` namespace is canonical from v2 onward — see `TrellisDiagnosticIds`.)

## Behavioral notes

### Source-generator state

`Trellis.EntityFrameworkCore` ships with a Roslyn source generator (`Trellis.EntityFrameworkCore.Generator.dll`, bundled at `analyzers/dotnet/cs/`). The current generator emits:

- `Maybe<T>` partial-property bodies with private `_camelCase` backing fields that EF Core can map through reflection-free conventions.
- `[OwnedEntity]` validation/generation diagnostics (`TRLS035`–`TRLS038`).

A generated `ApplyTrellisConventionsFor<TContext>` extension that would call `AddTrellisScalarConverter<TClr, TProvider>` and `AddTrellisCoreConventions(...)` directly (eliminating runtime `MakeGenericType`) is **planned** and is referenced in the package’s XML doc comments, but is not currently emitted. Today's recommended entry point remains the reflection-based `ModelConfigurationBuilder.ApplyTrellisConventions(typeof(SomeRootType).Assembly)`. AOT/trimming consumers can call `AddTrellisScalarConverter<TClr, TProvider>` and `AddTrellisCoreConventions` by hand to avoid `MakeGenericType`.

### `Maybe<T>` storage, owned types, and migrations

`MaybeConvention` and `CompositeValueObjectConvention` together control how `Maybe<T>` properties are stored. Knowing the rules helps when authoring EF migrations:

- **Scalar `Maybe<T>` (e.g., `Maybe<DateTimeOffset>`, `Maybe<EmailAddress>`).** The CLR `Maybe<T>` property is ignored; the source-generated `_camelCase` backing field is mapped as a **nullable column** named after the property (or the explicit `HasColumnName(...)` if configured). Migrations show this as a single nullable column. Use `MaybeUpdateExtensions.SetMaybeValue` / `SetMaybeNone` inside `ExecuteUpdate` and `MaybeQueryableExtensions.WhereHasValue` / `WhereNone` / etc. for predicates — these rewrite to the mapped storage member so the SQL targets the actual column.
- **Composite `Maybe<T>` where `T` is an `[OwnedEntity]`/composite `ValueObject`.** `CompositeValueObjectConvention` decides between two storage shapes:
  - **Table-splitting (default).** When the owned type contains only nullable value-type properties (or reference properties) and has no nested owned navigations, every column is mapped onto the parent table as nullable columns. `Maybe<T>.None` ⇒ all columns `NULL`.
  - **Separate table.** When the owned type contains **non-nullable value-type properties** or **nested owned navigations**, `Maybe<T>` switches to a separate table named `{OwnerTypeName}_{PropertyName}` to preserve nullability semantics. Migrations will produce a child table with FK to the parent. Switching the inner shape of an owned type between these two regimes therefore generates a non-trivial migration (column drop + table create, or vice-versa) — review the generated migration and provide custom `Up`/`Down` data-copy steps when production data exists.
- **`Maybe<Money>` specifically.** `MoneyConvention` honors the nullability annotation written by `MaybeConvention` so the amount/currency columns are emitted as nullable when the property is `Maybe<Money>`.
- **Indexes.** Use `MaybeEntityTypeBuilderExtensions.HasTrellisIndex(x => new { x.SubmittedAt, ... })` so EF Core indexes the mapped storage member instead of the unmapped `Maybe<T>` CLR property.
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
            ? Result.Fail<Address>(new Error.UnprocessableContent(EquatableArray.Create(violations.ToArray())))
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
            ? Result.Fail<CustomerId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "customerId"), "required") { Detail = "Customer ID is required." })))
            : Result.Ok(new CustomerId(value));

    public static Result<CustomerId> TryCreate(string? value, string? fieldName = null) =>
        Guid.TryParse(value, out var guid)
            ? TryCreate(guid, fieldName)
            : Result.Fail<CustomerId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "customerId"), "must_be_guid") { Detail = "Customer ID must be a GUID." })));
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
    new Error.NotFound(new ResourceRef("Customer")) { Detail = "Customer not found." });

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

## Cross-references

- [Trellis DDD primitives in `Trellis.Core` (API reference)](trellis-api-core.md) — `IEntity`, `IAggregate`, `Aggregate<TId>`, `Entity<TId>`, `ValueObject`, `ScalarValueObject<TSelf, T>`, and `Specification<T>`
- [Trellis.Core API reference](trellis-api-core.md) — `Result<T>`, `Maybe<T>`, `Error`, `Unit`, `IScalarValue<TSelf, TPrimitive>`, and `EntityTagValue`
- [Trellis.Primitives API reference](trellis-api-primitives.md) — `Money`, `RequiredEnum<T>`, and built-in value objects commonly scanned by `ApplyTrellisConventions`
