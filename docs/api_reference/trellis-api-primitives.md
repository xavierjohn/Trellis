# Trellis API Primitives

**Packages:** `Trellis.Primitives` (the 13 concrete VOs: `EmailAddress`, `Money`, `Url`, `PhoneNumber`, `MonetaryAmount`, `Percentage`, etc.); `Trellis.Core` (since Phase 2: scalar/composite VO base classes — `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, `RequiredInt<TSelf>`, `RequiredDecimal<TSelf>`, `RequiredEnum<TSelf>` — plus the Trellis-namespaced validation attributes `StringLengthAttribute`, `RangeAttribute`, `EnumValueAttribute` and `StringExtensions`. The `Trellis.Core.Generator` source generator that emits `partial` bodies for these base classes is bundled inside `Trellis.Core.nupkg` under `analyzers/dotnet/cs/`). | **Namespaces:** `Trellis`, `Trellis.Primitives` | **Purpose:** strongly typed scalar and structured value objects, JSON converters, validation attributes, tracing helpers, and built-in concrete primitives.

## Types

### `IScalarValue<TSelf, TPrimitive>`

```csharp
public interface IScalarValue<TSelf, TPrimitive>
    where TSelf : IScalarValue<TSelf, TPrimitive>
    where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `TPrimitive` | Canonical primitive value exposed by scalar value objects. |

| Signature | Returns | Description |
| --- | --- | --- |
| `static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null)` | `Result<TSelf>` | Validates a primitive input. |
| `static abstract Result<TSelf> TryCreate(string? value, string? fieldName = null)` | `Result<TSelf>` | Validates a string input. |
| `static virtual TSelf Create(TPrimitive value)` | `TSelf` | Throws `InvalidOperationException` when validation fails. |

### `IFormattableScalarValue<TSelf, TPrimitive>`

```csharp
public interface IFormattableScalarValue<TSelf, TPrimitive> : IScalarValue<TSelf, TPrimitive>
    where TSelf : IFormattableScalarValue<TSelf, TPrimitive>
    where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `TPrimitive` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `static abstract Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` | `Result<TSelf>` | Culture-aware string parsing contract used by numeric and date primitives. |

### `ValueObject`

```csharp
public abstract class ValueObject : IComparable<ValueObject>, IComparable, IEquatable<ValueObject>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. Equality comes from `GetEqualityComponents()`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override bool Equals(object? obj)` | `bool` | Structural equality by type and equality components. |
| `public bool Equals(ValueObject? other)` | `bool` | Strongly typed structural equality. |
| `public override int GetHashCode()` | `int` | Cached structural hash code. |
| `public virtual int CompareTo(ValueObject? other)` | `int` | Ordered comparison by equality components. |
| `public static bool operator ==(ValueObject? a, ValueObject? b)` | `bool` | Equality operator. |
| `public static bool operator !=(ValueObject? a, ValueObject? b)` | `bool` | Inequality operator. |
| `public static bool operator <(ValueObject? left, ValueObject? right)` | `bool` | Less-than operator for same-type value objects. |
| `public static bool operator <=(ValueObject? left, ValueObject? right)` | `bool` | Less-than-or-equal operator for same-type value objects. |
| `public static bool operator >(ValueObject? left, ValueObject? right)` | `bool` | Greater-than operator for same-type value objects. |
| `public static bool operator >=(ValueObject? left, ValueObject? right)` | `bool` | Greater-than-or-equal operator for same-type value objects. |

### `ScalarValueObject<TSelf, T>`

```csharp
public abstract class ScalarValueObject<TSelf, T> : ValueObject, IConvertible, IFormattable
    where TSelf : ScalarValueObject<TSelf, T>, IScalarValue<TSelf, T>
    where T : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` | Wrapped primitive value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override string ToString()` | `string` | Default string representation of `Value`. |
| `public static implicit operator T(ScalarValueObject<TSelf, T> valueObject)` | `T` | Implicit unwrap to primitive. |
| `public static TSelf Create(T value)` | `TSelf` | Throwing scalar factory inherited by derived types. |
| `public TypeCode GetTypeCode()` | `TypeCode` | `IConvertible` support. |
| `public bool ToBoolean(IFormatProvider? provider)` | `bool` | Converts wrapped value. |
| `public byte ToByte(IFormatProvider? provider)` | `byte` | Converts wrapped value. |
| `public char ToChar(IFormatProvider? provider)` | `char` | Converts wrapped value. |
| `public DateTime ToDateTime(IFormatProvider? provider)` | `DateTime` | Converts wrapped value. |
| `public decimal ToDecimal(IFormatProvider? provider)` | `decimal` | Converts wrapped value. |
| `public double ToDouble(IFormatProvider? provider)` | `double` | Converts wrapped value. |
| `public short ToInt16(IFormatProvider? provider)` | `short` | Converts wrapped value. |
| `public int ToInt32(IFormatProvider? provider)` | `int` | Converts wrapped value. |
| `public long ToInt64(IFormatProvider? provider)` | `long` | Converts wrapped value. |
| `public sbyte ToSByte(IFormatProvider? provider)` | `sbyte` | Converts wrapped value. |
| `public float ToSingle(IFormatProvider? provider)` | `float` | Converts wrapped value. |
| `public string ToString(IFormatProvider? provider)` | `string` | Converts wrapped value to string. |
| `public string ToString(string? format, IFormatProvider? formatProvider)` | `string` | Formats underlying value. |
| `public object ToType(Type conversionType, IFormatProvider? provider)` | `object` | Converts wrapped value to target type. |
| `public ushort ToUInt16(IFormatProvider? provider)` | `ushort` | Converts wrapped value. |
| `public uint ToUInt32(IFormatProvider? provider)` | `uint` | Converts wrapped value. |
| `public ulong ToUInt64(IFormatProvider? provider)` | `ulong` | Converts wrapped value. |

### `RangeAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RangeAttribute : Attribute
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Constructor arguments are consumed by the source generator; no public properties are exposed. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public RangeAttribute(int minimum, int maximum)` | `RangeAttribute` | Range metadata for `RequiredInt<TSelf>` and whole-number `RequiredDecimal<TSelf>`. |
| `public RangeAttribute(long minimum, long maximum)` | `RangeAttribute` | Range metadata for `RequiredLong<TSelf>`. |
| `public RangeAttribute(double minimum, double maximum)` | `RangeAttribute` | Fractional range metadata for `RequiredDecimal<TSelf>`. |

### `StringLengthAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StringLengthAttribute : Attribute
```

| Name | Type | Description |
| --- | --- | --- |
| `MaximumLength` | `int` | Inclusive maximum length. |
| `MinimumLength` | `int` | Inclusive minimum length; defaults to `0`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public StringLengthAttribute(int maximumLength)` | `StringLengthAttribute` | Length metadata for `RequiredString<TSelf>`. |

### `EnumValueAttribute`

```csharp
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EnumValueAttribute : Attribute
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Canonical symbolic name for a `RequiredEnum<TSelf>` member. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public EnumValueAttribute(string value)` | `EnumValueAttribute` | Overrides the default field-name-based symbolic value. |

### `StringExtensions`

```csharp
public static class StringExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Static helper type. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static string NormalizeFieldName(this string? fieldName, string defaultName)` | `string` | Uses `fieldName` when present, otherwise camel-cases `defaultName`. |
| `public static T ParseScalarValue<T>(string? s) where T : class, IScalarValue<T, string>` | `T` | Throws `FormatException` based on `T.TryCreate`. |
| `public static bool TryParseScalarValue<T>([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out T result) where T : class, IScalarValue<T, string>` | `bool` | Safe parsing helper based on `T.TryCreate`. |
| `public static string ToCamelCase(this string? str)` | `string` | Lowercases the first character only. |

### `PrimitiveValueObjectTrace`

```csharp
public static class PrimitiveValueObjectTrace
```

| Name | Type | Description |
| --- | --- | --- |
| `ActivitySource` | `ActivitySource` | Shared OpenTelemetry source used by primitive creation/parsing paths. |

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
| `public static TracerProviderBuilder AddPrimitiveValueObjectInstrumentation(this TracerProviderBuilder builder)` | `TracerProviderBuilder` | Registers the Trellis primitive activity source with OpenTelemetry. |

### `ParsableJsonConverter<T>`

```csharp
public class ParsableJsonConverter<T> : JsonConverter<T> where T : IParsable<T>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Converter type; no public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `T?` | Accepts JSON `string`, `number`, `true`, `false`, and `null`; converts to string and calls `T.Parse(raw, default)`. `null` throws because Trellis scalar types are non-nullable. |
| `public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)` | `void` | Writes JSON numbers for numeric scalar types discovered via `ScalarValueObject<,>`; otherwise writes JSON strings. |

### `RequiredEnumJsonConverter<TRequiredEnum>`

```csharp
public sealed class RequiredEnumJsonConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TRequiredEnum> : JsonConverter<TRequiredEnum>
    where TRequiredEnum : RequiredEnum<TRequiredEnum>, IScalarValue<TRequiredEnum, string>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Converter type; no public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override TRequiredEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `TRequiredEnum?` | Accepts only JSON `string` and `null`; string values are resolved through `RequiredEnum<TRequiredEnum>.TryFromName(name)`. |
