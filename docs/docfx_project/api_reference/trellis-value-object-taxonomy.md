---
package: Trellis.Core, Trellis.Primitives
namespaces: [Trellis, Trellis.Primitives]
types: [ValueObject, "ScalarValueObject<TSelf,T>", RequiredString<TSelf>, RequiredGuid<TSelf>, RequiredInt<TSelf>, RequiredLong<TSelf>, RequiredDecimal<TSelf>, RequiredBool<TSelf>, RequiredDateTime<TSelf>, RequiredDateTimeOffset<TSelf>, RequiredEnum<TSelf>, Maybe<T>]
version: v3
last_verified: 2026-06-16
audience: [llm]
---
# Trellis Value Object Taxonomy

**Packages:** `Trellis.Core` (DDD primitives `Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>`, `IDomainEvent`, plus VO base classes `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, `RequiredInt<TSelf>`, `RequiredLong<TSelf>`, `RequiredDecimal<TSelf>`, `RequiredBool<TSelf>`, `RequiredDateTime<TSelf>`, `RequiredDateTimeOffset<TSelf>`, `RequiredEnum<TSelf>`); `Trellis.Primitives` (concrete VOs only — `EmailAddress`, `Money`, etc.) | **Namespaces:** `Trellis`, `Trellis.Primitives` | **Purpose:** canonical category map for Trellis value-like types: scalar, symbolic, structured, and optionality wrappers.

## Patterns Index

Use this table to pick the right base class before reading the per-type signatures below.

| Goal | Canonical base / type | See |
|---|---|---|
| Wrap a single primitive (string, Guid, int, long, decimal, bool, date/time, enum) into a typed value object | `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, `RequiredInt<TSelf>`, `RequiredLong<TSelf>`, `RequiredDecimal<TSelf>`, `RequiredBool<TSelf>`, `RequiredDateTime<TSelf>`, `RequiredDateTimeOffset<TSelf>`, `RequiredEnum<TSelf>` (one base per primitive family) | [`RequiredString<TSelf>`](#requiredstringtself), [`RequiredGuid<TSelf>`](#requiredguidtself), [`RequiredInt<TSelf>`](#requiredinttself), [`RequiredDecimal<TSelf>`](#requireddecimaltself), [`RequiredEnum<TSelf>`](#requiredenumtself) |
| Define a custom-validated scalar with no source-generated infrastructure | `ScalarValueObject<TSelf, T>` + `IScalarValue<TSelf, T>` | [`ScalarValueObject<TSelf, T>`](#scalarvalueobjecttself-t) |
| Compose multiple value-typed fields into a structural value object | `ValueObject` (override `GetEqualityComponents`) | [`ValueObject`](#valueobject) |
| Wrap an entity with identity (mutable through methods) | `Entity<TId>` | See [trellis-api-core.md → Domain-Driven Design](trellis-api-core.md#domain-driven-design) |
| Wrap a consistency boundary with domain events | `Aggregate<TId>` | See [trellis-api-core.md → Domain-Driven Design](trellis-api-core.md#domain-driven-design) |
| Express expected absence of a value | `Maybe<T>` | See [trellis-api-core.md → Maybe](trellis-api-core.md#public-readonly-struct-maybet-where-t--notnull) |
| Move a query predicate out of a repository | `Specification<T>` | See [trellis-api-core.md → Domain-Driven Design](trellis-api-core.md#domain-driven-design) |
| Pick a built-in concrete value object instead of writing your own | `EmailAddress`, `Money`, `CountryCode`, `Url`, etc. | See [trellis-api-primitives.md](trellis-api-primitives.md#built-in-primitives-table) |


## Required base defaults

`Required*<TSelf>` bases are lenient by default: rejects only `null`. Use `[NotDefault]` to opt into sentinel rejection and `[Trim]` to opt into string trimming.

| Base | Default rejects | Opt-in to reject sentinel |
|---|---|---|
| `RequiredString<TSelf>` | `null` only (accepts `""`, whitespace; no auto-trim) | `[NotDefault]` rejects `""`; `[Trim]` enables trimming; combine for strict trim-then-reject-empty |
| `RequiredGuid<TSelf>` | `null` only (accepts `Guid.Empty`) | `[NotDefault]` rejects `Guid.Empty` |
| `RequiredInt<TSelf>` / `RequiredLong<TSelf>` / `RequiredDecimal<TSelf>` | `null` only (accepts `0`) | `[NotDefault]` rejects the zero sentinel |
| `RequiredDateTime<TSelf>` / `RequiredDateTimeOffset<TSelf>` | `null` only (accepts `MinValue`) | `[NotDefault]` rejects `MinValue` |
| `RequiredBool<TSelf>` | `null` | none (`false` is valid) |
| `RequiredEnum<TSelf>` | `null`, undeclared names | none (smart-enum lookup) |

## Types

This section is a **selection guide** — one or two distinguishing facts per type, not a signature reference. For complete signatures (members, overloads, operators, generated factories) see the per-type entries in [trellis-api-core.md](trellis-api-core.md#primitive-value-object-base-classes) for the base classes and interfaces below, and [trellis-api-primitives.md](trellis-api-primitives.md#types) for the concrete built-in primitives. Default rejection behavior for every `Required*<TSelf>` base is the authoritative [Required base defaults](#required-base-defaults) table above and is not restated per type here.

### `ValueObject`

Base for **structured** value objects: override `GetEqualityComponents()` to define identity from multiple fields. Provides structural equality, hashing, and ordering (`IComparable`); yielded components must be `IComparable?` — use `MaybeComponent<T>(...)` to yield an optional field.

### `ScalarValueObject<TSelf, T>`

Base for a single-primitive value object with custom validation and **no** source generation. Exposes `Value : T`, implicit unwrap to `T`, and a throwing `Create(T)`. Prefer a `Required*<TSelf>` base unless you need a primitive family they don't cover.

### `IScalarValue<TSelf, TPrimitive>`

Static-abstract contract every scalar value object implements (`TryCreate` primitive/string paths + `Value`). The source generator emits it for partial `Required*` types; you rarely implement it by hand.

### `IFormattableScalarValue<TSelf, TPrimitive>`

`IScalarValue` plus a culture-aware `TryCreate(string?, IFormatProvider?, ...)` — used by the numeric and date/time scalar families.

### `RequiredString<TSelf>`

Single-`string` value object. Adds EF-Core-translatable query helpers (`StartsWith`/`Contains`/`EndsWith`, `Length`). Apply `[Trim, NotDefault, StringLength(n)]` for the strict, database-mapped default.

### `RequiredGuid<TSelf>`

Single-`Guid` identity value object. The generator adds `NewUniqueV7()` (time-ordered) and `NewUniqueV4()` — the idiomatic way to mint aggregate and entity IDs.

### `RequiredInt<TSelf>`

Single-`int` value object. Bound the domain with `[Range(min, max)]`; add `[NotDefault]` only when `0` is itself invalid (otherwise `0` is a legal value).

### `RequiredDecimal<TSelf>`

Single-`decimal` value object for ratios, rates, and quantities. For money use the dedicated `MonetaryAmount` (single-currency) or `Money` (multi-currency) types.

### `RequiredLong<TSelf>`

Single-`long` value object; same shape as `RequiredInt<TSelf>` for 64-bit ranges.

### `RequiredBool<TSelf>`

Single-`bool` value object. Both `true` and `false` are valid (no sentinel), so `[NotDefault]` does not apply.

### `RequiredDateTime<TSelf>`

Single-`DateTime` value object; `ToString()` is an invariant ISO-8601 round-trip.

### `RequiredDateTimeOffset<TSelf>`

Single-`DateTimeOffset` value object; prefer over `RequiredDateTime<TSelf>` when an offset matters (e.g. `IDomainEvent.OccurredAt` is a `DateTimeOffset`).

### `RequiredEnum<TSelf>`

**Symbolic** smart-enum that replaces a C# `enum`; members are `public static readonly` singletons. Identity is the string `Value` (equality is case-insensitive); ordering (`IComparable`) is by `Ordinal` = **declaration order**, like the underlying enum it replaces. String-backed in JSON and EF Core. Use `GetAll()` / `TryFromName(...)` for lookup and `[EnumValue("...")]` to override a member's external name. Stands apart from `ScalarValueObject<TSelf, T>`.

### `MonetaryAmount`

Concrete **single-currency** amount (`Value : decimal`, no currency component). Arithmetic returns `Result<>`; `TryCreate` rejects negatives.

### `Percentage`

Concrete percentage (`Value : decimal`) with `FromFraction` / `AsFraction` / `Of(amount)` helpers and `%` parsing.

### `Money`

Concrete **structured** value object (`Amount` + `Currency`) for multi-currency; arithmetic enforces a currency match and supports `Allocate(...)`.

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
    - `RequiredDateTimeOffset<TSelf>`
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

For a `partial` `Required*<TSelf>` type the primitive generator emits the `IScalarValue<TSelf, T>` implementation, the `TryCreate` primitive and string factories (plus a culture-aware `IFormatProvider` overload for the numeric and date/time families), `Create`, `Parse` / `TryParse`, an explicit cast operator, and a `ValidateAdditional(...)` extension hook. `RequiredGuid<TSelf>` additionally gets `NewUniqueV4()` / `NewUniqueV7()`; `RequiredEnum<TSelf>` creation routes through `TryFromName`. See the per-type generated-member entries in [trellis-api-core.md](trellis-api-core.md#primitive-value-object-base-classes) for the exact emitted signatures.

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

- [trellis-api-primitives.md](trellis-api-primitives.md#base-class-hierarchy)
- [trellis-api-core.md](trellis-api-core.md#domain-driven-design)
- [trellis-api-efcore.md](trellis-api-efcore.md#maybet-storage-owned-types-and-migrations)
