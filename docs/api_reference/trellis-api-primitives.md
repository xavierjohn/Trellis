# Trellis.Primitives — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-ddd.md, trellis-api-efcore.md.

**Package:** `Trellis.Primitives` | **Namespaces:** `Trellis` (base types), `Trellis.Primitives` (concrete VOs)

## JSON Converters (namespace: `Trellis`)

Trellis provides automatic JSON serialization for all value objects. `ParsableJsonConverter` handles scalar types; `MoneyJsonConverter` handles the `Money` composite type.

### ParsableJsonConverter\<T\>

Generic `System.Text.Json` converter for all types implementing `IParsable<T>`. Auto-applied via `[JsonConverter]` on source-generated value objects.

```csharp
public class ParsableJsonConverter<T> : JsonConverter<T> where T : IParsable<T>
```

Reads via `T.Parse(reader.GetString()!)`; writes via `writer.WriteStringValue(value.ToString())`.

### MoneyJsonConverter (namespace: `Trellis.Primitives`)

Serializes/deserializes `Money` as `{"amount": 99.99, "currency": "USD"}`.

```csharp
public class MoneyJsonConverter : JsonConverter<Money>
```

## Base Types (namespace: `Trellis`)

Value object base classes. Declare a `partial class` inheriting from these to trigger source generation of `TryCreate`, `Create`, `Parse`, JSON converters, and model binding.

### RequiredString\<TSelf\>

Inherits `ScalarValueObject<TSelf, string>`. Source generator provides on each `partial class Foo : RequiredString<Foo>`:

```csharp
// Auto-generated
static Result<Foo> TryCreate(string? value, string? fieldName = null)  // rejects null/empty/whitespace, auto-trims
static Foo Create(string? value, string? fieldName = null)
static explicit operator Foo(string value)
// IParsable<Foo>: Parse, TryParse
// [JsonConverter(typeof(ParsableJsonConverter<Foo>))]
```

#### `[StringLength]` — Optional Length Constraints

Apply `[StringLength(max)]` or `[StringLength(max, MinimumLength = min)]` to the class to add length validation into the generated `TryCreate`:

```csharp
[StringLength(50)]                        // max only
public partial class FirstName : RequiredString<FirstName> { }

[StringLength(500, MinimumLength = 10)]   // min + max
public partial class Description : RequiredString<Description> { }
```

Generated validation errors: `"{Name} must be at least {min} characters."`, `"{Name} must be {max} characters or fewer."`

> **Namespace note:** This is `Trellis.StringLengthAttribute`, not `System.ComponentModel.DataAnnotations.StringLengthAttribute`. If both namespaces are imported, disambiguate with `[Trellis.StringLength(max)]`.

#### `ValidateAdditional` — Optional Custom Validation Hook

Implement the `ValidateAdditional` partial method to add domain-specific validation (regex patterns, format checks, etc.). Called after built-in validations pass. If not implemented, the compiler removes the call — zero overhead.

```csharp
[StringLength(10)]
public partial class Sku : RequiredString<Sku>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
    {
        if (!Regex.IsMatch(value, @"^SKU-\d{6}$"))
            errorMessage = "Sku must match pattern SKU-XXXXXX.";
    }
}
```

**Signature:** `static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)`
- `value` — the validated string (not null, not whitespace, length-checked)
- `fieldName` — the normalized field name for error messages
- `errorMessage` — set to a non-null string to reject; leave null to accept. The generator wraps it in `Error.Validation(errorMessage, fieldName)` automatically.

### RequiredGuid\<TSelf\>

Inherits `ScalarValueObject<TSelf, Guid>`. Source generator provides:

```csharp
static Foo NewUniqueV4()
static Foo NewUniqueV7()
static Result<Foo> TryCreate(Guid value, string? fieldName = null)      // rejects Guid.Empty
static Result<Foo> TryCreate(Guid? value, string? fieldName = null)
static Result<Foo> TryCreate(string? value, string? fieldName = null)   // validates GUID format
static new Foo Create(Guid value)
static Foo Create(string stringValue)
static explicit operator Foo(Guid value)
// IParsable<Foo>: Parse, TryParse
// [JsonConverter(typeof(ParsableJsonConverter<Foo>))]
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Same pattern as RequiredString. Signature: `static partial void ValidateAdditional(Guid value, string fieldName, ref string? errorMessage)`

```csharp
public partial class TenantId : RequiredGuid<TenantId>
{
    static partial void ValidateAdditional(Guid value, string fieldName, ref string? errorMessage)
    {
        if (value.Version != 7)
            errorMessage = "Tenant Id must be a v7 UUID.";
    }
}
```

### RequiredInt\<TSelf\>

Inherits `ScalarValueObject<TSelf, int>`. Source generator provides:

```csharp
static Result<Foo> TryCreate(int value, string? fieldName = null)       // accepts any int
static Result<Foo> TryCreate(int? value, string? fieldName = null)     // rejects null
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static new Foo Create(int value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

With range constraints using `[Range]`:

```csharp
[Range(1, 999)]
public partial class LineItemQuantity : RequiredInt<LineItemQuantity> { }

[Range(0, 100)]  // constrains to 0–100 inclusive
public partial class StockQuantity : RequiredInt<StockQuantity> { }

// Generated TryCreate validates: min <= value <= max
// Error: "Line Item Quantity must be at least 1." / "Line Item Quantity must be at most 999."
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Same pattern as RequiredString. Signature: `static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage)`

```csharp
[Range(1, 100)]
public partial class EvenPercentage : RequiredInt<EvenPercentage>
{
    static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage)
    {
        if (value % 2 != 0)
            errorMessage = "Even Percentage must be an even number.";
    }
}
```

### RequiredDecimal\<TSelf\>

Inherits `ScalarValueObject<TSelf, decimal>`. Same pattern as RequiredInt with `decimal`.

```csharp
static Result<Foo> TryCreate(decimal value, string? fieldName = null)
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static Foo Create(decimal value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

#### `[Range]` — Optional Range Constraints

```csharp
[Range(1, 999)]           // whole-number bounds (int constructor)
public partial class UnitPrice : RequiredDecimal<UnitPrice> { }

[Range(0.01, 99.99)]      // fractional bounds (double constructor)
public partial class TaxRate : RequiredDecimal<TaxRate> { }
```

> **Note:** C# does not allow `decimal` in attribute constructors, so fractional ranges use the `double` constructor overload. The generated validation still operates on the `decimal` value.

`ValidateAdditional` is also available: `static partial void ValidateAdditional(decimal value, string fieldName, ref string? errorMessage)`

### RequiredLong\<TSelf\>

Inherits `ScalarValueObject<TSelf, long>`. Source generator provides:

```csharp
static Result<Foo> TryCreate(long value, string? fieldName = null)
static Result<Foo> TryCreate(long? value, string? fieldName = null)     // rejects null
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static new Foo Create(long value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

With range constraints using `[Range(long, long)]` — supports ranges exceeding `int.MaxValue`:

```csharp
[Range(0L, 5_000_000_000L)]
public partial class LargeId : RequiredLong<LargeId> { }

[Range(1L, 9_999_999_999L)]
public partial class PhoneNumber : RequiredLong<PhoneNumber> { }
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Same pattern as RequiredInt. Signature: `static partial void ValidateAdditional(long value, string fieldName, ref string? errorMessage)`

### RequiredBool\<TSelf\>

Inherits `ScalarValueObject<TSelf, bool>`. Distinguishes `false` (an explicit value) from `null`/missing — solves the "was the property `false` or not provided?" problem.

```csharp
static Result<Foo> TryCreate(bool value, string? fieldName = null)      // always succeeds
static Result<Foo> TryCreate(bool? value, string? fieldName = null)     // rejects null
static Result<Foo> TryCreate(string? value, string? fieldName = null)   // parses "true"/"false"
static new Foo Create(bool value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

```csharp
public partial class GiftWrap : RequiredBool<GiftWrap> { }

// Usage
var wrap = GiftWrap.Create(false);   // wrap.Value == false (explicitly false, not missing)
var result = GiftWrap.TryCreate(null as bool?);  // failure — null is not allowed
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Signature: `static partial void ValidateAdditional(bool value, string fieldName, ref string? errorMessage)`

### RequiredDateTime\<TSelf\>

Inherits `ScalarValueObject<TSelf, DateTime>`. Rejects `DateTime.MinValue` (the "empty" equivalent for `DateTime`). Overrides `ToString()` to use ISO 8601 round-trip format (`"O"`) for deterministic JSON serialization.

```csharp
static Result<Foo> TryCreate(DateTime value, string? fieldName = null)    // rejects DateTime.MinValue
static Result<Foo> TryCreate(DateTime? value, string? fieldName = null)   // rejects null
static Result<Foo> TryCreate(string? value, string? fieldName = null)     // invariant culture parsing
static new Foo Create(DateTime value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

```csharp
public partial class OrderDate : RequiredDateTime<OrderDate> { }

// Usage
var date = OrderDate.Create(DateTime.UtcNow);
var bad = OrderDate.TryCreate(DateTime.MinValue);  // failure — MinValue rejected
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Signature: `static partial void ValidateAdditional(DateTime value, string fieldName, ref string? errorMessage)`

### `[Range]` Attribute Reference

The `[Range]` attribute constrains numeric value objects at creation time. The source generator emits min/max validation into `TryCreate`.

| Constructor | Applies To | Example |
|---|---|---|
| `[Range(int min, int max)]` | `RequiredInt`, `RequiredDecimal` (whole numbers) | `[Range(1, 999)]` |
| `[Range(long min, long max)]` | `RequiredLong` (values exceeding `int.MaxValue`) | `[Range(0L, 5_000_000_000L)]` |
| `[Range(double min, double max)]` | `RequiredDecimal` (fractional bounds) | `[Range(0.01, 99.99)]` |

> **Namespace note:** This is `Trellis.RangeAttribute`, not `System.ComponentModel.DataAnnotations.RangeAttribute`. If both namespaces are imported, disambiguate with `[Trellis.Range(min, max)]`.

### RequiredEnum\<TSelf\>

**NOT a ScalarValueObject** — standalone hierarchy. Smart enum pattern.

```csharp
string Value { get; }      // semantic symbolic value; defaults to field name or [EnumValue(...)]
int Ordinal { get; }       // declaration-order metadata, not stable identity

static IReadOnlyCollection<TSelf> GetAll()
static Result<TSelf> TryFromName(string? name, string? fieldName = null)  // case-insensitive symbolic value lookup
bool Is(TSelf value)                               // allocation-free single-value check
bool Is(TSelf value1, TSelf value2)                // allocation-free two-value check
bool Is(params TSelf[] values)
bool IsNot(TSelf value)                            // allocation-free single-value check
bool IsNot(TSelf value1, TSelf value2)             // allocation-free two-value check
bool IsNot(params TSelf[] values)

// Source-generated:
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static Foo Create(string value)   // throws on invalid input (from IScalarValue)
// IParsable<Foo>, [JsonConverter(typeof(RequiredEnumJsonConverter<Foo>))]
```

Use `[EnumValue("code")]` only when the external name must differ from the default field name.

### EnumValueAttribute

Customizes the wire/storage name for a `RequiredEnum` member. Applied to static fields.

```csharp
[AttributeUsage(AttributeTargets.Field)]
public sealed class EnumValueAttribute(string value) : Attribute
{
    public string Value { get; }
}

// Usage — custom wire name different from field name
public partial class PaymentMethod : RequiredEnum<PaymentMethod>
{
    [EnumValue("credit-card")]
    public static readonly PaymentMethod CreditCard = new();

    [EnumValue("bank-transfer")]
    public static readonly PaymentMethod BankTransfer = new();
}
```

### RequiredEnumJsonConverter\<T\>

JSON converter for `RequiredEnum<T>` types. Auto-applied by the source generator via `[JsonConverter(typeof(RequiredEnumJsonConverter<T>))]`. Serializes to/from the string value (field name or `[EnumValue]` override).

```csharp
public sealed class RequiredEnumJsonConverter<TRequiredEnum> : JsonConverter<TRequiredEnum>
    where TRequiredEnum : RequiredEnum<TRequiredEnum>, IScalarValue<TRequiredEnum, string>
// Reads: string → TryFromName/TryFromValue → TRequiredEnum
// Writes: TRequiredEnum → Value (string)
// Null tokens → null
```

You do not need to register this manually — the source generator adds it to each `RequiredEnum<T>` type.

## Concrete Value Objects (namespace: `Trellis.Primitives`)

All have `TryCreate` → `Result<T>` and `Create` → `T` (throws). All implement `IParsable<T>` and have `[JsonConverter]`.

| Type | Primitive | Validation | Extra Members |
|------|-----------|------------|---------------|
| `EmailAddress` | `string` | RFC 5322 regex, case-insensitive, trims | — |
| `PhoneNumber` | `string` | E.164 format (`^\+[1-9]\d{7,14}$`), normalizes | `GetCountryCode()` |
| `Url` | `string` | Valid absolute URI, HTTP/HTTPS only | `Scheme`, `Host`, `Port`, `Path`, `Query`, `IsSecure`, `ToUri()` |
| `Hostname` | `string` | RFC 1123 compliant, ≤255 chars | — |
| `IpAddress` | `string` | `System.Net.IPAddress.TryParse` (v4/v6) | `ToIPAddress()` |
| `Slug` | `string` | Lowercase alphanumeric + hyphens, no consecutive/leading/trailing | — |
| `CountryCode` | `string` | 2 letters, ISO 3166-1 alpha-2, uppercase | — |
| `CurrencyCode` | `string` | 3 letters, ISO 4217, uppercase | — |
| `LanguageCode` | `string` | 2 letters, ISO 639-1, lowercase | — |
| `Age` | `int` | 0–150 inclusive | — |
| `Percentage` | `decimal` | 0–100 inclusive | `Zero`, `Full`, `AsFraction()`, `Of(decimal)`, `FromFraction(decimal, fieldName?)`, `TryCreate(decimal?)` |
| `MonetaryAmount` | `decimal` | Non-negative, rounds to 2 dp | `Zero`, `Add`, `Subtract`, `Multiply(int)`, `Multiply(decimal)` |
| `Money` | multi-value | Amount ≥ 0, valid currency code | See below |

### MonetaryAmount (extends ScalarValueObject)

Scalar value object for single-currency systems where currency is a system-wide policy, not per-row data. Wraps a non-negative `decimal` rounded to 2 decimal places. JSON: plain number (e.g. `99.99`). EF Core: maps to 1 `decimal` column (via `ApplyTrellisConventions`).

```csharp
// Implements: ScalarValueObject<MonetaryAmount, decimal>, IScalarValue<MonetaryAmount, decimal>, IParsable<MonetaryAmount>

static Result<MonetaryAmount> TryCreate(decimal value)
static Result<MonetaryAmount> TryCreate(decimal? value)
static MonetaryAmount Create(decimal value)
static MonetaryAmount Zero { get; }

// Arithmetic (returns Result — handles overflow)
Result<MonetaryAmount> Add(MonetaryAmount other)
Result<MonetaryAmount> Subtract(MonetaryAmount other)
Result<MonetaryAmount> Multiply(int quantity)
Result<MonetaryAmount> Multiply(decimal multiplier)
```

### Money (extends ValueObject, NOT ScalarValueObject)

Structured value object with two semantic components: `Amount` (decimal) + `Currency` (CurrencyCode). JSON: `{"amount": 99.99, "currency": "USD"}`.

```csharp
decimal Amount { get; }
CurrencyCode Currency { get; }

static Result<Money> TryCreate(decimal amount, string currencyCode, string? fieldName = null)
static Money Create(decimal amount, string currencyCode)
static Result<Money> Zero(string currencyCode = "USD")

// Arithmetic (returns Result — enforces same currency)
Result<Money> Add(Money other)
Result<Money> Subtract(Money other)
Result<Money> Multiply(decimal multiplier)
Result<Money> Multiply(int quantity)
Result<Money> Divide(decimal divisor)
Result<Money> Divide(int divisor)
Result<Money[]> Allocate(params int[] ratios)

// Comparison
bool IsGreaterThan(Money other)
bool IsGreaterThanOrEqual(Money other)
bool IsLessThan(Money other)
bool IsLessThanOrEqual(Money other)
```

---