| `public override void Write(Utf8JsonWriter writer, TRequiredEnum value, JsonSerializerOptions options)` | `void` | Writes `value.Value` as a JSON string. |

### `RequiredString<TSelf>`

```csharp
public abstract class RequiredString<TSelf> : ScalarValueObject<TSelf, string>
    where TSelf : RequiredString<TSelf>, IScalarValue<TSelf, string>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Inherited scalar value. |
| `Length` | `int` | Convenience access to `Value.Length`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public bool StartsWith(string value)` | `bool` | Delegates to `string.StartsWith(string)`. |
| `public bool Contains(string value)` | `bool` | Delegates to `string.Contains(string)`. |
| `public bool EndsWith(string value)` | `bool` | Delegates to `string.EndsWith(string)`. |
| `public static TSelf Create(string value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredGuid<TSelf>`

```csharp
public abstract class RequiredGuid<TSelf> : ScalarValueObject<TSelf, Guid>
    where TSelf : RequiredGuid<TSelf>, IScalarValue<TSelf, Guid>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `Guid` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(Guid value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredInt<TSelf>`

```csharp
public abstract class RequiredInt<TSelf> : ScalarValueObject<TSelf, int>
    where TSelf : RequiredInt<TSelf>, IScalarValue<TSelf, int>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `int` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(int value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredDecimal<TSelf>`

