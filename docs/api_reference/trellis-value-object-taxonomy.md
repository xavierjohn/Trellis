# Trellis Value Object Taxonomy

**Packages:** `Trellis.Core` (since Phase 2 of the v2 redesign: DDD primitives `Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>`, `IDomainEvent`, plus VO base classes `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, `RequiredInt<TSelf>`, `RequiredDecimal<TSelf>`, `RequiredEnum<TSelf>`); `Trellis.Primitives` (concrete VOs only — `EmailAddress`, `Money`, etc.) | **Namespaces:** `Trellis`, `Trellis.Primitives` | **Purpose:** canonical category map for Trellis value-like types: scalar, symbolic, structured, and optionality wrappers.

## Types

### `ValueObject`

```csharp
public abstract class ValueObject : IComparable<ValueObject>, IComparable, IEquatable<ValueObject>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Base type for all structural equality objects. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override bool Equals(object? obj)` | `bool` | Structural equality. |
| `public bool Equals(ValueObject? other)` | `bool` | Strongly typed structural equality. |
| `public override int GetHashCode()` | `int` | Cached structural hash. |
| `public virtual int CompareTo(ValueObject? other)` | `int` | Ordered comparison by equality components. |
| `public static bool operator ==(ValueObject? a, ValueObject? b)` | `bool` | Equality operator. |
| `public static bool operator !=(ValueObject? a, ValueObject? b)` | `bool` | Inequality operator. |
| `public static bool operator <(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator. |
| `public static bool operator <=(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator. |
| `public static bool operator >(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator. |
| `public static bool operator >=(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator. |

### `ScalarValueObject<TSelf, T>`

```csharp
public abstract class ScalarValueObject<TSelf, T> : ValueObject, IConvertible, IFormattable
    where TSelf : ScalarValueObject<TSelf, T>, IScalarValue<TSelf, T>
    where T : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` | Canonical scalar identity. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override string ToString()` | `string` | Primitive string form. |
| `public static implicit operator T(ScalarValueObject<TSelf, T> valueObject)` | `T` | Implicit unwrap. |
| `public static TSelf Create(T value)` | `TSelf` | Throwing scalar factory. |
| `public string ToString(string? format, IFormatProvider? formatProvider)` | `string` | Formattable support. |

### `IScalarValue<TSelf, TPrimitive>`

```csharp
public interface IScalarValue<TSelf, TPrimitive>
    where TSelf : IScalarValue<TSelf, TPrimitive>
    where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `TPrimitive` | Required canonical scalar property. |

| Signature | Returns | Description |
| --- | --- | --- |
| `static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null)` | `Result<TSelf>` | Primitive creation path. |
| `static abstract Result<TSelf> TryCreate(string? value, string? fieldName = null)` | `Result<TSelf>` | String creation path. |
| `static virtual TSelf Create(TPrimitive value)` | `TSelf` | Throwing convenience factory. |

### `IFormattableScalarValue<TSelf, TPrimitive>`

```csharp
public interface IFormattableScalarValue<TSelf, TPrimitive> : IScalarValue<TSelf, TPrimitive>
    where TSelf : IFormattableScalarValue<TSelf, TPrimitive>
    where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `TPrimitive` | Inherited canonical scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `static abstract Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` | `Result<TSelf>` | Culture-aware string creation path used by numeric/date scalar types. |

### `RequiredString<TSelf>`

```csharp
public abstract class RequiredString<TSelf> : ScalarValueObject<TSelf, string>
    where TSelf : RequiredString<TSelf>, IScalarValue<TSelf, string>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Canonical scalar identity for required text. |
| `Length` | `int` | String convenience member. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public bool StartsWith(string value)` | `bool` | Query helper. |
| `public bool Contains(string value)` | `bool` | Query helper. |
| `public bool EndsWith(string value)` | `bool` | Query helper. |

### `RequiredGuid<TSelf>`

```csharp
public abstract class RequiredGuid<TSelf> : ScalarValueObject<TSelf, Guid>
    where TSelf : RequiredGuid<TSelf>, IScalarValue<TSelf, Guid>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `Guid` | Canonical scalar identity for non-empty GUIDs. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(Guid value)` | `TSelf` | Inherited throwing scalar factory. |

### `RequiredInt<TSelf>`

```csharp
public abstract class RequiredInt<TSelf> : ScalarValueObject<TSelf, int>
    where TSelf : RequiredInt<TSelf>, IScalarValue<TSelf, int>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `int` | Canonical scalar identity for required integers. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(int value)` | `TSelf` | Inherited throwing scalar factory. |

### `RequiredDecimal<TSelf>`

```csharp
public abstract class RequiredDecimal<TSelf> : ScalarValueObject<TSelf, decimal>
    where TSelf : RequiredDecimal<TSelf>, IScalarValue<TSelf, decimal>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `decimal` | Canonical scalar identity for required decimals. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(decimal value)` | `TSelf` | Inherited throwing scalar factory. |

### `RequiredLong<TSelf>`

```csharp
public abstract class RequiredLong<TSelf> : ScalarValueObject<TSelf, long>
    where TSelf : RequiredLong<TSelf>, IScalarValue<TSelf, long>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `long` | Canonical scalar identity for required longs. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(long value)` | `TSelf` | Inherited throwing scalar factory. |

### `RequiredBool<TSelf>`

```csharp
public abstract class RequiredBool<TSelf> : ScalarValueObject<TSelf, bool>
    where TSelf : RequiredBool<TSelf>, IScalarValue<TSelf, bool>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `bool` | Canonical scalar identity for required booleans. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(bool value)` | `TSelf` | Inherited throwing scalar factory. |

### `RequiredDateTime<TSelf>`

```csharp
public abstract class RequiredDateTime<TSelf> : ScalarValueObject<TSelf, DateTime>
    where TSelf : RequiredDateTime<TSelf>, IScalarValue<TSelf, DateTime>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `DateTime` | Canonical scalar identity for non-default dates. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override string ToString()` | `string` | Invariant ISO 8601 round-trip string. |
| `public static TSelf Create(DateTime value)` | `TSelf` | Inherited throwing scalar factory. |

### `RequiredEnum<TSelf>`

```csharp
public abstract class RequiredEnum<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TSelf>
    : IEquatable<RequiredEnum<TSelf>>
    where TSelf : RequiredEnum<TSelf>, IScalarValue<TSelf, string>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Canonical symbolic identity. |
| `Ordinal` | `int` | Non-semantic declaration-order metadata. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IReadOnlyCollection<TSelf> GetAll()` | `IReadOnlyCollection<TSelf>` | Returns all declared symbolic members. |
| `public static Result<TSelf> TryFromName(string? name, string? fieldName = null)` | `Result<TSelf>` | Canonical symbolic lookup. |
| `public bool Is(params TSelf[] values)` | `bool` | Membership check. |
| `public bool IsNot(params TSelf[] values)` | `bool` | Negated membership check. |
| `public override string ToString()` | `string` | Returns `Value`. |
| `public override int GetHashCode()` | `int` | Case-insensitive symbolic hash. |
| `public override bool Equals(object? obj)` | `bool` | Case-insensitive symbolic equality. |
| `public bool Equals(RequiredEnum<TSelf>? other)` | `bool` | Case-insensitive symbolic equality. |
| `public static bool operator ==(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Equality operator. |
| `public static bool operator !=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Inequality operator. |

### `MonetaryAmount`

```csharp
public class MonetaryAmount : ScalarValueObject<MonetaryAmount, decimal>, IScalarValue<MonetaryAmount, decimal>, IFormattableScalarValue<MonetaryAmount, decimal>, IParsable<MonetaryAmount>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `decimal` | Canonical scalar identity for single-currency systems. |
| `Zero` | `MonetaryAmount` | Cached zero instance. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<MonetaryAmount> TryCreate(decimal value, string? fieldName = null)` | `Result<MonetaryAmount>` | Non-negative scalar creation path. |
| `public static Result<MonetaryAmount> TryCreate(decimal? value, string? fieldName = null)` | `Result<MonetaryAmount>` | Nullable scalar creation path. |
| `public static Result<MonetaryAmount> TryCreate(string? value, string? fieldName = null)` | `Result<MonetaryAmount>` | Invariant string creation path. |
| `public static Result<MonetaryAmount> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` | `Result<MonetaryAmount>` | Culture-aware string creation path. |
| `public Result<MonetaryAmount> Add(MonetaryAmount other)` | `Result<MonetaryAmount>` | Scalar arithmetic. |
| `public Result<MonetaryAmount> Subtract(MonetaryAmount other)` | `Result<MonetaryAmount>` | Scalar arithmetic. |
| `public Result<MonetaryAmount> Multiply(int quantity)` | `Result<MonetaryAmount>` | Scalar arithmetic. |
| `public Result<MonetaryAmount> Multiply(decimal multiplier)` | `Result<MonetaryAmount>` | Scalar arithmetic. |
| `public static Result<MonetaryAmount> Sum(IEnumerable<MonetaryAmount> values)` | `Result<MonetaryAmount>` | Scalar accumulation. |

### `Percentage`

```csharp
public class Percentage : ScalarValueObject<Percentage, decimal>, IScalarValue<Percentage, decimal>, IFormattableScalarValue<Percentage, decimal>, IParsable<Percentage>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `decimal` | Canonical scalar identity for percentages. |
| `Zero` | `Percentage` | Cached `0%`. |
| `Full` | `Percentage` | Cached `100%`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<Percentage> TryCreate(decimal value, string? fieldName = null)` | `Result<Percentage>` | Numeric creation path. |
| `public static Result<Percentage> TryCreate(decimal? value, string? fieldName = null)` | `Result<Percentage>` | Nullable numeric creation path. |
| `public static Result<Percentage> TryCreate(string? value, string? fieldName = null)` | `Result<Percentage>` | Invariant string creation path. |
| `public static Result<Percentage> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` | `Result<Percentage>` | Culture-aware string creation path. |
| `public static Result<Percentage> FromFraction(decimal fraction, string? fieldName = null)` | `Result<Percentage>` | Derived scalar factory. |
| `public decimal AsFraction()` | `decimal` | Converts to fraction. |
| `public decimal Of(decimal amount)` | `decimal` | Applies percentage to an amount. |

### `Money`

```csharp
public class Money : ValueObject
```

| Name | Type | Description |
| --- | --- | --- |
| `Amount` | `decimal` | One structured component. |
| `Currency` | `CurrencyCode` | Second structured component. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<Money> TryCreate(decimal amount, string currencyCode, string? fieldName = null)` | `Result<Money>` | Structured factory. |
| `public static Money Create(decimal amount, string currencyCode)` | `Money` | Throwing structured factory. |
| `public Result<Money> Add(Money other)` | `Result<Money>` | Structured arithmetic with currency match enforcement. |
| `public Result<Money> Subtract(Money other)` | `Result<Money>` | Structured arithmetic with currency match enforcement. |
| `public Result<Money> Multiply(decimal multiplier)` | `Result<Money>` | Structured arithmetic. |
| `public Result<Money> Multiply(int quantity)` | `Result<Money>` | Structured arithmetic. |
| `public Result<Money> Divide(decimal divisor)` | `Result<Money>` | Structured arithmetic. |
| `public Result<Money> Divide(int divisor)` | `Result<Money>` | Structured arithmetic. |
| `public Result<Money[]> Allocate(params int[] ratios)` | `Result<Money[]>` | Structured split operation. |
| `public static Result<Money> Zero(string currencyCode = "USD")` | `Result<Money>` | Structured zero factory. |
| `public static Result<Money> Sum(IEnumerable<Money> values)` | `Result<Money>` | Structured accumulation. |

## Base class hierarchy

- **Scalar value objects**
  - `ValueObject` -> `ScalarValueObject<TSelf, T>` -> concrete scalars
  - Required scalar bases:
    - `RequiredString<TSelf>`
    - `RequiredGuid<TSelf>`
    - `RequiredInt<TSelf>`
    - `RequiredDecimal<TSelf>`
    - `RequiredLong<TSelf>`
    - `RequiredBool<TSelf>`
    - `RequiredDateTime<TSelf>`
  - Built-in scalar concretes:
    - `Age`
    - `CountryCode`
    - `CurrencyCode`
    - `EmailAddress`
    - `Hostname`
    - `IpAddress`
    - `LanguageCode`
    - `MonetaryAmount`
    - `Percentage`
    - `PhoneNumber`
    - `Slug`
    - `Url`
- **Symbolic value objects**
  - `RequiredEnum<TSelf>` is separate from `ScalarValueObject<TSelf, T>` but still uses `Value` as its canonical public identity.
- **Structured value objects**
  - `Money` -> `ValueObject`
- **Optionality wrappers**
  - `Maybe<T>` belongs to `Trellis.Core`; it wraps presence/absence and is not a value object category peer to scalar/symbolic/structured types.

## Source-generated members

The primitive generator adds category-specific members to partial types:

- `RequiredGuid<TSelf>`: `NewUniqueV4()`, `NewUniqueV7()`, `TryCreate(Guid value, string? fieldName = null)`, `TryCreate(Guid? requiredGuidOrNothing, string? fieldName = null)`, `TryCreate(string? stringOrNull, string? fieldName = null)`, `Create(string stringValue)`, `Parse`, `TryParse`, explicit cast, and `ValidateAdditional(Guid value, string fieldName, ref string? errorMessage)`.
- `RequiredString<TSelf>`: `TryCreate(string? value, string? fieldName = null)`, `Create(string? value, string? fieldName = null)`, `Parse`, `TryParse`, explicit cast, and `ValidateAdditional(string value, string fieldName, ref string? errorMessage)`.
- `RequiredInt<TSelf>`, `RequiredDecimal<TSelf>`, `RequiredLong<TSelf>`, and `RequiredDateTime<TSelf>`: the invariant `TryCreate(string? stringOrNull, string? fieldName = null)` overload plus the culture-aware `TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` overload, `Create(string stringValue)`, `Parse`, `TryParse`, explicit cast, and `ValidateAdditional(...)`.
- `RequiredBool<TSelf>`: primitive/string factories, `Parse`, `TryParse`, explicit cast, and `ValidateAdditional(bool value, string fieldName, ref string? errorMessage)`.
- `RequiredEnum<TSelf>`: `TryCreate(string value)`, `TryCreate(string? value, string? fieldName = null)`, `Parse`, `TryParse`, and `Create(string value)`. Generated enum creation uses `TryFromName` only.

## Built-in primitives table

| Type | Category | Canonical identity | Wire/storage shape | Notes |
| --- | --- | --- | --- | --- |
| `Age` | Scalar | `Value : int` | JSON number | Numeric scalar. |
| `CountryCode` | Scalar | `Value : string` | JSON string | ISO alpha-2. |
| `CurrencyCode` | Scalar | `Value : string` | JSON string | ISO alpha-3. |
| `EmailAddress` | Scalar | `Value : string` | JSON string | Domain string scalar. |
| `Hostname` | Scalar | `Value : string` | JSON string | Domain string scalar. |
| `IpAddress` | Scalar | `Value : string` | JSON string | Domain string scalar. |
| `LanguageCode` | Scalar | `Value : string` | JSON string | ISO alpha-2. |
| `MonetaryAmount` | Scalar | `Value : decimal` | JSON number | Single-currency amount; no currency component. |
| `Percentage` | Scalar | `Value : decimal` | JSON number | Supports fraction helpers and `%` parsing. |
| `PhoneNumber` | Scalar | `Value : string` | JSON string | Normalized E.164. |
| `Slug` | Scalar | `Value : string` | JSON string | URL-safe slug. |
| `Url` | Scalar | `Value : string` | JSON string | Absolute HTTP/HTTPS URL. |
| `RequiredEnum<TSelf>` derivatives | Symbolic | `Value : string` | JSON string | Finite symbolic set with behavior. |
| `Money` | Structured | `Amount` + `Currency` | JSON object | Use for multi-currency scenarios. |

### `Money` vs `MonetaryAmount`

- Use `MonetaryAmount` when the bounded context has a single external currency policy and the semantic identity is only the numeric amount.
- Use `Money` when currency is part of the semantic identity and must travel with the value.
- `MonetaryAmount` is a scalar value object.
- `Money` is a structured value object.

## Code examples

```csharp
using System.Globalization;
using Trellis;
using Trellis.Primitives;

namespace Demo;

public partial class OrderId : RequiredGuid<OrderId> { }

public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new();

    [EnumValue("awaiting-payment")]
    public static readonly OrderStatus AwaitingPayment = new();
}

public static class Example
{
    public static void Run()
    {
        // Scalar
        var orderId = OrderId.NewUniqueV4();
        var amount = MonetaryAmount.TryCreate("12.34", CultureInfo.InvariantCulture).Value;

        // Symbolic
        var status = OrderStatus.TryFromName("awaiting-payment").Value;
        var isOpen = status.Is(OrderStatus.Draft, OrderStatus.AwaitingPayment);

        // Structured
        var subtotal = Money.Create(12.34m, "USD");
        var shipping = Money.Create(2.00m, "USD");
        var total = subtotal.Add(shipping).Value;

        _ = (orderId, amount, isOpen, total);
    }
}
```

## Cross-references

- [trellis-api-primitives.md](trellis-api-primitives.md)
- [trellis-api-core.md](trellis-api-core.md)
- [trellis-api-core.md](trellis-api-core.md)
- [trellis-api-efcore.md](trellis-api-efcore.md)
