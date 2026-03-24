# Trellis.Primitives — Primitive Value Objects

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Primitives.svg)](https://www.nuget.org/packages/Trellis.Primitives)

This library provides infrastructure and ready-to-use implementations for primitive value objects with source code generation, eliminating boilerplate code and primitive obsession in domain-driven design applications.

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [RequiredString](#requiredstring)
- [RequiredGuid](#requiredguid)
- [RequiredInt and RequiredDecimal](#requiredint-and-requireddecimal)
- [RequiredEnum](#requiredenum)
- [EmailAddress](#emailaddress)
- [Additional Value Objects](#additional-value-objects)
- [Money](#money)
- [ASP.NET Core Integration](#aspnet-core-integration)
- [Core Concepts](#core-concepts)
- [Best Practices](#best-practices)
- [Related Packages](#related-packages)
- [License](#license)

## Installation

Install both packages via NuGet:

```bash
dotnet add package Trellis.Primitives
dotnet add package Trellis.Primitives.Generator
```

**Important:** Both packages are required:
- `Trellis.Primitives` - Provides base classes (`RequiredString`, `RequiredGuid`, `RequiredInt`, `RequiredDecimal`, `RequiredEnum`), built-in scalar value objects, and the structured `Money` value object
- `Trellis.Primitives.Generator` - Source generator that creates implementations for `Required*` base class derivatives

## Quick Start

### RequiredString

Create strongly-typed string value objects using source code generation:

```csharp
public partial class TrackingId : RequiredString<TrackingId>
{
}

// The source generator automatically creates:
// - IScalarValue<TrackingId, string> interface implementation
// - TryCreate(string) -> Result<TrackingId> (required by IScalarValue)
// - TryCreate(string?, string? fieldName = null) -> Result<TrackingId>
// - Parse(string, IFormatProvider?) -> TrackingId
// - TryParse(string?, IFormatProvider?, out TrackingId) -> bool
// - explicit operator TrackingId(string)

var result = TrackingId.TryCreate("TRK-12345");
if (result.IsSuccess)
{
    var trackingId = result.Value;
    Console.WriteLine(trackingId); // Outputs: TRK-12345
}

// With custom field name for validation errors
var result2 = TrackingId.TryCreate(input, "shipment.trackingId");
// Error field will be "shipment.trackingId" instead of default "trackingId"

// Supports IParsable<T>
var parsed = TrackingId.Parse("TRK-12345", null);

// Explicit cast operator (throws on failure)
var trackingId = (TrackingId)"TRK-12345";
```

Optional: Add `[StringLength]` for length constraints:

```csharp
[StringLength(50)]
public partial class FirstName : RequiredString<FirstName> { }

[StringLength(500, MinimumLength = 10)]
public partial class Description : RequiredString<Description> { }
```

Optional: Add `ValidateAdditional` for custom validation (regex, format checks):

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

The hook is completely optional — if not implemented, the compiler removes the call (zero overhead). Available on `RequiredString`, `RequiredInt`, and `RequiredDecimal`.

### RequiredGuid

Create strongly-typed GUID value objects. Use `NewUniqueV7()` for time-ordered, sortable identifiers — GUID V7 provides the same benefits as ULIDs (sequential, timestamp-embedded) while using the standard `System.Guid` type.

```csharp
public partial class EmployeeId : RequiredGuid<EmployeeId>
{
}

// The source generator automatically creates:
// - IScalarValue<EmployeeId, Guid> interface implementation
// - NewUniqueV4() -> EmployeeId
// - NewUniqueV7() -> EmployeeId (time-ordered)
// - TryCreate(Guid) -> Result<EmployeeId> (required by IScalarValue)
// - TryCreate(Guid?, string? fieldName = null) -> Result<EmployeeId>
// - TryCreate(string?, string? fieldName = null) -> Result<EmployeeId>
// - Parse(string, IFormatProvider?) -> EmployeeId
// - TryParse(string?, IFormatProvider?, out EmployeeId) -> bool
// - explicit operator EmployeeId(Guid)

var employeeId = EmployeeId.NewUniqueV7(); // Create new time-ordered GUID
var result = EmployeeId.TryCreate(guid);
var result2 = EmployeeId.TryCreate("550e8400-e29b-41d4-a716-446655440000");

// With custom field name for validation errors
var result3 = EmployeeId.TryCreate(input, "employee.id");

// Supports IParsable<T>
var parsed = EmployeeId.Parse("550e8400-e29b-41d4-a716-446655440000", null);

// Explicit cast operator (throws on failure)
var employeeId = (EmployeeId)Guid.NewGuid();
```

### RequiredInt and RequiredDecimal

Create strongly-typed numeric value objects:

```csharp
public partial class Quantity : RequiredInt<Quantity> { }
public partial class Price : RequiredDecimal<Price> { }

// Same features as RequiredGuid/RequiredString:
var qty = Quantity.TryCreate(10);
var price = Price.TryCreate(99.99m);

// Validates non-zero values
var invalid = Quantity.TryCreate(0);
// Returns: Error.Validation("Quantity cannot be empty.", "quantity")
```

Use `[Range]` to constrain values (like `[StringLength]` for strings):

```csharp
[Range(1, 999)]
public partial class LineItemQuantity : RequiredInt<LineItemQuantity> { }

[Range(0, 100)]  // allows zero
public partial class StockQuantity : RequiredInt<StockQuantity> { }

var qty = LineItemQuantity.TryCreate(1000);
// Returns: Error.Validation("Line item quantity must be at most 999.", "lineItemQuantity")
```

### RequiredEnum

Create type-safe enumeration value objects that replace C# enums with full-featured classes:

```csharp
public partial class OrderState : RequiredEnum<OrderState>
{
    public static readonly OrderState Draft = new();
    public static readonly OrderState Confirmed = new();
    public static readonly OrderState Shipped = new();
    public static readonly OrderState Delivered = new();
    public static readonly OrderState Cancelled = new();
}

// Members are discovered via reflection; Value is the semantic contract
Console.WriteLine(OrderState.Draft.Value);   // "Draft"

// Create from string
var result = OrderState.TryCreate("Confirmed");
// result.Value == OrderState.Confirmed

// Helper methods
OrderState.Confirmed.Is(OrderState.Confirmed);    // true
OrderState.Confirmed.IsNot(OrderState.Cancelled);  // true

// Invalid values are impossible
var invalid = OrderState.TryCreate("Unknown");
// Returns: Error.Validation("Invalid OrderState value: Unknown", "orderState")

// Ordinal exists as declaration-order metadata, not as the primary public identity
```

### EmailAddress

Pre-built email validation value object with RFC 5322 compliant validation:

```csharp
var result = EmailAddress.TryCreate("user@example.com");
if (result.IsSuccess)
{
    var email = result.Value;
    Console.WriteLine(email); // Outputs: user@example.com
}

// With custom field name for validation errors
var result2 = EmailAddress.TryCreate(input, "contact.email");

// Validation errors
var invalid = EmailAddress.TryCreate("not-an-email");
// Returns: Error.Validation("Email address is not valid.", "email")

// Supports IParsable<T>
var parsed = EmailAddress.Parse("user@example.com", null);

if (EmailAddress.TryParse("user@example.com", null, out var email))
{
    Console.WriteLine($"Valid: {email.Value}");
}
```

### Additional Value Objects

#### Url
```csharp
var result = Url.TryCreate("https://example.com/path");
// Access URL components
if (result.IsSuccess)
{
    var url = result.Value;
    Console.WriteLine(url.Scheme);  // "https"
    Console.WriteLine(url.Host);    // "example.com"
    Console.WriteLine(url.Path);    // "/path"
    Console.WriteLine(url.IsSecure); // true
}

// Invalid URLs
var invalid = Url.TryCreate("not-a-url");
// Returns: Error.Validation("URL must be a valid absolute HTTP or HTTPS URL.", "url")
```

#### PhoneNumber
```csharp
var result = PhoneNumber.TryCreate("+14155551234");
if (result.IsSuccess)
{
    var phone = result.Value;
    Console.WriteLine(phone.GetCountryCode()); // "1"
}

// Normalizes input (removes spaces, dashes, parentheses)
var normalized = PhoneNumber.TryCreate("+1 (415) 555-1234");
// Stores as: "+14155551234"
```

#### Percentage
```csharp
var discount = Percentage.TryCreate(15.5m);
var fromFraction = Percentage.FromFraction(0.155m); // Also 15.5%

if (discount.IsSuccess)
{
    var pct = discount.Value;
    Console.WriteLine(pct.AsFraction());      // 0.155
    Console.WriteLine(pct.Of(100m));          // 15.5
    Console.WriteLine(pct.ToString());        // "15.5%"
}

// Parse with % suffix
var parsed = Percentage.Parse("20%", null); // Valid
```

#### CurrencyCode
```csharp
var result = CurrencyCode.TryCreate("USD");
// Stores as uppercase: "USD"

var invalid = CurrencyCode.TryCreate("US"); 
// Error: Must be 3-letter code
```

#### CountryCode and LanguageCode
```csharp
var country = CountryCode.TryCreate("US");  // Uppercase
var language = LanguageCode.TryCreate("en"); // Lowercase

// ISO standard codes only
var invalid = CountryCode.TryCreate("USA"); // Error: Must be 2 letters
```

#### IpAddress
```csharp
var ipv4 = IpAddress.TryCreate("192.168.1.1");
var ipv6 = IpAddress.TryCreate("::1");

if (ipv4.IsSuccess)
{
    var ip = ipv4.Value.ToIPAddress(); // Access System.Net.IPAddress
}
```

#### Hostname and Slug
```csharp
var hostname = Hostname.TryCreate("example.com");
// RFC 1123 validation

var slug = Slug.TryCreate("my-blog-post");
// Lowercase letters, digits, and hyphens only
// No leading/trailing hyphens, no consecutive hyphens

var invalid = Slug.TryCreate("My Blog Post!"); // Error
```

#### Age
```csharp
var age = Age.TryCreate(42);
// Range: 0-150

var tooOld = Age.TryCreate(200);
// Error: "Age is unrealistically high."
```

#### Money

`Money` is a structured value object, not a scalar wrapper. Its identity is the pair of `Amount` and `Currency`, so its JSON and EF representations stay object-shaped rather than flowing through the `IScalarValue<TSelf, TPrimitive>` pipeline.

Monetary amounts with currency code and type-safe arithmetic:

```csharp
var price = Money.TryCreate(99.99m, "USD");
if (price.IsSuccess)
{
    Console.WriteLine(price.Value.Amount);    // 99.99
    Console.WriteLine(price.Value.Currency);  // USD
}

// Arithmetic operations enforce currency matching
var total = price.Value.Add(Money.Create(10.00m, "USD"));
// Returns: Result<Money> with 109.99 USD

// Mixing currencies returns an error
var invalid = price.Value.Add(Money.Create(10.00m, "EUR"));
// Returns: Error — currency mismatch

// Negative amounts are rejected
var negative = Money.TryCreate(-5.00m, "USD");
// Returns: Error.Validation("Amount cannot be negative.", "amount")
```

### ASP.NET Core Integration

Value objects implementing `IScalarValue` work seamlessly with ASP.NET Core for automatic validation:

```csharp
// 1. Register in Program.cs
builder.Services
    .AddControllers()
    .AddScalarValueValidation(); // Enable automatic scalar value validation!

// 2. Define your value objects (source generator adds IScalarValue automatically)
public partial class FirstName : RequiredString<FirstName> { }
public partial class CustomerId : RequiredGuid<CustomerId> { }

// 3. Use in DTOs with both custom and built-in value objects
public record CreateUserDto
{
    public FirstName FirstName { get; init; } = null!;
    public EmailAddress Email { get; init; } = null!;
    public PhoneNumber Phone { get; init; } = null!;
    public Age Age { get; init; } = null!;
    public CountryCode Country { get; init; } = null!;
    public Maybe<Url> Website { get; init; } // Optional — null in JSON → Maybe.None
}

// 4. Controllers get automatic validation - no manual Result.Combine needed!
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public IActionResult Create(CreateUserDto dto)
    {
        // If we reach here, dto is FULLY validated!
        // All 6 value objects were automatically validated:
        // - FirstName: non-empty string
        // - Email: RFC 5322 format
        // - Phone: E.164 format
        // - Age: 0-150 range
        // - Country: ISO 3166-1 alpha-2 code
        // - Website: valid HTTP/HTTPS URL (optional, Maybe<Url>)
        
        var user = new User(dto.FirstName, dto.Email, dto.Phone, dto.Age, dto.Country);
        return Ok(user);
    }

    [HttpGet("{id}")]
    public IActionResult Get(CustomerId id) // Route parameter validated automatically!
    {
        var user = _repository.GetById(id);
        return Ok(user);
    }
}

// Invalid requests automatically return 400 Bad Request with validation errors
```

**Benefits:**
- No manual `Result.Combine()` calls in controllers
- Works with route parameters, query strings, form data, and JSON bodies
- Validation errors automatically flow into `ModelState`
- Standard ASP.NET Core validation infrastructure
- Works with `[ApiController]` attribute for automatic 400 responses

`Money` is the main built-in exception here: it is a structured value object with its own JSON converter, so it is not part of the automatic scalar model-binding/validation pipeline.

## Core Concepts

### Available Value Objects

This library provides both **base classes** for creating custom value objects and **ready-to-use** value objects for common scenarios:

#### Base Classes (with Source Generation)
| Value Object | Base Class | Purpose | Key Features |
|-------------|-----------|----------|-------------|
| **RequiredString** | Primitive wrapper | Non-empty strings | Source generation, IScalarValue, IParsable, ASP.NET validation, optional `[StringLength]` constraints |
| **RequiredGuid** | Primitive wrapper | Non-default GUIDs | Source generation, IScalarValue, NewUniqueV4()/V7(), ASP.NET validation |
| **RequiredInt** | Primitive wrapper | Non-default integers | Source generation, IScalarValue, IParsable, ASP.NET validation |
| **RequiredDecimal** | Primitive wrapper | Non-default decimals | Source generation, IScalarValue, IParsable, ASP.NET validation |
| **RequiredEnum** | Enum value object | Type-safe enumerations | Source generation, behavior, state machines, JSON serialization |

#### Ready-to-Use Scalar Value Objects
| Value Object | Purpose | Validation Rules | Example |
|-------------|----------|-----------------|---------|
| **EmailAddress** | Email validation | RFC 5322 compliant, trimmed | `user@example.com` |
| **Url** | Web URLs | Absolute HTTP/HTTPS URIs | `https://example.com/path` |
| **PhoneNumber** | Phone numbers | E.164 format | `+14155551234` |
| **Percentage** | Percentage values | 0-100 range, supports % suffix | `15.5` or `15.5%` |
| **CurrencyCode** | Currency codes | ISO 4217 3-letter codes | `USD`, `EUR`, `GBP` |
| **IpAddress** | IP addresses | IPv4 and IPv6 | `192.168.1.1` or `::1` |
| **Hostname** | Hostnames | RFC 1123 compliant | `example.com` |
| **Slug** | URL slugs | Lowercase, digits, hyphens | `my-blog-post` |
| **CountryCode** | Country codes | ISO 3166-1 alpha-2 | `US`, `GB`, `FR` |
| **LanguageCode** | Language codes | ISO 639-1 alpha-2 | `en`, `es`, `fr` |
| **Age** | Age values | 0-150 range | `42` |

#### Ready-to-Use Structured Value Object
| Value Object | Purpose | Validation Rules | Example |
|-------------|----------|-----------------|---------|
| **Money** | Monetary amounts | Non-negative amount + ISO 4217 currency | `99.99 USD` |

**What are Primitive Value Objects?**

Primitive value objects wrap single primitive types (`string`, `Guid`, etc.) to provide:
- **Type safety**: Prevents mixing semantically different values
- **Domain semantics**: Makes code self-documenting and expressive
- **Validation**: Encapsulates validation rules at creation time
- **Immutability**: Ensures values cannot change after creation

`Money` is intentionally separate from that scalar bucket. It is still a value object, but it is a structured one composed of multiple meaningful components.

**Generated Code Features:**
- `TryCreate` methods returning `Result<T>` with optional `fieldName` parameter
- `IParsable<T>` implementation (`Parse`/`TryParse`)
- Explicit cast operators
- Validation with descriptive error messages
- Property name inference for error messages (class name converted to camelCase)
- JSON serialization via `ParsableJsonConverter<T>`
- OpenTelemetry activity tracing support, typically a better day-to-day diagnostic signal than full ROP tracing because it emits spans at value creation and validation boundaries

## Best Practices

1. **Use partial classes**  
   Required for source code generation to work correctly.

2. **Leverage generated methods**  
   Use `TryCreate` for safe parsing that returns `Result<T>`.

3. **Use the fieldName parameter**  
   Pass custom field names for better validation error messages in APIs.

4. **Compose with other value objects**  
   Combine multiple value objects using `Combine` for validation.

5. **Use meaningful names**  
   Class name becomes part of the error message (e.g., "Employee Id cannot be empty.").

6. **Prefer specific types over primitives**  
   `EmployeeId` is more expressive than `Guid` or `string` - eliminates primitive obsession.

## Related Packages

- [Trellis.Primitives.Generator](https://www.nuget.org/packages/Trellis.Primitives.Generator) — Source generator (required companion)
- [Trellis.Results](https://www.nuget.org/packages/Trellis.Results) — Core `Result<T>` type
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — Entity and aggregate patterns
- [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) — ASP.NET Core integration

## License

MIT — see [LICENSE](../LICENSE) for details.