```csharp
public abstract class RequiredDecimal<TSelf> : ScalarValueObject<TSelf, decimal>
    where TSelf : RequiredDecimal<TSelf>, IScalarValue<TSelf, decimal>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `decimal` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(decimal value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredLong<TSelf>`

```csharp
public abstract class RequiredLong<TSelf> : ScalarValueObject<TSelf, long>
    where TSelf : RequiredLong<TSelf>, IScalarValue<TSelf, long>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `long` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(long value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredBool<TSelf>`

```csharp
public abstract class RequiredBool<TSelf> : ScalarValueObject<TSelf, bool>
    where TSelf : RequiredBool<TSelf>, IScalarValue<TSelf, bool>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `bool` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(bool value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredDateTime<TSelf>`

```csharp
public abstract class RequiredDateTime<TSelf> : ScalarValueObject<TSelf, DateTime>
    where TSelf : RequiredDateTime<TSelf>, IScalarValue<TSelf, DateTime>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `DateTime` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override string ToString()` | `string` | Formats `Value` using invariant round-trip format `"O"`. |
| `public static TSelf Create(DateTime value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredEnum<TSelf>`

```csharp
public abstract class RequiredEnum<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TSelf>
    : IEquatable<RequiredEnum<TSelf>>
    where TSelf : RequiredEnum<TSelf>, IScalarValue<TSelf, string>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Canonical symbolic identity; defaults to the public static field name unless `[EnumValue]` overrides it. |
| `Ordinal` | `int` | Declaration-order metadata; not a wire/storage identity. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IReadOnlyCollection<TSelf> GetAll()` | `IReadOnlyCollection<TSelf>` | Returns all discovered public static readonly members. |
| `public static Result<TSelf> TryFromName(string? name, string? fieldName = null)` | `Result<TSelf>` | Case-insensitive symbolic lookup. |
| `public bool Is(params TSelf[] values)` | `bool` | True when this instance matches any provided member. |
| `public bool IsNot(params TSelf[] values)` | `bool` | Negation of `Is(params TSelf[])`. |
| `public override string ToString()` | `string` | Returns `Value`. |
| `public override int GetHashCode()` | `int` | Case-insensitive hash of `Value`. |
| `public override bool Equals(object? obj)` | `bool` | Case-insensitive symbolic equality. |
| `public bool Equals(RequiredEnum<TSelf>? other)` | `bool` | Case-insensitive symbolic equality. |
| `public static bool operator ==(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Equality operator. |
| `public static bool operator !=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Inequality operator. |

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
| `public static Age Create(int value)` | `Age` | Inherited throwing scalar factory. |

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
| `public static CountryCode Create(string value)` | `CountryCode` | Inherited throwing scalar factory. |

