# Primitive Value Objects

Trellis.Primitives provides base classes for creating strongly-typed value objects and a set of ready-to-use concrete types. Use these to prevent primitive obsession — every domain concept gets its own type with built-in validation, JSON serialization, EF Core support, and OpenTelemetry tracing.

**Namespaces:**

- `Trellis` — base classes (`RequiredString<T>`, `RequiredGuid<T>`, etc.)
- `Trellis.Primitives` — concrete value objects (`EmailAddress`, `Money`, etc.)

## Base Classes

All base classes use the curiously recurring template pattern (`T : Base<T>`) so the source generator can produce factory methods, serialization, and model binding for your derived type.

| Base Class | Primitive Type | Use For | Attributes |
|-----------|---------------|---------|------------|
| `RequiredString<T>` | `string` | Names, descriptions, codes | `[StringLength(max)]`, `[StringLength(max, MinimumLength = min)]` |
| `RequiredGuid<T>` | `Guid` | Entity IDs | — |
| `RequiredInt<T>` | `int` | Quantities, counts | `[Range(min, max)]` |
| `RequiredDecimal<T>` | `decimal` | Prices, amounts | `[Range(min, max)]` |
| `RequiredLong<T>` | `long` | Large numbers | `[Range(min, max)]` |
| `RequiredDateTime<T>` | `DateTime` | Dates, timestamps | — |
| `RequiredBool<T>` | `bool` | Flags, toggles | — |
| `RequiredEnum<T>` | `string` (symbolic) | Status, category | `[EnumValue("wire-name")]` |

## Creating Custom Value Objects

Declare your type as `partial` so the source generator can wire up `TryCreate`, JSON converters, model binding, and EF Core value conversion automatically.

### Minimal — just declare partial

```csharp
public partial class TodoId : RequiredGuid<TodoId> { }
```

### With length constraint

```csharp
[Trellis.StringLength(200)]
public partial class Title : RequiredString<Title> { }
```

### With range constraint

```csharp
[Trellis.Range(1, 1000)]
public partial class Quantity : RequiredInt<Quantity> { }
```

### With custom validation

```csharp
[Trellis.StringLength(50)]
public partial class Tag : RequiredString<Tag>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
    {
        if (!Regex.IsMatch(value, @"^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            errorMessage = "Tag must contain only lowercase letters, numbers, and hyphens.";
    }
}
```

### Smart enum

```csharp
public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new();
    public static readonly OrderStatus Pending = new();
    public static readonly OrderStatus Confirmed = new();
}
```

> For a deep dive into `RequiredEnum`, see the [RequiredEnum](required-enum.md) article.

## Factory Methods

Every value object provides two factory methods:

- `TryCreate(...)` → `Result<T>` — for expected failures (user input, external data)
- `Create(...)` → `T` (throws) — for known-valid values (tests, seed data)

```csharp
var result = Title.TryCreate(userInput);     // Result<Title>
var title = Title.Create("Known valid");      // Title (throws if invalid)
var id = TodoId.NewUniqueV7();                // Generate new GUID v7 (time-ordered)
```

Use `TryCreate` in your domain logic and API handlers to stay on the railway:

```csharp
return Title.TryCreate(request.Title)
    .Combine(TodoId.TryCreate(request.Id))
    .Bind((title, id) => Todo.TryCreate(id, title));
```

## RequiredString Methods

`RequiredString<T>` exposes common string methods directly on the value object so you can write natural code without reaching into `.Value`:

```csharp
// Available on all RequiredString<T> types
name.StartsWith("Al")    // delegates to Value.StartsWith
name.Contains("lic")     // delegates to Value.Contains
name.EndsWith("ce")      // delegates to Value.EndsWith
name.Length               // delegates to Value.Length
```

These enable natural EF Core queries without `.Value`:

```csharp
context.Customers.Where(c => c.Name.StartsWith("Al"))  // → LIKE 'Al%'
context.Customers.Where(c => c.Name.Length > 3)         // → LEN(Name) > 3
```

## Built-in Concrete Types

These types live in the `Trellis.Primitives` namespace and are ready to use out of the box.

