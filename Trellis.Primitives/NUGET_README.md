# Primitive Value Objects

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Primitives.svg)](https://www.nuget.org/packages/Trellis.Primitives)

Infrastructure and ready-to-use implementations for primitive value objects with source code generation, eliminating boilerplate code and primitive obsession in domain-driven design applications.

## Installation

Install both packages via NuGet:

```bash
dotnet add package Trellis.Primitives
dotnet add package Trellis.Primitives.Generator
```

**Important:** Both packages are required:
- `Trellis.Primitives` — Base classes, built-in scalar value objects, and the structured `Money` value object
- `Trellis.Primitives.Generator` — Source generator for `Required*` derivatives

## Quick Start

### RequiredString

Create strongly-typed string value objects using source code generation:

```csharp
public partial class TrackingId : RequiredString<TrackingId>
{
}

// The source generator automatically creates:
// - TryCreate(string?, string? fieldName = null) -> Result<TrackingId>
// - Parse / TryParse (IParsable<T>)
// - explicit operator TrackingId(string)

var result = TrackingId.TryCreate("TRK-12345");
if (result.IsSuccess)
    Console.WriteLine(result.Value); // TRK-12345

// With custom field name for validation errors
var result2 = TrackingId.TryCreate(input, "shipment.trackingId");
```

Optional: enforce length constraints with `[StringLength]`:

```csharp
[StringLength(50)]
public partial class FirstName : RequiredString<FirstName> { }

[StringLength(500, MinimumLength = 10)]
public partial class Description : RequiredString<Description> { }
```

### RequiredGuid

Use `NewUniqueV7()` for time-ordered, sortable identifiers — GUID V7 provides the same benefits as ULIDs (sequential, timestamp-embedded) with the standard `System.Guid` type.

```csharp
public partial class EmployeeId : RequiredGuid<EmployeeId>
{
}

var employeeId = EmployeeId.NewUniqueV7(); // Time-ordered, sortable
var result = EmployeeId.TryCreate(guid);
var result2 = EmployeeId.TryCreate("550e8400-e29b-41d4-a716-446655440000");
```

### RequiredInt and RequiredDecimal

```csharp
public partial class Quantity : RequiredInt<Quantity> { }
public partial class Price : RequiredDecimal<Price> { }

var qty = Quantity.TryCreate(10);
var price = Price.TryCreate(99.99m);
```

## Ready-to-Use Value Objects

| Value Object | Purpose | Validation | Example |
|-------------|----------|------------|---------|
| **EmailAddress** | Email validation | RFC 5322 compliant | `user@example.com` |
| **Url** | Web URLs | Absolute HTTP/HTTPS | `https://example.com` |
| **PhoneNumber** | Phone numbers | E.164 format | `+14155551234` |
| **Percentage** | Percentage values | 0–100 range | `15.5` or `15.5%` |
| **CurrencyCode** | Currency codes | ISO 4217 3-letter | `USD`, `EUR` |
| **IpAddress** | IP addresses | IPv4 and IPv6 | `192.168.1.1` |
| **Hostname** | Hostnames | RFC 1123 | `example.com` |
| **Slug** | URL slugs | Lowercase, digits, hyphens | `my-blog-post` |
| **CountryCode** | Country codes | ISO 3166-1 alpha-2 | `US`, `GB` |
| **LanguageCode** | Language codes | ISO 639-1 alpha-2 | `en`, `es` |
| **Age** | Age values | 0–150 range | `42` |
| **Money** | Monetary amounts | Non-negative + ISO 4217 currency | `99.99 USD` |

```csharp
var email = EmailAddress.TryCreate("user@example.com");
var url = Url.TryCreate("https://example.com/path");
var phone = PhoneNumber.TryCreate("+14155551234");
var pct = Percentage.TryCreate(15.5m);
var money = Money.TryCreate(99.99m, "USD");
```

All built-ins in the table except `Money` follow the scalar `IScalarValue<TSelf, TPrimitive>` pattern. `Money` is intentionally structured: its identity is `Amount` + `Currency`, and it keeps object-shaped JSON and EF persistence.

## RequiredEnum

Type-safe enumerations that replace C# enums with full-featured classes:

```csharp
public partial class OrderState : RequiredEnum<OrderState>
{
    public static readonly OrderState Draft = new();
    public static readonly OrderState Confirmed = new();
    public static readonly OrderState Shipped = new();
}

var result = OrderState.TryCreate("Confirmed");
// result.Value == OrderState.Confirmed

Console.WriteLine(OrderState.Confirmed.Value);   // "Confirmed"

// Ordinal exists as secondary declaration-order metadata, not as the main public contract
```

## ASP.NET Core Integration

Value objects implementing `IScalarValue` work seamlessly with ASP.NET Core:

```csharp
// 1. Register in Program.cs
builder.Services
    .AddControllers()
    .AddScalarValueValidation();

// 2. Use in DTOs
public record CreateUserDto
{
    public FirstName FirstName { get; init; } = null!;
    public EmailAddress Email { get; init; } = null!;
    public Maybe<Url> Website { get; init; } // Optional — null → Maybe.None
}

// 3. Controllers get automatic validation
[HttpPost]
public IActionResult Create(CreateUserDto dto)
{
    // All value objects validated on deserialization!
    return Ok(dto);
}
```

See [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) for full ASP.NET Core integration.

`Money` is not part of that scalar model-binding pipeline; it uses its own JSON converter because it is a structured value object.

## Generated Code Features

- `TryCreate` methods returning `Result<T>` with optional `fieldName` parameter
- `IParsable<T>` implementation (`Parse`/`TryParse`)
- Explicit cast operators
- Property name inference for error messages (class name → camelCase)
- JSON serialization via `ParsableJsonConverter<T>`
- OpenTelemetry activity tracing support, typically a better day-to-day diagnostic signal than full ROP tracing because it emits spans at value creation and validation boundaries

## Best Practices

1. **Use partial classes** — Required for source code generation
2. **Leverage generated methods** — Use `TryCreate` for safe parsing
3. **Use the fieldName parameter** — Better validation error messages in APIs
4. **Prefer specific types over primitives** — `EmployeeId` is more expressive than `Guid`
5. **Use meaningful names** — Class name becomes part of error messages

## Related Packages

- [Trellis.Primitives.Generator](https://www.nuget.org/packages/Trellis.Primitives.Generator) — Source generator (required companion)
- [Trellis.Results](https://www.nuget.org/packages/Trellis.Results) — Core `Result<T>` type
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — Entity and aggregate patterns
- [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) — ASP.NET Core integration

## License

MIT — see [LICENSE](https://github.com/xavierjohn/Trellis/blob/main/LICENSE) for details.