### `CurrencyCode`

```csharp
public class CurrencyCode : ScalarValueObject<CurrencyCode, string>, IScalarValue<CurrencyCode, string>, IParsable<CurrencyCode>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | ISO 4217 code, stored uppercase. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<CurrencyCode> TryCreate(string? value, string? fieldName = null)` | `Result<CurrencyCode>` | Requires exactly three letters. |
| `public static CurrencyCode Parse(string? s, IFormatProvider? provider)` | `CurrencyCode` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out CurrencyCode result)` | `bool` | Safe parse helper. |
| `public static CurrencyCode Create(string value)` | `CurrencyCode` | Inherited throwing scalar factory. |

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
| `public static EmailAddress Create(string value)` | `EmailAddress` | Inherited throwing scalar factory. |

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
| `public static Hostname Create(string value)` | `Hostname` | Inherited throwing scalar factory. |

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
| `public static IpAddress Create(string value)` | `IpAddress` | Inherited throwing scalar factory. |

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
| `public static LanguageCode Create(string value)` | `LanguageCode` | Inherited throwing scalar factory. |

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
| `public Result<MonetaryAmount> Add(MonetaryAmount other)` | `Result<MonetaryAmount>` | Adds two amounts. |
| `public Result<MonetaryAmount> Subtract(MonetaryAmount other)` | `Result<MonetaryAmount>` | Subtracts and fails if result would become invalid. |
| `public Result<MonetaryAmount> Multiply(int quantity)` | `Result<MonetaryAmount>` | Rejects negative quantity. |
| `public Result<MonetaryAmount> Multiply(decimal multiplier)` | `Result<MonetaryAmount>` | Rejects negative multiplier. |
| `public static MonetaryAmount Parse(string? s, IFormatProvider? provider)` | `MonetaryAmount` | Throws `FormatException` on failure. |
| `public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out MonetaryAmount result)` | `bool` | Safe parse helper. |
| `public static explicit operator MonetaryAmount(decimal value)` | `MonetaryAmount` | Explicit cast using `Create(decimal)`. |
| `public override string ToString()` | `string` | Invariant decimal string. |
| `public static Result<MonetaryAmount> Sum(IEnumerable<MonetaryAmount> values)` | `Result<MonetaryAmount>` | Returns `Zero` for empty collections. |
| `public static MonetaryAmount Create(decimal value)` | `MonetaryAmount` | Inherited throwing scalar factory. |

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
| `public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `T?` | Reads a JSON object, populates parameters by JSON property name (case-insensitive), invokes `TryCreate`, and throws `TrellisJsonValidationException` with the error display message on failure. Throws `TrellisJsonValidationException` for missing required properties. |
| `public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)` | `void` | Writes one JSON property per public instance property in declaration order, using the underlying primitive value for `IScalarValue<,>` properties. |

Apply via `[JsonConverter(typeof(CompositeValueObjectJsonConverter<MyVo>))]` on the value object type.
Reflection is performed once per generic instantiation and cached. **Not Native AOT compatible** — for AOT
scenarios, hand-write a `JsonConverter<T>`.

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
| `public static Result<Money> TryCreate(decimal amount, string currencyCode, string? fieldName = null)` | `Result<Money>` | Rejects negative amounts and invalid currency codes. |
| `public static Money Create(decimal amount, string currencyCode)` | `Money` | Throwing factory. |
| `public Result<Money> Add(Money other)` | `Result<Money>` | Requires matching currencies. |
| `public Result<Money> Subtract(Money other)` | `Result<Money>` | Requires matching currencies and non-negative result. |
| `public Result<Money> Multiply(decimal multiplier)` | `Result<Money>` | Rejects negative multiplier. |
| `public Result<Money> Multiply(int quantity)` | `Result<Money>` | Rejects negative quantity. |
| `public Result<Money> Divide(decimal divisor)` | `Result<Money>` | Divisor must be positive. |
| `public Result<Money> Divide(int divisor)` | `Result<Money>` | Divisor must be positive. |
| `public Result<Money[]> Allocate(params int[] ratios)` | `Result<Money[]>` | Ratio-based split with remainder distribution. |
| `public bool IsGreaterThan(Money other)` | `bool` | False when currencies differ. |
| `public bool IsGreaterThanOrEqual(Money other)` | `bool` | False when currencies differ. |
| `public bool IsLessThan(Money other)` | `bool` | False when currencies differ. |
| `public bool IsLessThanOrEqual(Money other)` | `bool` | False when currencies differ. |
| `public static Result<Money> Zero(string currencyCode = "USD")` | `Result<Money>` | Currency-aware zero instance. |
| `public override string ToString()` | `string` | Invariant amount plus currency code. |
| `public static Result<Money> Sum(IEnumerable<Money> values)` | `Result<Money>` | Fails for empty or mixed-currency collections. |

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
| `public static Percentage Create(decimal value)` | `Percentage` | Inherited throwing scalar factory. |

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
| `public string GetCountryCode()` | `string` | Extracts the E.164 country calling code. |
| `public static PhoneNumber Create(string value)` | `PhoneNumber` | Inherited throwing scalar factory. |

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
| `public static Slug Create(string value)` | `Slug` | Inherited throwing scalar factory. |

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
| `public static Url Create(string value)` | `Url` | Inherited throwing scalar factory. |