| Type | Primitive | Validation |
|------|----------|------------|
| `EmailAddress` | `string` | RFC 5322 regex |
| `PhoneNumber` | `string` | E.164 format |
| `Url` | `string` | Valid URI; properties: `Scheme`, `Host`, `Port`, `Path` |
| `Hostname` | `string` | Valid hostname |
| `IpAddress` | `string` | IPv4/IPv6; `ToIPAddress()` |
| `Slug` | `string` | URL-safe slug |
| `CountryCode` | `string` | ISO 3166-1 alpha-2 |
| `CurrencyCode` | `string` | ISO 4217 |
| `LanguageCode` | `string` | ISO 639-1 |
| `Age` | `int` | 0–199 |
| `Percentage` | `decimal` | 0–100; `FromFraction()`, `AsFraction()`, `Of()` |
| `MonetaryAmount` | `decimal` | Non-negative, 2 dp; `Add`, `Subtract`, `Multiply` — single-currency alternative to `Money` |
| `Money` | composite | Amount + CurrencyCode; arithmetic: `Add`, `Subtract`, `Multiply`, `Divide`, `Allocate` |

### Usage examples

```csharp
var email = EmailAddress.TryCreate("alice@example.com");   // Result<EmailAddress>
var phone = PhoneNumber.TryCreate("+1234567890");           // Result<PhoneNumber>
var url   = Url.Create("https://example.com/path");         // Url (throws if invalid)

Console.WriteLine(url.Host);    // "example.com"
Console.WriteLine(url.Scheme);  // "https"
```

```csharp
var pct = Percentage.Create(25m);
Console.WriteLine(pct.AsFraction());    // 0.25
Console.WriteLine(pct.Of(200m));        // 50

var half = Percentage.FromFraction(0.5m);
Console.WriteLine(half.Value);          // 50
```

```csharp
// MonetaryAmount — single-currency systems (1 column in EF Core)
var amount = MonetaryAmount.Create(49.95m);
var total  = amount.Add(MonetaryAmount.Create(10.00m));  // Result<MonetaryAmount>
```

```csharp
// Money — multi-currency systems (2 columns in EF Core)
var price = Money.Create(100.00m, CurrencyCode.Create("USD"));
var tax   = price.Multiply(0.08m);
var total = price.Add(tax);

// Split a bill evenly (handles rounding)
var shares = total.Allocate(3);
```

## Culture-Aware String Parsing

Numeric and date value objects (`Age`, `MonetaryAmount`, `Percentage`, `RequiredInt<T>`, `RequiredDecimal<T>`, `RequiredLong<T>`, `RequiredDateTime<T>`) implement `IFormattableScalarValue<TSelf, TPrimitive>`, which adds `TryCreate(string?, IFormatProvider?, string?)` for culture-sensitive parsing. The standard `TryCreate(string?)` always uses `InvariantCulture` — safe for APIs. Use the `IFormatProvider` overload when importing CSV data or handling user input with a known locale. String-based types (`EmailAddress`, `Slug`, etc.) are not affected by culture and do not implement this interface.

## EF Core LINQ Support

Trellis value objects work seamlessly in EF Core LINQ queries. In most cases you do **not** need `.Value`:

| Scenario | Example | `.Value` needed? |
|----------|---------|:---:|
| Equality (VO vs primitive) | `c.Name == "Alice"` | No |
| Equality (VO vs VO) | `c.Name == someName` | No |
| Comparison (VO vs primitive) | `todo.DueDate < cutoff` | No |
| Comparison (VO vs VO) | `todo.DueDate < someDueDate` | No |
| StartsWith | `c.Name.StartsWith("Al")` | No |
| Contains | `c.Name.Contains("lic")` | No |
| EndsWith | `c.Name.EndsWith("ce")` | No |
| Length | `c.Name.Length > 3` | No |
| OrderBy | `OrderBy(c => c.Name)` | No |
| Select to primitive | `Select(c => c.Name.Value)` | **Yes** |

> **Note:** `StartsWith`, `Contains`, `EndsWith`, and `Length` require `AddTrellisInterceptors()` in your `DbContext` configuration to translate correctly through EF Core.

```csharp
// In your DbContext configuration
protected override void OnConfiguring(DbContextOptionsBuilder options)
{
    options.AddTrellisInterceptors();
}
```

## When to Use Built-in vs Custom

- **Use built-in types** (`EmailAddress`, `Money`, `Percentage`, etc.) when the validation matches your domain needs exactly.
- **Create custom types** when you need different validation rules, length constraints, or domain-specific logic.
- **No collision risk** — if you create a custom type with the same name (e.g., your own `EmailAddress`), simply don't import `Trellis.Primitives` in that file.

```csharp
// Your own EmailAddress with stricter validation
[Trellis.StringLength(100)]
public partial class EmailAddress : RequiredString<EmailAddress>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
    {
        if (!value.EndsWith("@mycompany.com"))
            errorMessage = "Only company email addresses are allowed.";
    }
}
```
