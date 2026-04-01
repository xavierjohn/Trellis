# Trellis.DomainDrivenDesign — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-primitives.md, trellis-api-efcore.md.

**Package:** `Trellis.DomainDrivenDesign` | **Namespace:** `Trellis`

## IEntity (interface)

Marker interface for entities that track creation and modification timestamps. Implemented by `Entity<TId>`. Used by `EntityTimestampInterceptor` (in `Trellis.EntityFrameworkCore`) to automatically set timestamps on `SaveChanges`.

```csharp
public interface IEntity
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset LastModified { get; set; }
}
```

## Entity\<TId\> (abstract class, where TId : notnull, implements IEntity)

Identity-based equality. Two entities are equal iff same type and same non-default ID. Implements `IEntity` to provide automatic `CreatedAt` and `LastModified` timestamps.

```csharp
public TId Id { get; init; }
public DateTimeOffset CreatedAt { get; set; }
public DateTimeOffset LastModified { get; set; }
protected Entity(TId id)
// Operators: ==, !=
// Overrides: Equals, GetHashCode
```

## IAggregate (interface, extends IChangeTracking)

Marker interface for aggregates. Implemented by `Aggregate<TId>`.

```csharp
public interface IAggregate : IChangeTracking
{
    IReadOnlyList<IDomainEvent> UncommittedEvents();
    string ETag { get; }  // optimistic concurrency token per RFC 9110 (opaque string, auto-generated on save)
}
```

## Aggregate\<TId\> (abstract class, extends Entity\<TId\>, implements IAggregate)

Consistency boundary that encapsulates domain state, enforces business rules through domain methods, and publishes domain events. Inherits `Entity<TId>`. Use for root entities that own child entities and control their lifecycle.

```csharp
protected Aggregate(TId id)
protected List<IDomainEvent> DomainEvents { get; }
bool IsChanged { get; }                    // true if DomainEvents.Count > 0
string ETag { get; private set; }          // RFC 9110 entity tag (auto-generated on save)
IReadOnlyList<IDomainEvent> UncommittedEvents()
void AcceptChanges()                       // clears DomainEvents
```

### RFC 9110 Optimistic Concurrency

`ETag` provides automatic optimistic concurrency per RFC 9110:
- `AggregateETagConvention` (registered by `ApplyTrellisConventions`) marks `ETag` as `IsConcurrencyToken()`
- `AggregateETagInterceptor` (registered by `AddTrellisInterceptors()`) generates a new GUID ETag on Added and Modified entries
- EF Core generates `UPDATE ... WHERE ETag = @original`; if stale → `ConflictError`
- `OptionalETag(expectedETags)` — skips if absent; returns `PreconditionFailedError` (412) on mismatch
- `RequireETag(expectedETags)` — returns `PreconditionRequiredError` (428) if absent; `PreconditionFailedError` (412) on mismatch

### OptionalETag / RequireETag Extensions

```csharp
// Optional — If-Match absent → unconditional update
Result<T> OptionalETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate
// Required — If-Match absent → 428 Precondition Required
Result<T> RequireETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate
// Async overloads
Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>>, EntityTagValue[]? expectedETags)
ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>>, EntityTagValue[]? expectedETags)
Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>>, EntityTagValue[]? expectedETags)
ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>>, EntityTagValue[]? expectedETags)
// null → no header (unconditional); [EntityTagValue.Wildcard()] → wildcard; [EntityTagValue.Strong("a")] → match any; [] → weak-only (unsatisfiable → 412)
```

## IDomainEvent (interface)

Marker interface for domain events raised by aggregates. Events are collected via `UncommittedEvents()` and published after persistence. Use to decouple side effects from the domain operation that triggered them.

```csharp
DateTime OccurredAt { get; }
```

## ValueObject (abstract class)

Structural equality based on `GetEqualityComponents()`. Hash code is cached (immutability assumed).

```csharp
protected abstract IEnumerable<IComparable?> GetEqualityComponents()

// Helper for including Maybe<T> in equality components.
// Returns the inner value if present, or null if empty.
protected static IComparable? MaybeComponent<T>(Maybe<T> maybe) where T : notnull, IComparable

// Operators: ==, !=, <, <=, >, >=
// Implements: IComparable<ValueObject>, IEquatable<ValueObject>
```

## ScalarValueObject\<TSelf, T\> (abstract class, extends ValueObject)

Single-value wrapper. Constraints: `TSelf : ScalarValueObject<TSelf, T>, IScalarValue<TSelf, T>` and `T : IComparable`.

```csharp
T Value { get; }
protected ScalarValueObject(T value)
static TSelf Create(T value)               // calls TryCreate, throws on failure
implicit operator T(ScalarValueObject<TSelf, T> vo)  // unwrap to primitive
// Implements IConvertible
```

## IScalarValue\<TSelf, TPrimitive\> (interface)

Interface for value objects wrapping a single primitive value. Enables automatic ASP.NET Core model binding, JSON serialization, and EF Core value conversion. Implemented by the source generator on `RequiredString`, `RequiredInt`, `RequiredGuid`, etc.

```csharp
static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null)
static virtual TSelf Create(TPrimitive value)  // default: TryCreate + throw
TPrimitive Value { get; }
```

## IFormattableScalarValue\<TSelf, TPrimitive\> (interface)

Extends `IScalarValue` with culture-aware string parsing. Implemented by numeric and date value objects where culture affects string parsing (decimal separators, date formats).

```csharp
static abstract Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null);
```

Implementors: `Age`, `MonetaryAmount`, `Percentage` (hand-implemented), `RequiredInt<T>`, `RequiredDecimal<T>`, `RequiredLong<T>`, `RequiredDateTime<T>` (source-generated). Not implemented by string-based types (`EmailAddress`, `Slug`, etc.) — culture doesn't affect their parsing.

## Specification\<T\> (abstract class)

Composable business rules that produce `Expression<Func<T, bool>>`.

```csharp
abstract Expression<Func<T, bool>> ToExpression()
bool IsSatisfiedBy(T entity)
protected virtual bool CacheCompilation => true    // when true (default), IsSatisfiedBy caches the compiled expression
                                                   // override to false for specifications that capture mutable state
Specification<T> And(Specification<T> other)
Specification<T> Or(Specification<T> other)
Specification<T> Not()
implicit operator Expression<Func<T, bool>>(Specification<T> spec)
```

---