## Base class hierarchy

- Scalar contracts: `IScalarValue<TSelf, TPrimitive>` -> `IFormattableScalarValue<TSelf, TPrimitive>` for culture-aware numeric/date parsing.
- Scalar base class: `ValueObject` -> `ScalarValueObject<TSelf, T>`.
- Generated scalar bases:
  - `RequiredString<TSelf>` -> `ScalarValueObject<TSelf, string>` -> `ValueObject`
  - `RequiredGuid<TSelf>` -> `ScalarValueObject<TSelf, Guid>` -> `ValueObject`
  - `RequiredInt<TSelf>` -> `ScalarValueObject<TSelf, int>` -> `ValueObject`
  - `RequiredDecimal<TSelf>` -> `ScalarValueObject<TSelf, decimal>` -> `ValueObject`
  - `RequiredLong<TSelf>` -> `ScalarValueObject<TSelf, long>` -> `ValueObject`
  - `RequiredBool<TSelf>` -> `ScalarValueObject<TSelf, bool>` -> `ValueObject`
  - `RequiredDateTime<TSelf>` -> `ScalarValueObject<TSelf, DateTime>` -> `ValueObject`
- Symbolic base class:
  - `RequiredEnum<TSelf>` does **not** inherit `ScalarValueObject<TSelf, T>`; it implements symbolic identity directly through `Value`.
- Built-in scalars:
  - `Age`, `CountryCode`, `CurrencyCode`, `EmailAddress`, `Hostname`, `IpAddress`, `LanguageCode`, `MonetaryAmount`, `Percentage`, `PhoneNumber`, `Slug`, `Url` -> `ScalarValueObject<TSelf, T>` -> `ValueObject`
- Structured built-in:
  - `Money` -> `ValueObject`

## Source-generated members

The incremental generator in `Trellis.Primitives\generator\RequiredPartialClassGenerator.cs` augments partial classes that inherit a required base type.

