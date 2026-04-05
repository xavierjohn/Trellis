# Trellis.DomainDrivenDesign

**Package:** `Trellis.DomainDrivenDesign`  
**Namespace:** `Trellis`  
**Purpose:** Base domain-driven design primitives for entities, aggregates, value objects, specifications, and aggregate ETag validation.

## Types

### `IEntity`

```csharp
public interface IEntity
```

| Name | Type | Description |
| --- | --- | --- |
| `CreatedAt` | `DateTimeOffset` | UTC timestamp for the first successful persistence of the entity. |
| `LastModified` | `DateTimeOffset` | UTC timestamp for the latest successful persistence update. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No methods. |

### `Entity<TId>`

```csharp
public abstract class Entity<TId> : IEntity where TId : notnull
```

| Name | Type | Description |
| --- | --- | --- |
| `Id` | `TId` | Immutable identity value for the entity. |
| `CreatedAt` | `DateTimeOffset` | Infrastructure-managed creation timestamp. |
| `LastModified` | `DateTimeOffset` | Infrastructure-managed last-modified timestamp. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected Entity(TId id)` | — | Initializes the entity identity. |
| `public override bool Equals(object? obj)` | `bool` | Returns `true` for the same reference before checking default IDs; otherwise compares exact runtime type and non-default IDs. |
| `public static bool operator ==(Entity<TId>? a, Entity<TId>? b)` | `bool` | Identity-based equality operator. |
| `public static bool operator !=(Entity<TId>? a, Entity<TId>? b)` | `bool` | Identity-based inequality operator. |
| `public override int GetHashCode()` | `int` | Combines runtime type and `Id`. |

### `IAggregate`

```csharp
public interface IAggregate : IChangeTracking
```

| Name | Type | Description |
| --- | --- | --- |
| `ETag` | `string` | Optimistic concurrency token for the aggregate. |
| `IsChanged` | `bool` | Inherited from `IChangeTracking`; implemented by `Aggregate<TId>` as domain-event-based change tracking by default. |

| Signature | Returns | Description |
| --- | --- | --- |
| `IReadOnlyList<IDomainEvent> UncommittedEvents()` | `IReadOnlyList<IDomainEvent>` | Returns the domain events raised since the last `AcceptChanges()`. |
| `void AcceptChanges()` | `void` | Inherited from `IChangeTracking`; marks the aggregate as committed. |

### `Aggregate<TId>`

```csharp
public abstract class Aggregate<TId> : Entity<TId>, IAggregate where TId : notnull
```

| Name | Type | Description |
| --- | --- | --- |
| `DomainEvents` | `List<IDomainEvent>` | Protected mutable event buffer for derived aggregate methods. |
| `ETag` | `string` | Persistence-managed optimistic concurrency token. |
| `IsChanged` | `bool` | `[JsonIgnore]` virtual change-tracking flag; default implementation is `DomainEvents.Count > 0`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected Aggregate(TId id)` | — | Initializes the aggregate identity. |
| `public IReadOnlyList<IDomainEvent> UncommittedEvents()` | `IReadOnlyList<IDomainEvent>` | Returns a read-only snapshot of current domain events. |
| `public void AcceptChanges()` | `void` | Clears `DomainEvents`. |

### `IDomainEvent`

```csharp
public interface IDomainEvent
```

| Name | Type | Description |
| --- | --- | --- |
| `OccurredAt` | `DateTime` | UTC timestamp for when the domain event occurred. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No methods. |

### `ValueObject`

