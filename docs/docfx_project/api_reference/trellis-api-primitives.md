---
package: Trellis.Primitives
namespaces: [Trellis, Trellis.Primitives]
types: [Age, CountryCode, CurrencyCode, EmailAddress, Hostname, IpAddress, LanguageCode, MonetaryAmount, Money, Percentage, PhoneNumber, Slug, Url, CompositeValueObjectJsonConverter<T>, PrimitiveValueObjectTraceProviderBuilderExtensions]
version: v3
last_verified: 2026-06-03
audience: [llm]
---
# Trellis API Primitives

**Package:** `Trellis.Primitives`  
**Namespaces:** `Trellis`, `Trellis.Primitives`  
**Purpose:** the 13 built-in concrete value objects (`Age`, `CountryCode`, `CurrencyCode`, `EmailAddress`, `Hostname`, `IpAddress`, `LanguageCode`, `MonetaryAmount`, `Money`, `Percentage`, `PhoneNumber`, `Slug`, `Url`) plus Primitives-owned VO-runtime infrastructure (`CompositeValueObjectJsonConverter<T>`, `PrimitiveValueObjectTraceProviderBuilderExtensions`).

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-1--crud-aggregate-ddd-value-objects--entity--repository-contract) — recipes using this package.

> **Package scope.** The `Required*<TSelf>` base classes (`RequiredString`, `RequiredEnum`, `RequiredInt`, `RequiredLong`, `RequiredDecimal`, `RequiredGuid`, `RequiredBool`, `RequiredDateTime`, `RequiredDateTimeOffset`), validation attributes (`StringLengthAttribute`, `RangeAttribute`, `EnumValueAttribute`), strict-default opt-out attributes (`AllowEmptyAttribute`, `AllowWhitespaceAttribute`, `NoTrimAttribute`, `AllowZeroAttribute`, `AllowMinValueAttribute`), vestigial compatibility attributes (`NotDefaultAttribute`, `TrimAttribute`), `StringExtensions` (`NormalizeFieldName`, `ToCamelCase`, `ParseScalarValue`, `TryParseScalarValue`), `ParsableJsonConverter<T>`, `PrimitiveValueObjectTrace`, and `RequiredEnumJsonConverter<TRequiredEnum>` live in `Trellis.Core`. The base contracts (`IScalarValue<TSelf, TPrimitive>`, `IFormattableScalarValue<TSelf, TPrimitive>`) and base classes (`ValueObject`, `ScalarValueObject<TSelf, T>`) also live in `Trellis.Core`. `Trellis.Primitives` ships the concrete VOs that build on those bases plus the composite JSON converter and OpenTelemetry registration extension listed below. See [trellis-api-core.md](trellis-api-core.md#primitive-value-object-base-classes) for the base-type reference.
>
> The incremental generator that emits the `TryCreate`/`Create`/`Parse`/`TryParse`/`JsonConverter` partial bodies for `Required*<TSelf>` derivations (`Trellis.Core.Generator`) is bundled inside `Trellis.Core.nupkg` under `analyzers/dotnet/cs/`. `Trellis.Primitives` no longer references its own generator package — installing `Trellis.Core` (or transitively, `Trellis.Primitives` which depends on it) attaches the analyzer automatically.

## Use this file when

- You need one of the ready-made concrete value objects such as `EmailAddress`, `PhoneNumber`, `Money`, `CurrencyCode`, `Url`, or `Slug`.
- You need the composite value-object JSON converter or OpenTelemetry registration extension for primitives shipped by `Trellis.Primitives`.
- You are deciding whether to use a built-in primitive or define a custom `Required*<TSelf>` value object from `Trellis.Core`.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Validate an email string | `EmailAddress.TryCreate(...)` | [`EmailAddress`](#emailaddress) |
| Validate optional phone input | `PhoneNumber.TryCreate(...)` and wrap absence with `Maybe<PhoneNumber>` at the domain seam | [`PhoneNumber`](#phonenumber), [Core `Maybe<T>`](trellis-api-core.md#public-readonly-struct-maybet-where-t--notnull) |
| Represent money | `Money` / `MonetaryAmount` / `CurrencyCode` | [`Money`](#money), [`MonetaryAmount`](#monetaryamount), [`CurrencyCode`](#currencycode) |
| Bind/serialize built-in scalar primitives | Use generated converters from the primitive/base contracts; ASP validation is in `Trellis.Asp` | [`ParsableJsonConverter<T>`](trellis-api-core.md#parsablejsonconvertert), [ASP validation](trellis-api-asp.md#namespace-trellisaspvalidation) |
| Define a custom SKU/order-id primitive | Use `partial class Sku : RequiredString<Sku>` or `partial class OrderId : RequiredGuid<OrderId>` from `Trellis.Core` | [Core primitive base classes](trellis-api-core.md#primitive-value-object-base-classes) |
| Opt out of strict Required defaults | Use `[AllowEmpty]`, `[AllowWhitespace]`, `[NoTrim]`, `[AllowZero]`, or `[AllowMinValue]` on the generated primitive class | [`Required*` defaults and opt-outs](#required-defaults-and-opt-outs) |
| Add length/range constraints to custom primitives | Use Trellis `[StringLength]` / `[Range]` attributes from `namespace Trellis`, not DataAnnotations | [Core attributes](trellis-api-core.md#primitive-value-object-base-classes) |
| Add JSON for composite value objects | `CompositeValueObjectJsonConverter<T>` | [`CompositeValueObjectJsonConverter<T>`](#compositevalueobjectjsonconvertert) |

## Common traps

- This file documents concrete primitives. Custom primitive base classes and Trellis validation attributes live in [trellis-api-core.md](trellis-api-core.md#primitive-value-object-base-classes).
- `Required*<TSelf>` generated primitives are strict by default. Remove legacy `[NotDefault]` / `[Trim]`; use the per-base opt-outs below only when the sentinel value is a legitimate domain value.
- Use Trellis attributes when defining generated primitives. On the value-object class the similarly named DataAnnotations attributes do not compile (`CS0592`, or `CS0104` for an unqualified attribute when both namespaces are in scope); on a member they compile but the Trellis generator ignores them.
- Keep primitive parsing out of handlers. Convert transport primitives to value objects at the DTO/controller/application seam, then pass shaped commands inward.

### Trellis validation attributes vs `System.ComponentModel.DataAnnotations`

The Trellis validation attributes are **class-targeted** with **different shapes** than the same-named DataAnnotations types. Decorate the `partial class` definition of a `Required*<TSelf>`-derived value object, not a property. `[StringLength]` supports both `MaximumLength` (positional ctor argument) and `MinimumLength` (property initializer); the missing piece relative to DataAnnotations is `[RegularExpression]` — pattern checks go in `static partial void ValidateAdditional(string value, string fieldName, ref string? error)`.

**Class-targeted (apply to the `partial class` of a `Required*<TSelf>`-derived value object):**

| Attribute | Target | Constructor(s) | Use it for | DataAnnotations equivalent that does **not** work |
|---|---|---|---|---|
| `Trellis.StringLengthAttribute` | `class` (on the `partial class X : RequiredString<X>`) | `StringLengthAttribute(int maximumLength)` (use `MinimumLength = N` initializer for the lower bound; `maximumLength` must be `>= 1`) | Length constraint on a `RequiredString<TSelf>` value object. Generated `TryCreate` enforces `MinimumLength <= length <= MaximumLength` after the null/empty/whitespace check. | `[System.ComponentModel.DataAnnotations.StringLength(...)]` on the class fails with `CS0592` (DataAnnotations targets `Property | Field | Parameter`, not `Class`); on a member it compiles but is ignored by the Trellis generator. |
| `Trellis.RangeAttribute` | `class` (on the `partial class X : RequiredInt<X>` / `RequiredLong<X>` / `RequiredDecimal<X>`) | `(int min, int max)`, `(long min, long max)`, `(double min, double max)` | Numeric range constraint. The constructor selected determines which generator template fires. | `[Range(typeof(decimal), "0.01", "999999.99")]` — use `(double, double)` instead. |
| **Pattern / regex** | n/a | n/a | Override `static partial void ValidateAdditional(string value, string fieldName, ref string? error)` and run a `Regex.IsMatch(...)`. There is no `[RegularExpression]` attribute analog. | `[RegularExpression(@"^[A-Z]{3}\d{4}$")]` — silently does nothing on a `Required*<TSelf>` class. |

**Field-targeted (apply to `public static readonly` members of a `RequiredEnum<TSelf>`-derived value object):**

`Trellis.EnumValueAttribute(string value)` overrides the external symbolic name for a single enum member. Apply it to a `public static readonly TSelf` field whose canonical name should differ from the C# field identifier (e.g., when serializing `Status.InProgress` as `"in-progress"`). Without the attribute, `RequiredEnum<TSelf>` falls back to the field name. **This is the only Trellis primitive attribute that targets `AttributeTargets.Field` and takes a `string` argument** — do not include it in the class-targeted table above.

A property-targeted form like `[StringLength(20, MinimumLength = 3)] public string Value { get; }` produces `CS0592: Attribute is not valid on this declaration type` only if `[StringLength]` resolves to `Trellis.StringLengthAttribute`, which targets `AttributeTargets.Class` only. The DataAnnotations attribute of the same name targets properties/fields/parameters and compiles in those positions, but the Trellis source generator only inspects attributes on the partial class declaration, so a DataAnnotations attribute on a member of a `Required*<TSelf>`-derived class is silently ignored. A DataAnnotations `[StringLength]`/`[Range]` applied to the class itself is a compile error (`CS0592`, or `CS0104` for an unqualified attribute when both namespaces are in scope).


## `Required*` defaults and opt-outs

`Required*<TSelf>` generated primitives are **strict by default**. The generator rejects `null` for every base; it also rejects each base's sentinel value unless the matching opt-out attribute is present. `[NotDefault]` and `[Trim]` are vestigial compatibility attributes: remove them from new code. The generator ignores them and emits informational diagnostics (`TRLS046`, `TRLS047`). `[AllowDefault]` was removed; use the per-type opt-out name instead.

| Base | Default rejects | Opt-out |
|---|---|---|
| `RequiredString<T>` | `null`, `""`, whitespace-only | `[AllowEmpty]`, `[AllowWhitespace]`, `[NoTrim]` |
| `RequiredGuid<T>` | `null`, `Guid.Empty` | `[AllowEmpty]` |
| `RequiredDateTime<T>` | `null`, `DateTime.MinValue` | `[AllowMinValue]` |
| `RequiredDateTimeOffset<T>` | `null`, `DateTimeOffset.MinValue` | `[AllowMinValue]` |
| `RequiredInt<T>` | `null`, `0` | `[AllowZero]` |
| `RequiredLong<T>` | `null`, `0` | `[AllowZero]` |
| `RequiredDecimal<T>` | `null`, `0m` | `[AllowZero]` |
| `RequiredBool<T>` | `null` | (no opt-out — degenerate) |
| `RequiredEnum<T>` | `null`, undeclared members | (handled by smart-enum lookup) |

### `RequiredString<T>` truth table

| Attribute(s) | `null` | `""` | `"   "` | `" a "` | `"a"` |
|---|---|---|---|---|---|
| (none) | reject | reject | reject | accept `"a"` | accept `"a"` |
| `[AllowEmpty]` | reject | accept `""` | reject | accept `"a"` | accept `"a"` |
| `[AllowWhitespace]` | reject | reject | accept `""` | accept `"a"` | accept `"a"` |
| `[NoTrim]` | reject | reject | reject | accept `" a "` | accept `"a"` |
| `[AllowEmpty, AllowWhitespace]` | reject | accept `""` | accept `""` | accept `"a"` | accept `"a"` |
| `[AllowEmpty, NoTrim]` | reject | accept `""` | reject | accept `" a "` | accept `"a"` |
| `[AllowWhitespace, NoTrim]` | reject | reject | accept `"   "` | accept `" a "` | accept `"a"` |
| `[AllowEmpty, AllowWhitespace, NoTrim]` | reject | accept `""` | accept `"   "` | accept `" a "` | accept `"a"` |

Validation order:

1. **null check** (no opt-out)
2. **whitespace-only check on raw input** (skipped if `[AllowWhitespace]`)
3. **trim** (skipped if `[NoTrim]`)
4. **empty check on final input** (skipped if `[AllowEmpty]` OR raw was whitespace AND `[AllowWhitespace]`)
5. user-supplied constraints (`[StringLength]`, `ValidateAdditional`)

> [!IMPORTANT]
> `[AllowWhitespace]` alone (no `[NoTrim]`) accepts whitespace-only input but trim normalizes it to `""` for storage. To preserve whitespace verbatim, combine with `[NoTrim]`.

### Required opt-out diagnostics

| ID | Severity | When |
|---|---|---|
| TRLS046 | Info | `[NotDefault]` is vestigial — strict behavior is the new default |
| TRLS047 | Info | `[Trim]` is vestigial — trim is the new default on `RequiredString` |
| TRLS048 | Error | `[AllowZero]` on a non-numeric Required base |
| TRLS049 | Error | `[AllowEmpty]` on a numeric / date Required base |
| TRLS050 | Error | `[AllowMinValue]` on a non-date Required base |
| TRLS051 | Error | `[AllowWhitespace]` on a non-string Required base |
| TRLS052 | Error | `[NoTrim]` on a non-string Required base |
| TRLS053 | Error | Contradictory combinations (e.g. `[AllowZero]` + `[Positive]`) |

## Types

> Base contracts (`IScalarValue<TSelf, TPrimitive>`, `IFormattableScalarValue<TSelf, TPrimitive>`), base classes (`ValueObject`, `ScalarValueObject<TSelf, T>`), validation and opt-out attributes (`RangeAttribute`, `StringLengthAttribute`, `EnumValueAttribute`, `AllowEmptyAttribute`, `AllowWhitespaceAttribute`, `NoTrimAttribute`, `AllowZeroAttribute`, `AllowMinValueAttribute`), vestigial compatibility attributes (`NotDefaultAttribute`, `TrimAttribute`), `StringExtensions`, the `Required*<TSelf>` base classes, `ParsableJsonConverter<T>`, `PrimitiveValueObjectTrace`, and `RequiredEnumJsonConverter<TRequiredEnum>` are all documented in [trellis-api-core.md](trellis-api-core.md#primitive-value-object-base-classes). They live in `Trellis.Core` and are used by every concrete VO listed below. `Trellis.Primitives` type-forwards `ParsableJsonConverter<T>` and `PrimitiveValueObjectTrace` for binary compatibility, but new source guidance should treat Core as the owner. The inherited `static TSelf Create(TPrimitive value)` factory documented on `ScalarValueObject<TSelf, T>` is **not** repeated on each concrete VO below.

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No additional public methods. |

### `PrimitiveValueObjectTraceProviderBuilderExtensions`

```csharp
public static class PrimitiveValueObjectTraceProviderBuilderExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Static extension container. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TracerProviderBuilder AddPrimitiveValueObjectInstrumentation(this TracerProviderBuilder builder)` | `TracerProviderBuilder` | Registers the Core-owned Trellis primitive activity source (`PrimitiveValueObjectTrace.ActivitySourceName`) with OpenTelemetry. Throws `ArgumentNullException` when `builder` is null. |

### `Age`

```csharp
public class Age : ScalarValueObject<Age, int>, IScalarValue<Age, int>, IFormattableScalarValue<Age, int>, IParsable<Age>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `int` | Age in years. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<Age> TryCreate(int value, string? fieldName = null)` | `Result<Age>` | Validates `0 <= value <= 150`. |
| `public static Result<Age> TryCreate(string? value, string? fieldName = null)` | `Result<Age>` | Invariant string parsing. |
| `public static Result<Age> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` | `Result<Age>` | Culture-aware string parsing. |
| `public static Age Parse(string? s, IFormatProvider? provider)` | `Age` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Age result)` | `bool` | Safe parse helper. |

### `CountryCode`

```csharp
public class CountryCode : ScalarValueObject<CountryCode, string>, IScalarValue<CountryCode, string>, IParsable<CountryCode>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | ISO 3166-1 alpha-2 code, stored uppercase. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<CountryCode> TryCreate(string? value, string? fieldName = null)` | `Result<CountryCode>` | Requires exactly two letters. |
| `public static CountryCode Parse(string? s, IFormatProvider? provider)` | `CountryCode` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out CountryCode result)` | `bool` | Safe parse helper. |

### `CurrencyCode`

```csharp
public class CurrencyCode : ScalarValueObject<CurrencyCode, string>, IScalarValue<CurrencyCode, string>, IParsable<CurrencyCode>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | ISO 4217 code, stored uppercase. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<CurrencyCode> TryCreate(string? value, string? fieldName = null)` | `Result<CurrencyCode>` | Requires exactly three ASCII letters per ISO 4217 *format*. Input is case-insensitive — `"usd"`, `"USD"`, and `"Usd"` are all accepted; the stored value is uppercase via `ToUpperInvariant()`. The ISO 4217 *active-code list* is not enforced — syntactically valid but reserved or unassigned codes such as `XXX`, `XTS`, `ZZZ` are accepted. Applications that need active-currency enforcement (e.g., payment processors that only support a subset, or excluding the ISO test/reserved codes) should layer an allow-list at the application boundary. |
| `public static CurrencyCode Parse(string? s, IFormatProvider? provider)` | `CurrencyCode` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out CurrencyCode result)` | `bool` | Safe parse helper. |

### `EmailAddress`

```csharp
public partial class EmailAddress : ScalarValueObject<EmailAddress, string>, IScalarValue<EmailAddress, string>, IParsable<EmailAddress>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Trimmed email string. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<EmailAddress> TryCreate(string? value, string? fieldName = null)` | `Result<EmailAddress>` | Regex-based email validation. |
| `public static EmailAddress Parse(string? s, IFormatProvider? provider)` | `EmailAddress` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out EmailAddress result)` | `bool` | Safe parse helper. |

### `Hostname`

```csharp
public partial class Hostname : ScalarValueObject<Hostname, string>, IScalarValue<Hostname, string>, IParsable<Hostname>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | RFC 1123 hostname. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<Hostname> TryCreate(string? value, string? fieldName = null)` | `Result<Hostname>` | RFC 1123 hostname validation. |
| `public static Hostname Parse(string? s, IFormatProvider? provider)` | `Hostname` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Hostname result)` | `bool` | Safe parse helper. |

### `IpAddress`

```csharp
public class IpAddress : ScalarValueObject<IpAddress, string>, IScalarValue<IpAddress, string>, IParsable<IpAddress>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Original trimmed IPv4/IPv6 text. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<IpAddress> TryCreate(string? value, string? fieldName = null)` | `Result<IpAddress>` | Uses `IPAddress.TryParse`. |
| `public IPAddress ToIPAddress()` | `IPAddress` | Returns cached parsed address. |
| `public static IpAddress Parse(string? s, IFormatProvider? provider)` | `IpAddress` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out IpAddress result)` | `bool` | Safe parse helper. |

### `LanguageCode`

```csharp
public class LanguageCode : ScalarValueObject<LanguageCode, string>, IScalarValue<LanguageCode, string>, IParsable<LanguageCode>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | ISO 639-1 alpha-2 code, stored lowercase. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<LanguageCode> TryCreate(string? value, string? fieldName = null)` | `Result<LanguageCode>` | Requires exactly two letters. |
| `public static LanguageCode Parse(string? s, IFormatProvider? provider)` | `LanguageCode` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out LanguageCode result)` | `bool` | Safe parse helper. |

### `MonetaryAmount`

```csharp
public class MonetaryAmount : ScalarValueObject<MonetaryAmount, decimal>, IScalarValue<MonetaryAmount, decimal>, IFormattableScalarValue<MonetaryAmount, decimal>, IParsable<MonetaryAmount>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `decimal` | Rounded non-negative amount without currency. |
| `Zero` | `MonetaryAmount` | Cached `0m` instance. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<MonetaryAmount> TryCreate(decimal value, string? fieldName = null)` | `Result<MonetaryAmount>` | Rejects negatives; rounds to two decimal places using `MidpointRounding.AwayFromZero`. |
| `public static Result<MonetaryAmount> TryCreate(decimal? value, string? fieldName = null)` | `Result<MonetaryAmount>` | Rejects `null`. |
| `public static Result<MonetaryAmount> TryCreate(string? value, string? fieldName = null)` | `Result<MonetaryAmount>` | Invariant string parsing. |
| `public static Result<MonetaryAmount> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` | `Result<MonetaryAmount>` | Culture-aware string parsing. |
| `public Result<MonetaryAmount> Add(MonetaryAmount other)` | `Result<MonetaryAmount>` | Adds two amounts. Throws `ArgumentNullException` when `other` is null. |
| `public Result<MonetaryAmount> Subtract(MonetaryAmount other)` | `Result<MonetaryAmount>` | Subtracts and fails if result would become invalid. Throws `ArgumentNullException` when `other` is null. |
| `public Result<MonetaryAmount> Multiply(int quantity)` | `Result<MonetaryAmount>` | Rejects negative quantity. |
| `public Result<MonetaryAmount> Multiply(decimal multiplier)` | `Result<MonetaryAmount>` | Rejects negative multiplier. |
| `public static MonetaryAmount Parse(string? s, IFormatProvider? provider)` | `MonetaryAmount` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out MonetaryAmount result)` | `bool` | Safe parse helper. |
| `public static explicit operator MonetaryAmount(decimal value)` | `MonetaryAmount` | Explicit cast using `Create(decimal)`. |
| `public override string ToString()` | `string` | Invariant decimal string. |
| `public static Result<MonetaryAmount> Sum(IEnumerable<MonetaryAmount> values)` | `Result<MonetaryAmount>` | Returns `Zero` for empty collections. Throws `ArgumentNullException` when `values` is null and `ArgumentException` when any element is null. |

### `CompositeValueObjectJsonConverter<T>`

```csharp
public sealed class CompositeValueObjectJsonConverter<T> : JsonConverter<T>
    where T : ValueObject
```

Convention-based JSON converter for composite value objects. Each public read-only instance property
becomes a JSON field (camelCase of the property name). The "primitive type" for each field is the
underlying primitive of an `IScalarValue<TSelf, TPrimitive>` property, or the property's own type when it
is already a primitive. The target type must expose a public static
`Result<T> TryCreate(p1, ..., pN[, string? fieldName])` whose parameters are the primitive types in the
order the properties are declared.

| Signature | Returns | Description |
| --- | --- | --- |
| `public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `T?` | Reads a JSON object, populates parameters by JSON property name (case-insensitive), invokes `TryCreate`, and throws `TrellisJsonValidationException` with the error display message on failure. When required properties are missing, throws a single `TrellisJsonValidationException` listing **all** missing names (e.g. `Required properties missing: 'amount', 'currency'.`) so multi-field violations surface in one round trip. |
| `public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)` | `void` | Writes one JSON property per public instance property in declaration order, using the underlying primitive value for `IScalarValue<,>` properties. |

Apply via `[JsonConverter(typeof(CompositeValueObjectJsonConverter<MyVo>))]` on the value object type.
Reflection is performed once per generic instantiation and cached. **Not Native AOT compatible** — for AOT
scenarios, hand-write a `JsonConverter<T>`.

> **Pattern reference.** For the full Domain + API JSON binding + EF Core ownership walkthrough on a multi-field VO (`ShippingAddress`-style), see [Cookbook Recipe 13](trellis-api-cookbook.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership). Without this `[JsonConverter]` attribute on a request DTO's composite `[OwnedEntity]` property, model binding falls back to default construction and **silently bypasses `TryCreate`** — the inner-field validation never runs and an invalid payload propagates into the domain layer.

### `Money`

```csharp
public class Money : ValueObject
```

| Name | Type | Description |
| --- | --- | --- |
| `Amount` | `decimal` | Currency-aware rounded amount. |
| `Currency` | `CurrencyCode` | ISO 4217 currency code. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<Money> TryCreate(decimal amount, string currencyCode, string? fieldName = null)` | `Result<Money>` | Rejects negative amounts. Currency validation is delegated to [`CurrencyCode.TryCreate`](#currencycode) and is syntactic only — three ASCII letters (case-insensitive input; normalized to uppercase). Codes outside the ISO 4217 active list (`XXX`, `XTS`, `ZZZ`, etc.) are accepted because they satisfy the format. Layer an application-level allow-list when active-code enforcement is required. |
| `public static Money Create(decimal amount, string currencyCode)` | `Money` | Throwing factory. |
| `public Result<Money> Add(Money other)` | `Result<Money>` | Requires matching currencies. Throws `ArgumentNullException` when `other` is null. Returns `Error.InvalidInput` on currency mismatch or addition overflow. |
| `public Result<Money> Subtract(Money other)` | `Result<Money>` | Requires matching currencies and non-negative result. Throws `ArgumentNullException` when `other` is null. |
| `public Result<Money> Multiply(decimal multiplier)` | `Result<Money>` | Rejects negative multiplier. Returns `Error.InvalidInput` on multiplication overflow. |
| `public Result<Money> Multiply(int quantity)` | `Result<Money>` | Rejects negative quantity. Returns `Error.InvalidInput` on multiplication overflow. |
| `public Result<Money> Divide(decimal divisor)` | `Result<Money>` | Divisor must be positive. Returns `Error.InvalidInput` when division would overflow (e.g. very small positive divisor). |
| `public Result<Money> Divide(int divisor)` | `Result<Money>` | Divisor must be positive. Returns `Error.InvalidInput` on division overflow. |
| `public Result<Money[]> Allocate(params int[] ratios)` | `Result<Money[]>` | Ratio-based split with remainder distribution. Throws `ArgumentNullException` when `ratios` is null. Returns `Error.InvalidInput` with field `ratios` when any ratio is non-positive or `ratios.Sum()` overflows; with field `amount` when the minor-unit conversion or the per-ratio share multiplication (`amountInMinorUnits * ratios[i]`, computed in a checked context) overflows. |
| `public bool IsGreaterThan(Money other)` | `bool` | False when currencies differ. Throws `ArgumentNullException` when `other` is null. |
| `public bool IsGreaterThanOrEqual(Money other)` | `bool` | False when currencies differ. Throws `ArgumentNullException` when `other` is null. |
| `public bool IsLessThan(Money other)` | `bool` | False when currencies differ. Throws `ArgumentNullException` when `other` is null. |
| `public bool IsLessThanOrEqual(Money other)` | `bool` | False when currencies differ. Throws `ArgumentNullException` when `other` is null. |
| `public static Result<Money> Zero(string currencyCode = "USD")` | `Result<Money>` | Currency-aware zero instance. |
| `public override string ToString()` | `string` | Invariant amount plus currency code. |
| `public static Result<Money> Sum(IEnumerable<Money> values)` | `Result<Money>` | Fails for empty or mixed-currency collections. Throws `ArgumentNullException` when `values` is null and `ArgumentException` when any element is null. |
| `public static Result<Money> Sum(IEnumerable<Money> values, Money fallback)` | `Result<Money>` | Returns `fallback` when `values` is empty. When `values` is non-empty the result currency is inferred from the first element exactly as `Sum(values)` — `fallback`'s currency is ignored. Mirrors `MonetaryAmount.Sum`'s empty-yields-zero ergonomic when the caller has a meaningful currency for the empty case. Throws `ArgumentNullException` when `values` or `fallback` is null and `ArgumentException` when any element is null. |

### `Percentage`

```csharp
public class Percentage : ScalarValueObject<Percentage, decimal>, IScalarValue<Percentage, decimal>, IFormattableScalarValue<Percentage, decimal>, IParsable<Percentage>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `decimal` | Percentage value in the range `0` to `100`. |
| `Zero` | `Percentage` | Cached `0%` instance. |
| `Full` | `Percentage` | Cached `100%` instance. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<Percentage> TryCreate(decimal value, string? fieldName = null)` | `Result<Percentage>` | Rejects values outside `0..100`. |
| `public static Result<Percentage> TryCreate(decimal? value, string? fieldName = null)` | `Result<Percentage>` | Rejects `null`. |
| `public static Result<Percentage> TryCreate(string? value, string? fieldName = null)` | `Result<Percentage>` | Invariant string parsing; trims an optional trailing `%`. |
| `public static Result<Percentage> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` | `Result<Percentage>` | Culture-aware string parsing; trims an optional trailing `%`. |
| `public static Result<Percentage> FromFraction(decimal fraction, string? fieldName = null)` | `Result<Percentage>` | Converts `0..1` fractions into `0..100` percentages. |
| `public decimal AsFraction()` | `decimal` | Converts `Value` to a `0..1` fraction. |
| `public decimal Of(decimal amount)` | `decimal` | Calculates this percentage of `amount`. |
| `public static Percentage Parse(string? s, IFormatProvider? provider)` | `Percentage` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Percentage result)` | `bool` | Safe parse helper. |
| `public static explicit operator Percentage(decimal value)` | `Percentage` | Explicit cast using `Create(decimal)`. |
| `public override string ToString()` | `string` | Appends `%` to `Value`. |

### `PhoneNumber`

```csharp
public partial class PhoneNumber : ScalarValueObject<PhoneNumber, string>, IScalarValue<PhoneNumber, string>, IParsable<PhoneNumber>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Normalized E.164 phone number. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<PhoneNumber> TryCreate(string? value, string? fieldName = null)` | `Result<PhoneNumber>` | Removes spaces, dashes, and parentheses, then validates E.164. |
| `public static PhoneNumber Parse(string? s, IFormatProvider? provider)` | `PhoneNumber` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out PhoneNumber result)` | `bool` | Safe parse helper. |
| `public Maybe<string> GetCountryCode()` | `Maybe<string>` | Extracts the E.164 country calling code via longest-prefix lookup. Returns `Maybe.From(code)` when the prefix matches an assigned ITU-T calling code; returns `Maybe<string>.None` when the prefix is unrecognized (`TryCreate` validates only E.164 *shape*, not assigned-code membership). |

### `Slug`

```csharp
public partial class Slug : ScalarValueObject<Slug, string>, IScalarValue<Slug, string>, IParsable<Slug>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Lowercase slug. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<Slug> TryCreate(string? value, string? fieldName = null)` | `Result<Slug>` | Validates lowercase letters, digits, and single hyphen separators. |
| `public static Slug Parse(string? s, IFormatProvider? provider)` | `Slug` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Slug result)` | `bool` | Safe parse helper. |

### `Url`

```csharp
public class Url : ScalarValueObject<Url, string>, IScalarValue<Url, string>, IParsable<Url>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Absolute URI string. |
| `Scheme` | `string` | URI scheme. |
| `Host` | `string` | URI host. |
| `Port` | `int` | URI port. |
| `Path` | `string` | Absolute path. |
| `Query` | `string` | Query string, including leading `?`. |
| `IsSecure` | `bool` | True for HTTPS URLs. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<Url> TryCreate(string? value, string? fieldName = null)` | `Result<Url>` | Requires an absolute HTTP or HTTPS URI. |
| `public Uri ToUri()` | `Uri` | Returns cached `Uri`. |
| `public static Url Parse(string? s, IFormatProvider? provider)` | `Url` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Url result)` | `bool` | Safe parse helper. |

## Base class hierarchy

The base classes (`ValueObject`, `ScalarValueObject<TSelf, T>`, `RequiredString<TSelf>`, etc.) live in `Trellis.Core` — see [trellis-api-core.md](trellis-api-core.md#primitive-value-object-base-classes) for the full hierarchy. The concrete primitives in this package layer on top:

- Built-in scalars:
  - `Age`, `CountryCode`, `CurrencyCode`, `EmailAddress`, `Hostname`, `IpAddress`, `LanguageCode`, `MonetaryAmount`, `Percentage`, `PhoneNumber`, `Slug`, `Url` -> `ScalarValueObject<TSelf, T>` -> `ValueObject`
- Structured built-in:
  - `Money` -> `ValueObject`

## Built-in primitives table

| Type | Namespace | Category | Underlying/wire shape | Notes |
| --- | --- | --- | --- | --- |
| `Age` | `Trellis.Primitives` | Scalar | JSON number or numeric string input; JSON number output | `int`, range `0..150`. |
| `CountryCode` | `Trellis.Primitives` | Scalar | JSON string | Uppercase ASCII ISO 3166-1 alpha-2 (exactly two ASCII letters). |
| `CurrencyCode` | `Trellis.Primitives` | Scalar | JSON string | Uppercase ASCII ISO 4217 (exactly three ASCII letters). |
| `EmailAddress` | `Trellis.Primitives` | Scalar | JSON string | Trimmed validated email. |
| `Hostname` | `Trellis.Primitives` | Scalar | JSON string | RFC 1123 hostname. |
| `IpAddress` | `Trellis.Primitives` | Scalar | JSON string | IPv4 or IPv6 text. |
| `LanguageCode` | `Trellis.Primitives` | Scalar | JSON string | Lowercase ASCII ISO 639-1 alpha-2. |
| `MonetaryAmount` | `Trellis.Primitives` | Scalar | JSON number or numeric string input; JSON number output | Non-negative single-currency amount with 2-decimal rounding. |
| `Money` | `Trellis.Primitives` | Structured | JSON object `{ "amount": number, "currency": string }` | Multi-currency value object; not scalar. Decimal places per ISO 4217 minor units (0 for JPY/KRW/BIF/CLP/DJF/GNF/ISK/KMF/PYG/RWF/UGX/UYI/VND/VUV/XAF/XOF/XPF; 3 for BHD/IQD/JOD/KWD/LYD/OMR/TND; 4 for CLF/UYW; 2 otherwise). |
| `Percentage` | `Trellis.Primitives` | Scalar | JSON number or numeric string input; JSON number output | `decimal` in `0..100`; `ToString()` adds `%`. |
| `PhoneNumber` | `Trellis.Primitives` | Scalar | JSON string | Normalized E.164 string. `GetCountryCode()` returns `Maybe<string>.None` when the prefix is not an assigned ITU-T calling code. |
| `Slug` | `Trellis.Primitives` | Scalar | JSON string | Lowercase letters, digits, single hyphens. |
| `Url` | `Trellis.Primitives` | Scalar | JSON string | Absolute HTTP/HTTPS URI. |

## Code examples

```csharp
using Trellis;
using Trellis.Primitives;

namespace Demo;

public static class Example
{
    public static void Run()
    {
        var email = EmailAddress.Create("ada@example.com");
        var country = CountryCode.Create("US");
        var phone = PhoneNumber.Create("+14155551234");

        var percentage = Percentage.FromFraction(0.15m).TryGetValue(out var p) ? p : Percentage.Zero;
        var amount = MonetaryAmount.Create(12.34m);
        var taxAmount = percentage.Of(amount);

        var total = Money.Create(12.34m, "USD");
        var shipping = Money.Create(2.00m, "USD");
        var grandTotal = total.Add(shipping).TryGetValue(out var gt) ? gt : total;

        _ = (email, country, phone, taxAmount, grandTotal);
    }
}
```

For examples of building **your own** primitives by deriving from `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, `RequiredEnum<TSelf>`, etc., see [trellis-api-core.md](trellis-api-core.md#primitive-value-object-base-classes).

## Cross-references

- [trellis-api-core.md](trellis-api-core.md#primitive-value-object-base-classes) — `Required*<TSelf>` base classes, validation attributes (`StringLengthAttribute`, `RangeAttribute`, `EnumValueAttribute`), `StringExtensions`, and the `IScalarValue<TSelf, TPrimitive>` / `IFormattableScalarValue<TSelf, TPrimitive>` contracts.
- [trellis-api-efcore.md](trellis-api-efcore.md#modelconfigurationbuilderextensions) — EF Core mapping conventions for `ValueObject`, `ScalarValueObject<TSelf, T>`, and the built-in primitives in this package.
- [trellis-value-object-taxonomy.md](trellis-value-object-taxonomy.md) — how the built-in primitives fit into the broader VO taxonomy.