### `RequiredString<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(string? value, string? fieldName = null)
public static TSelf Create(string? value, string? fieldName = null)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(string value)
static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
```

- Built-in validation: null/empty/whitespace rejection, trimming, optional `[StringLength]` checks.

### `RequiredGuid<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static TSelf NewUniqueV4()
public static TSelf NewUniqueV7()
public static Result<TSelf> TryCreate(Guid value, string? fieldName = null)
public static Result<TSelf> TryCreate(Guid? requiredGuidOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static new TSelf Create(Guid value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(Guid value)
static partial void ValidateAdditional(Guid value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `Guid.Empty` rejection.

### `RequiredInt<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(int value, string? fieldName = null)
public static Result<TSelf> TryCreate(int? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(int value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(int value)
static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `null` rejection for nullable inputs, optional `[Range(int, int)]`.

### `RequiredDecimal<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(decimal value, string? fieldName = null)
public static Result<TSelf> TryCreate(decimal? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(decimal value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(decimal value)
static partial void ValidateAdditional(decimal value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `null` rejection for nullable inputs, optional `[Range(int, int)]` or `[Range(double, double)]`.

### `RequiredLong<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(long value, string? fieldName = null)
public static Result<TSelf> TryCreate(long? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(long value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(long value)
static partial void ValidateAdditional(long value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `null` rejection for nullable inputs, optional `[Range(long, long)]`.

### `RequiredBool<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(bool value, string? fieldName = null)
public static Result<TSelf> TryCreate(bool? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static new TSelf Create(bool value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(bool value)
static partial void ValidateAdditional(bool value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `null` rejection for nullable inputs; `false` is valid.

### `RequiredDateTime<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(DateTime value, string? fieldName = null)
public static Result<TSelf> TryCreate(DateTime? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(DateTime value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(DateTime value)
static partial void ValidateAdditional(DateTime value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `DateTime.MinValue` rejection.

### `RequiredEnum<TSelf>`

```csharp
[JsonConverter(typeof(RequiredEnumJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(string value)
public static Result<TSelf> TryCreate(string? value, string? fieldName = null)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static TSelf Create(string value)
```

- Generated `TryCreate` delegates only to `TryFromName`.
- The enum JSON converter also uses only `TryFromName`; there is no `TryFromValue` path.

## Built-in primitives table

| Type | Namespace | Category | Underlying/wire shape | Notes |
| --- | --- | --- | --- | --- |
| `Age` | `Trellis.Primitives` | Scalar | JSON number or numeric string input; JSON number output | `int`, range `0..150`. |
| `CountryCode` | `Trellis.Primitives` | Scalar | JSON string | Uppercase ISO 3166-1 alpha-2. |
| `CurrencyCode` | `Trellis.Primitives` | Scalar | JSON string | Uppercase ISO 4217. |
| `EmailAddress` | `Trellis.Primitives` | Scalar | JSON string | Trimmed validated email. |
| `Hostname` | `Trellis.Primitives` | Scalar | JSON string | RFC 1123 hostname. |
| `IpAddress` | `Trellis.Primitives` | Scalar | JSON string | IPv4 or IPv6 text. |
| `LanguageCode` | `Trellis.Primitives` | Scalar | JSON string | Lowercase ISO 639-1 alpha-2. |
| `MonetaryAmount` | `Trellis.Primitives` | Scalar | JSON number or numeric string input; JSON number output | Non-negative single-currency amount with 2-decimal rounding. |
| `Money` | `Trellis.Primitives` | Structured | JSON object `{ "amount": number, "currency": string }` | Multi-currency value object; not scalar. |
| `Percentage` | `Trellis.Primitives` | Scalar | JSON number or numeric string input; JSON number output | `decimal` in `0..100`; `ToString()` adds `%`. |
| `PhoneNumber` | `Trellis.Primitives` | Scalar | JSON string | Normalized E.164 string. |
| `Slug` | `Trellis.Primitives` | Scalar | JSON string | Lowercase letters, digits, single hyphens. |
| `Url` | `Trellis.Primitives` | Scalar | JSON string | Absolute HTTP/HTTPS URI. |

## Code examples

```csharp
using System.Globalization;
using Trellis;
using Trellis.Primitives;

namespace Demo;

[StringLength(50)]
public partial class CustomerName : RequiredString<CustomerName> { }

public partial class OrderId : RequiredGuid<OrderId> { }

[Range(1, 999)]
public partial class LineCount : RequiredInt<LineCount> { }

public partial class SubmittedAt : RequiredDateTime<SubmittedAt> { }

public partial class OrderState : RequiredEnum<OrderState>
{
    public static readonly OrderState Draft = new();

    [EnumValue("submitted")]
    public static readonly OrderState Submitted = new();
}

public static class Example
{
    public static void Run()
    {
        var orderId = OrderId.NewUniqueV7();
        var name = CustomerName.Create("Ada");
        var lines = LineCount.TryCreate("42", CultureInfo.InvariantCulture).Value;
        var submittedAt = SubmittedAt.Parse("2026-01-15T12:00:00Z", CultureInfo.InvariantCulture);
        var state = OrderState.Create("submitted");

        var percentage = Percentage.FromFraction(0.15m).Value;
        var amount = MonetaryAmount.Create(12.34m);
        var taxAmount = percentage.Of(amount);

        var total = Money.Create(12.34m, "USD");
        var shipping = Money.Create(2.00m, "USD");
        var grandTotal = total.Add(shipping).Value;

        _ = (orderId, name, lines, submittedAt, state, taxAmount, grandTotal);
    }
}
```

## Cross-references

- [trellis-api-core.md](trellis-api-core.md)
- [trellis-api-core.md](trellis-api-core.md)
- [trellis-api-efcore.md](trellis-api-efcore.md)