```csharp
public abstract class ValueObject : IComparable<ValueObject>, IComparable, IEquatable<ValueObject>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public or protected properties. Equality and ordering are driven by methods. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected abstract IEnumerable<IComparable?> GetEqualityComponents()` | `IEnumerable<IComparable?>` | Returns the ordered components used for equality, comparison, and hash-code generation. |
| `protected static IComparable? MaybeComponent<T>(Maybe<T> maybe) where T : notnull, IComparable` | `IComparable?` | Converts `Maybe<T>` to an equality component by returning the inner value or `null`. |
| `public override bool Equals(object? obj)` | `bool` | Delegates to `Equals(ValueObject? other)`. |
| `public bool Equals(ValueObject? other)` | `bool` | Structural equality check against the same runtime type. |
| `public override int GetHashCode()` | `int` | Computes and caches a hash code from the equality components. |
| `public virtual int CompareTo(ValueObject? other)` | `int` | Compares equality components in order. |
| `public static bool operator ==(ValueObject? a, ValueObject? b)` | `bool` | Structural equality operator. |
| `public static bool operator !=(ValueObject? a, ValueObject? b)` | `bool` | Structural inequality operator. |
| `public static bool operator <(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator based on `CompareTo(ValueObject?)`. |
| `public static bool operator <=(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator based on `CompareTo(ValueObject?)`. |
| `public static bool operator >(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator based on `CompareTo(ValueObject?)`. |
| `public static bool operator >=(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator based on `CompareTo(ValueObject?)`. |

### `ScalarValueObject<TSelf, T>`

```csharp
public abstract class ScalarValueObject<TSelf, T> : ValueObject, IConvertible, IFormattable
where TSelf : ScalarValueObject<TSelf, T>, IScalarValue<TSelf, T>
where T : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` | Wrapped scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected ScalarValueObject(T value)` | — | Stores the wrapped scalar value. |
| `protected override IEnumerable<IComparable?> GetEqualityComponents()` | `IEnumerable<IComparable?>` | Default scalar equality uses only `Value`. |
| `public override string ToString()` | `string` | Returns `Value?.ToString() ?? string.Empty`. |
| `public static implicit operator T(ScalarValueObject<TSelf, T> valueObject)` | `T` | Unwraps the scalar value object to its primitive value. |
| `public static TSelf Create(T value)` | `TSelf` | Calls `TSelf.TryCreate(value)` and throws `InvalidOperationException` on failure. |
| `public TypeCode GetTypeCode()` | `TypeCode` | Returns `Type.GetTypeCode(typeof(T))`. |
| `public bool ToBoolean(IFormatProvider? provider)` | `bool` | Converts `Value` with `Convert.ToBoolean`. |
| `public byte ToByte(IFormatProvider? provider)` | `byte` | Converts `Value` with `Convert.ToByte`. |
| `public char ToChar(IFormatProvider? provider)` | `char` | Converts `Value` with `Convert.ToChar`. |
| `public DateTime ToDateTime(IFormatProvider? provider)` | `DateTime` | Converts `Value` with `Convert.ToDateTime`. |
| `public decimal ToDecimal(IFormatProvider? provider)` | `decimal` | Converts `Value` with `Convert.ToDecimal`. |
| `public double ToDouble(IFormatProvider? provider)` | `double` | Converts `Value` with `Convert.ToDouble`. |
| `public short ToInt16(IFormatProvider? provider)` | `short` | Converts `Value` with `Convert.ToInt16`. |
| `public int ToInt32(IFormatProvider? provider)` | `int` | Converts `Value` with `Convert.ToInt32`. |
| `public long ToInt64(IFormatProvider? provider)` | `long` | Converts `Value` with `Convert.ToInt64`. |
| `public sbyte ToSByte(IFormatProvider? provider)` | `sbyte` | Converts `Value` with `Convert.ToSByte`. |
| `public float ToSingle(IFormatProvider? provider)` | `float` | Converts `Value` with `Convert.ToSingle`. |
| `public string ToString(IFormatProvider? provider)` | `string` | Converts `Value` with `Convert.ToString`. |
| `public string ToString(string? format, IFormatProvider? formatProvider)` | `string` | Uses `IFormattable` when the wrapped value supports it; otherwise uses `Convert.ToString`. |
| `public object ToType(Type conversionType, IFormatProvider? provider)` | `object` | Converts `Value` to an arbitrary type via `Convert.ChangeType`. |
| `public ushort ToUInt16(IFormatProvider? provider)` | `ushort` | Converts `Value` with `Convert.ToUInt16`. |
| `public uint ToUInt32(IFormatProvider? provider)` | `uint` | Converts `Value` with `Convert.ToUInt32`. |
| `public ulong ToUInt64(IFormatProvider? provider)` | `ulong` | Converts `Value` with `Convert.ToUInt64`. |

### `AggregateETagExtensions`

```csharp
public static class AggregateETagExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> OptionalETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate` | `Result<T>` | If `expectedETags` is `null`, returns the original result unchanged; otherwise enforces strong ETag matching. |
| `public static Result<T> RequireETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate` | `Result<T>` | Requires an `If-Match` value and enforces strong ETag matching. |
| `public static Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate` | `Task<Result<T>>` | Async `Task` wrapper for `OptionalETag<T>`. |
| `public static ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate` | `ValueTask<Result<T>>` | Async `ValueTask` wrapper for `OptionalETag<T>`. |
| `public static Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate` | `Task<Result<T>>` | Async `Task` wrapper for `RequireETag<T>`. |
| `public static ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate` | `ValueTask<Result<T>>` | Async `ValueTask` wrapper for `RequireETag<T>`. |

### `Specification<T>`

```csharp
public abstract class Specification<T>
```

| Name | Type | Description |
| --- | --- | --- |
| `CacheCompilation` | `bool` | Protected virtual switch that controls whether `IsSatisfiedBy(T entity)` reuses a lazily compiled delegate. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected Specification()` | — | Initializes the lazy compiled delegate cache. |
| `public abstract Expression<Func<T, bool>> ToExpression()` | `Expression<Func<T, bool>>` | Returns the canonical expression tree for the specification. |
| `public bool IsSatisfiedBy(T entity)` | `bool` | Evaluates the specification in memory. |
| `public Specification<T> And(Specification<T> other)` | `Specification<T>` | Returns a composed AND specification. |
| `public Specification<T> Or(Specification<T> other)` | `Specification<T>` | Returns a composed OR specification. |
| `public Specification<T> Not()` | `Specification<T>` | Returns a negated specification. |
| `public static implicit operator Expression<Func<T, bool>>(Specification<T> spec)` | `Expression<Func<T, bool>>` | Converts the specification directly to its expression tree. |

## Extension methods

### `AggregateETagExtensions`

```csharp
public static Result<T> OptionalETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate
public static Result<T> RequireETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate
public static Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate
public static ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate
public static Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate
public static ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate
```

Notes:

- Matching is always strong RFC 9110 comparison.
- `expectedETags is null` means “no `If-Match` header supplied”.
- `expectedETags.Length == 0` fails with `PreconditionFailedError` because the header contained only weak ETags.
- `EntityTagValue.Wildcard()` bypasses value comparison and succeeds immediately.

## Internal types

- `AndSpecification<T>`, `OrSpecification<T>`, and `NotSpecification<T>` are internal implementation types returned by the public combinators on `Specification<T>`.
- `IScalarValue<TSelf, TPrimitive>` and `IFormattableScalarValue<TSelf, TPrimitive>` are **not** in this package. They are defined in `Trellis.Results` and are referenced by `ScalarValueObject<TSelf, T>` through its generic constraint.

## Code examples

### Aggregate, entity, and ETag validation

```csharp
using System;
using Trellis;

public sealed class OrderId : ScalarValueObject<OrderId, Guid>, IScalarValue<OrderId, Guid>
{
    private OrderId(Guid value) : base(value) { }

    public static Result<OrderId> TryCreate(Guid value, string? fieldName = null) =>
        value == Guid.Empty
            ? Result.Failure<OrderId>(Error.Validation("Order ID is required.", fieldName ?? "orderId"))
            : Result.Success(new OrderId(value));

    public static Result<OrderId> TryCreate(string? value, string? fieldName = null) =>
        Guid.TryParse(value, out var guid)
            ? TryCreate(guid, fieldName)
            : Result.Failure<OrderId>(Error.Validation("Order ID must be a GUID.", fieldName ?? "orderId"));
}

public sealed record OrderPlaced(OrderId OrderId, DateTime OccurredAt) : IDomainEvent;

public sealed class Order : Aggregate<OrderId>
{
    public string Description { get; private set; }

    private Order(OrderId id, string description) : base(id) => Description = description;

    public static Result<Order> Create(string description)
    {
        var order = new Order(OrderId.Create(Guid.NewGuid()), description);
        order.DomainEvents.Add(new OrderPlaced(order.Id, DateTime.UtcNow));
        return Result.Success(order);
    }
}

Result<Order> orderResult = Order.Create("starter-order");
var guarded = orderResult.OptionalETag(new[] { EntityTagValue.Strong(orderResult.Value.ETag) });
```

### Specification composition

```csharp
using System;
using System.Linq.Expressions;
using Trellis;

public sealed class Subscription
{
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsCancelled { get; init; }
}

public sealed class ExpiredSubscriptionSpec(DateTimeOffset now) : Specification<Subscription>
{
    public override Expression<Func<Subscription, bool>> ToExpression() =>
        subscription => subscription.ExpiresAt < now;
}

public sealed class ActiveSubscriptionSpec : Specification<Subscription>
{
    public override Expression<Func<Subscription, bool>> ToExpression() =>
        subscription => !subscription.IsCancelled;
}

var spec = new ExpiredSubscriptionSpec(DateTimeOffset.UtcNow)
    .And(new ActiveSubscriptionSpec());
```

## Cross-references

- [Trellis.Results API reference](trellis-api-results.md) — `Result<T>`, `Maybe<T>`, `Error`, `EntityTagValue`, `IScalarValue<TSelf, TPrimitive>`, and `IFormattableScalarValue<TSelf, TPrimitive>`
- [Trellis.Primitives API reference](trellis-api-primitives.md) — built-in scalar and composite value objects that build on these DDD primitives
- [Trellis.EntityFrameworkCore API reference](trellis-api-efcore.md) — EF Core conventions and interceptors for `IEntity`, `IAggregate`, `ValueObject`, and `Maybe<T>`
