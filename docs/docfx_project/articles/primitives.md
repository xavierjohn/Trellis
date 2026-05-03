---
title: Primitive Value Objects
package: Trellis.Primitives
topics: [primitive-obsession, value-object, validation, json, ef-core, type-converter]
related_api_reference: [trellis-api-primitives.md, trellis-api-core.md]
last_verified: 2026-05-03
audience: [developer]
---
# Primitive Value Objects

`Trellis.Primitives` turns raw CLR values into small, validated domain types so `"USD"`, `"john@example.com"`, `42`, and `true` stop carrying business meaning the compiler cannot see.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Use a ready-made validated email / URL / phone / ISO code | Built-in concrete VOs in `Trellis.Primitives` (`EmailAddress`, `Url`, `PhoneNumber`, `CountryCode`, ...) | [Built-in primitives](#built-in-primitives) |
| Wrap an ID, name, count, flag, or timestamp from your own domain | `partial class X : Required*<X>` from `Trellis.Core` | [Defining custom primitives](#defining-custom-primitives) |
| Constrain a custom string length or numeric range | `[Trellis.StringLength(...)]` / `[Trellis.Range(...)]` on the partial class | [Validation](#validation) |
| Add a regex / pattern check | Override `static partial void ValidateAdditional(...)` | [Validation](#validation) |
| Construct from untrusted input on the railway | `TSelf.TryCreate(value, "field")` returning `Result<TSelf>` | [Factory methods](#factory-methods) |
| Construct in tests / from trusted constants | `TSelf.Create(value)` (throws on invalid input) | [Factory methods](#factory-methods) |
| Generate a new ID | `TId.NewUniqueV4()` / `TId.NewUniqueV7()` | [Defining custom primitives](#defining-custom-primitives) |
| Serialize a scalar primitive to/from JSON | `ParsableJsonConverter<T>` (auto-applied by the generator) | [JSON binding](#json-binding) |
| Serialize a multi-field value object (e.g., `Money`-shaped) | `[JsonConverter(typeof(CompositeValueObjectJsonConverter<MyVo>))]` | [JSON binding](#json-binding) |
| Translate `StartsWith` / `Contains` / `Length` in EF Core LINQ | Register `AddTrellisInterceptors()` on the `DbContextOptionsBuilder` | [EF Core interop](#ef-core-interop) |
| Combine several `TryCreate` calls into one validated command | `Result.Combine(...)` (see `Trellis.Core`) | [Composition](#composition) |

## Use this guide when

- You want a typed domain instead of `string` / `int` / `Guid` parameters with implicit validation rules.
- You need JSON, model-binding, and EF Core to flow through a single `TryCreate(...)` so validation is enforced at every seam.
- You are deciding between a built-in primitive (`EmailAddress`, `Money`, ...) and a custom `Required*<TSelf>` for your own SKU, order id, or display name.

## Surface at a glance

`Trellis.Primitives` ships 13 ready-made concrete value objects plus the composite JSON converter and OpenTelemetry registration extension that backs them. The `Required*<TSelf>` base classes, validation attributes, source generator, scalar JSON converter, and primitive trace source that you use to define **your own** primitives all live in `Trellis.Core` and are pulled in transitively.

| Area | Key APIs | Lives in |
|---|---|---|
| Built-in scalar VOs | `Age`, `CountryCode`, `CurrencyCode`, `EmailAddress`, `Hostname`, `IpAddress`, `LanguageCode`, `MonetaryAmount`, `Percentage`, `PhoneNumber`, `Slug`, `Url` | `Trellis.Primitives` |
| Built-in structured VO | `Money` (`amount` + `currency`) | `Trellis.Primitives` |
| Custom-primitive bases | `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, `RequiredInt<TSelf>`, `RequiredLong<TSelf>`, `RequiredDecimal<TSelf>`, `RequiredBool<TSelf>`, `RequiredDateTime<TSelf>`, `RequiredEnum<TSelf>` | `Trellis.Core` |
| Validation attributes | `[Trellis.StringLength]`, `[Trellis.Range]`, `[Trellis.EnumValue]` | `Trellis.Core` |
| Pattern / cross-field hook | `static partial void ValidateAdditional(value, fieldName, ref string? errorMessage)` | generator-emitted |
| Scalar JSON converters | `ParsableJsonConverter<T>` (scalars), `RequiredEnumJsonConverter<TRequiredEnum>` | `Trellis.Core` |
| Composite JSON converter | `CompositeValueObjectJsonConverter<T>` | `Trellis.Primitives` |
| Tracing source | `PrimitiveValueObjectTrace.ActivitySource`, `PrimitiveValueObjectTrace.ActivitySourceName` | `Trellis.Core` |
| Tracing registration | `AddPrimitiveValueObjectInstrumentation(this TracerProviderBuilder)` | `Trellis.Primitives` |

Full signatures: [trellis-api-primitives.md](../api_reference/trellis-api-primitives.md).

## Installation

```bash
dotnet add package Trellis.Primitives
```

`Trellis.Primitives` depends on `Trellis.Core`, which carries the `Required*<TSelf>` base classes, the validation attributes, and the source generator (bundled at `analyzers/dotnet/cs/Trellis.Core.Generator.dll`). Installing `Trellis.Primitives` is enough — you do not need a separate generator package.

## Quick start

Define a few primitives, then build an entity that cannot be constructed from invalid input.

```csharp
using Trellis;
using Trellis.Primitives;

namespace QuickStart;

public partial class CustomerId : RequiredGuid<CustomerId> { }

[Trellis.StringLength(200, MinimumLength = 1)]
public partial class DisplayName : RequiredString<DisplayName> { }

[Trellis.Range(0, 150)]
public partial class LoyaltyScore : RequiredInt<LoyaltyScore> { }

public partial class IsVipCustomer : RequiredBool<IsVipCustomer> { }
public partial class LastPurchaseAt : RequiredDateTime<LastPurchaseAt> { }

public sealed class Customer : Entity<CustomerId>
{
    public Customer(
        CustomerId id,
        DisplayName displayName,
        EmailAddress email,
        LoyaltyScore loyaltyScore,
        IsVipCustomer isVipCustomer,
        LastPurchaseAt lastPurchaseAt)
        : base(id)
    {
        DisplayName = displayName;
        Email = email;
        LoyaltyScore = loyaltyScore;
        IsVipCustomer = isVipCustomer;
        LastPurchaseAt = lastPurchaseAt;
    }

    public DisplayName DisplayName { get; }
    public EmailAddress Email { get; }
    public LoyaltyScore LoyaltyScore { get; }
    public IsVipCustomer IsVipCustomer { get; }
    public LastPurchaseAt LastPurchaseAt { get; }
}

public static class Construction
{
    public static Result<Customer> Build(string displayName, string email, int loyaltyScore, bool isVip, DateTime lastPurchase) =>
        DisplayName.TryCreate(displayName, "displayName")
            .Combine(EmailAddress.TryCreate(email, "email"))
            .Combine(LoyaltyScore.TryCreate(loyaltyScore, "loyaltyScore"))
            .Combine(IsVipCustomer.TryCreate(isVip, "isVip"))
            .Combine(LastPurchaseAt.TryCreate(lastPurchase, "lastPurchaseAt"))
            .Map(((((DisplayName n, EmailAddress e), LoyaltyScore s), IsVipCustomer v), LastPurchaseAt t) =>
                new Customer(CustomerId.NewUniqueV7(), n, e, s, v, t));
}
```

Every primitive enforces its rule on the way in, so the entity body has nothing to validate.

## Defining custom primitives

A custom primitive is a `partial class` that inherits the appropriate `Required*<TSelf>` base. `partial` is required — the source generator emits the factory methods, parser, JSON converter, and the optional `ValidateAdditional` hook into the partial half.

| Base class | Underlying type | Built-in validation | Notable extras |
|---|---|---|---|
| `RequiredString<TSelf>` | `string` | null / empty / whitespace rejected, value trimmed; `[StringLength]` enforced | `Length`, `StartsWith(string)`, `Contains(string)`, `EndsWith(string)` |
| `RequiredGuid<TSelf>` | `Guid` | `Guid.Empty` rejected | `NewUniqueV4()`, `NewUniqueV7()` |
| `RequiredInt<TSelf>` | `int` | `null` rejected for nullable inputs; `[Range(int, int)]` enforced | invariant + culture-aware string parsing |
| `RequiredLong<TSelf>` | `long` | `null` rejected for nullable inputs; `[Range(long, long)]` enforced | invariant + culture-aware string parsing |
| `RequiredDecimal<TSelf>` | `decimal` | `null` rejected for nullable inputs; `[Range(int, int)]` or `[Range(double, double)]` enforced | invariant + culture-aware string parsing |
| `RequiredBool<TSelf>` | `bool` | `null` rejected for nullable inputs; `false` is valid | string parsing of `"true"`/`"false"` |
| `RequiredDateTime<TSelf>` | `DateTime` | `DateTime.MinValue` rejected | invariant round-trip `"O"` formatting |
| `RequiredEnum<TSelf>` | `string` | `TryFromName` lookup against `public static readonly TSelf` fields | `[EnumValue("...")]` on each field overrides the wire name |

> [!NOTE]
> The base contracts (`IScalarValue<TSelf, TPrimitive>`, `IFormattableScalarValue<TSelf, TPrimitive>`) and shared bases (`ValueObject`, `ScalarValueObject<TSelf, T>`) live in `Trellis.Core`. `ScalarValueObject<TSelf, T>` implements `IConvertible` and `IFormattable` so scalar primitives behave naturally in formatting and conversion scenarios.

```csharp
using Trellis;

namespace CustomPrimitives;

public partial class OrderId : RequiredGuid<OrderId> { }

[Trellis.StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }

[Trellis.Range(1, 1000)]
public partial class Quantity : RequiredInt<Quantity> { }

public partial class IsPublished : RequiredBool<IsPublished> { }
public partial class PublishedAt : RequiredDateTime<PublishedAt> { }
public partial class ExternalSequence : RequiredLong<ExternalSequence> { }
```

### Factory methods

The generator emits the same factory shape on every `Required*<TSelf>` derivation:

| Method | Returns | Use it for |
|---|---|---|
| `TryCreate(value, fieldName?)` | `Result<TSelf>` | User input, file input, HTTP / JSON input — anything that may fail. Also accepts the nullable underlying type and the string form. |
| `Create(value)` (and `Create(string)`) | `TSelf` | Trusted test data and hard-coded constants. Throws on invalid input. |
| `Parse(s, provider)` | `TSelf` | `IParsable<TSelf>` integration. Throws `FormatException` on failure. |
| `TryParse(s, provider, out result)` | `bool` | `IParsable<TSelf>` integration. Non-throwing. |
| `(TSelf)value` (explicit cast) | `TSelf` | Conversion via `Create(...)` — same throwing semantics. |
| `NewUniqueV4()` / `NewUniqueV7()` | `TSelf` | `RequiredGuid<TSelf>` only. Time-ordered v7 GUIDs are recommended for new IDs. |

Stay on the railway at boundaries:

```csharp
using Trellis;
using Trellis.Primitives;

[Trellis.StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }

[Trellis.Range(1, 1000)]
public partial class Quantity : RequiredInt<Quantity> { }

public sealed record AddToCart(string Email, string Product, int Qty);

public static Result<(EmailAddress, ProductName, Quantity)> Parse(AddToCart input) =>
    EmailAddress.TryCreate(input.Email, "email")
        .Combine(ProductName.TryCreate(input.Product, "name"))
        .Combine(Quantity.TryCreate(input.Qty, "quantity"))
        .Map(((EmailAddress e, ProductName p) ep, Quantity q) => (ep.e, ep.p, q));
```

### `RequiredEnum<TSelf>` shape

Use `RequiredEnum<TSelf>` when you need a finite, symbolic, extensible set with a stable wire name per member.

```csharp
using Trellis;

public partial class OrderState : RequiredEnum<OrderState>
{
    public static readonly OrderState Draft = new();

    [EnumValue("submitted")]
    public static readonly OrderState Submitted = new();
}
```

`EnumValueAttribute` is the only Trellis primitive attribute that targets a field (each `public static readonly TSelf`). Without it, the wire name is the field identifier. See [trellis-api-core.md](../api_reference/trellis-api-core.md#requiredenumtself) for `GetAll`, `TryFromName`, `Is(...)`, and equality semantics, and the dedicated [required-enum.md](required-enum.md) article for usage patterns.

## Validation

Trellis primitives enforce their rules in the generated `TryCreate`. There are three layers, in order of precedence:

1. **Built-in checks** (per base class — see the table above).
2. **Class-targeted attributes** declared on the partial class.
3. **`ValidateAdditional`** — your own pattern / cross-field rule, called last.

### Class-targeted attributes

| Attribute | Target | Constructors | Notes |
|---|---|---|---|
| `Trellis.StringLengthAttribute` | `partial class X : RequiredString<X>` | `StringLength(int maximumLength)`; set `MinimumLength = N` via property initializer | `maximumLength` must be `>= 1`. Enforced after the null/empty/whitespace check. |
| `Trellis.RangeAttribute` | `partial class X : RequiredInt<X>` / `RequiredLong<X>` / `RequiredDecimal<X>` | `(int, int)`, `(long, long)`, `(double, double)` | The constructor selected determines which generator template fires. There is **no** `RangeAttribute(typeof(decimal), "0.01", "999999.99")` overload — use `(double, double)` for fractional ranges. |

> [!WARNING]
> The `System.ComponentModel.DataAnnotations` attributes of the same name **do not work**. `[DataAnnotations.StringLength]` on the class fails to compile (`CS0592`); on a property of a `Required*<TSelf>` it compiles but is **silently ignored** by the generator. The analyzer rule `TRLS017` flags the class-placement case. Always import from `namespace Trellis`.

### Patterns and regex

There is no `[RegularExpression]` analog. Override the generated `ValidateAdditional` partial:

```csharp
using System.Text.RegularExpressions;
using Trellis;

[Trellis.StringLength(8)]
public partial class Sku : RequiredString<Sku>
{
    private static readonly Regex Pattern = new(@"^[A-Z]{3}\d{4}$", RegexOptions.Compiled);

    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
    {
        if (!Pattern.IsMatch(value))
            errorMessage = $"{fieldName} must match XXX9999.";
    }
}
```

The signature varies by base class — `string` for `RequiredString`, `Guid` for `RequiredGuid`, `int`/`long`/`decimal`/`bool`/`DateTime` for the others. See the source-generated members table in [trellis-api-core.md](../api_reference/trellis-api-core.md#source-generated-members).

### Culture-aware parsing

Numeric and date primitives expose both invariant and culture-aware string overloads through `IFormattableScalarValue<TSelf, TPrimitive>`.

```csharp
using System.Globalization;
using Trellis.Primitives;

var invariant = MonetaryAmount.TryCreate("12.34");
var french    = MonetaryAmount.TryCreate("12,34", CultureInfo.GetCultureInfo("fr-FR"));
```

## JSON binding

### Scalar primitives — automatic

Every non-enum `Required*<TSelf>` partial gets `[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]` from the Core generator. `RequiredEnum<TSelf>` partials get `[JsonConverter(typeof(RequiredEnumJsonConverter<TSelf>))]` instead. Each built-in scalar VO in `Trellis.Primitives` (`EmailAddress`, `Url`, `MonetaryAmount`, ...) follows the same Core-owned converter split. There is **nothing to register** — the non-enum converter:

- Accepts JSON `string`, `number`, `true`, `false`; converts to text and calls `TSelf.Parse(...)`.
- Writes JSON numbers for numeric scalars and JSON strings for everything else.
- Throws on JSON `null` because Trellis scalars are non-nullable.

The enum converter is string in, string out via `TryFromName`.

### Composite value objects — opt in

Multi-field value objects (the `Money` shape) need `CompositeValueObjectJsonConverter<T>` applied **per type**:

```csharp
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Primitives;

[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]
public sealed class ShippingAddress : ValueObject
{
    public ShippingAddress(CountryCode country, string line1, string postcode)
    {
        Country = country;
        Line1 = line1;
        Postcode = postcode;
    }

    public CountryCode Country { get; }
    public string Line1 { get; }
    public string Postcode { get; }

    public static Result<ShippingAddress> TryCreate(string country, string line1, string postcode, string? fieldName = null) =>
        CountryCode.TryCreate(country, $"{fieldName}.country")
            .Map(c => new ShippingAddress(c, line1, postcode));
}
```

The converter discovers properties in declaration order, populates a matching `static Result<T> TryCreate(p1, ..., pN[, string? fieldName])`, and throws `TrellisJsonValidationException` on missing properties or `TryCreate` failure. Reflection runs once per generic instantiation and is cached. Native AOT scenarios should hand-write a `JsonConverter<T>`.

> [!WARNING]
> Without `[JsonConverter(typeof(CompositeValueObjectJsonConverter<MyVo>))]` on a composite VO used in a request DTO, model binding falls back to default construction and **silently bypasses `TryCreate`** — inner-field validation never runs. See [Cookbook Recipe 13](../api_reference/trellis-api-cookbook.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership) for the full Domain + API + EF walkthrough.

## Built-in primitives

`Trellis.Primitives` ships 13 concrete value objects so you do not re-derive `Email`, `Money`, or `Slug` in every project.

| Type | Category | Wire shape | Notes |
|---|---|---|---|
| `Age` | scalar `int` | JSON number / numeric string | Range `0..150`. |
| `CountryCode` | scalar `string` | JSON string | Uppercase ISO 3166-1 alpha-2 (exactly two letters). |
| `CurrencyCode` | scalar `string` | JSON string | Uppercase ISO 4217 (exactly three letters). |
| `EmailAddress` | scalar `string` | JSON string | Trimmed, regex-validated. |
| `Hostname` | scalar `string` | JSON string | RFC 1123 hostname. |
| `IpAddress` | scalar `string` | JSON string | IPv4/IPv6 via `IPAddress.TryParse`; `ToIPAddress()` returns the cached parse. |
| `LanguageCode` | scalar `string` | JSON string | Lowercase ISO 639-1 alpha-2. |
| `MonetaryAmount` | scalar `decimal` | JSON number / numeric string | Non-negative; rounds to two decimals (`MidpointRounding.AwayFromZero`). Single-currency. `Add` / `Subtract` / `Multiply` / `Sum` return `Result`. |
| `Money` | structured `ValueObject` | JSON object `{ "amount": number, "currency": string }` | Amount + `CurrencyCode`. `Add` / `Subtract` / `Sum` require matching currencies. `Allocate(int[] ratios)` distributes remainders. |
| `Percentage` | scalar `decimal` | JSON number / numeric string | Range `0..100`; `ToString()` appends `%`; `FromFraction(0..1)` and `Of(amount)` helpers. |
| `PhoneNumber` | scalar `string` | JSON string | Strips spaces / dashes / parentheses then validates E.164; `GetCountryCode()` extracts the calling code. |
| `Slug` | scalar `string` | JSON string | Lowercase letters, digits, single-hyphen separators. |
| `Url` | scalar `string` | JSON string | Absolute HTTP/HTTPS only; exposes `Scheme`, `Host`, `Port`, `Path`, `Query`, `IsSecure`, `ToUri()`. |

### `MonetaryAmount` vs `Money`

| Type | Use it when | Shape |
|---|---|---|
| `MonetaryAmount` | The whole bounded context uses one currency policy. | scalar `decimal` |
| `Money` | Currency is part of the value's identity. | structured (`amount` + `currency`) |

```csharp
using Trellis.Primitives;

var subtotal = MonetaryAmount.Create(120.00m);
var tax      = subtotal.Multiply(0.08m).TryGetValue(out var t) ? t : MonetaryAmount.Zero;
var total    = subtotal.Add(tax).TryGetValue(out var s) ? s : subtotal;

var price    = Money.Create(120.00m, "USD");
var shipping = Money.Create(10.00m, "USD");
var grand    = price.Add(shipping).TryGetValue(out var g) ? g : price;
```

For full signatures of every built-in (`Add`, `Multiply`, `Allocate`, `Sum`, `FromFraction`, ...), see [trellis-api-primitives.md](../api_reference/trellis-api-primitives.md).

## ASP.NET integration

Scalar primitives bind from route / query / body once you call `AddTrellisAsp()` or `AddScalarValueValidation()` from `Trellis.Asp`:

- Model binders read the raw value, call `TValue.TryCreate`, and add validation errors to `ModelState` on failure.
- JSON converters write the underlying primitive on `Write`, read with `TryCreate` on `Read`, and route failures into the `Error.UnprocessableContent` aggregator.
- `Maybe<TValue>` wrappers pass `null` through as `Maybe.None`.

Composite VOs need the explicit `[JsonConverter(typeof(CompositeValueObjectJsonConverter<...>))]` shown above — `AddScalarValueValidation` only wires scalar converters.

See [integration-aspnet.md](integration-aspnet.md) for the request pipeline and [trellis-api-asp.md](../api_reference/trellis-api-asp.md#namespace-trellisaspvalidation) for the validation surface.

## EF Core interop

Trellis primitives are designed to read naturally in LINQ; you usually do not reach into `.Value`.

```csharp
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Trellis;

[Trellis.StringLength(200)]
public partial class DisplayName : RequiredString<DisplayName> { }

public partial class IsVipCustomer : RequiredBool<IsVipCustomer> { }

public sealed class Customer
{
    public int Id { get; set; }
    public DisplayName DisplayName { get; set; } = null!;
    public IsVipCustomer IsVipCustomer { get; set; } = null!;
}

public static IQueryable<Customer> Vips(DbContext db) =>
    db.Set<Customer>()
        .Where(c => c.DisplayName.StartsWith("Tre"))
        .Where(c => c.DisplayName.Length > 3)
        .Where(c => c.IsVipCustomer == IsVipCustomer.Create(true));
```

> [!NOTE]
> Translation of `RequiredString<TSelf>` helpers (`StartsWith`, `Contains`, `EndsWith`, `Length`) requires `optionsBuilder.AddTrellisInterceptors()` from `Trellis.EntityFrameworkCore`. The same call wires `MaybeQueryInterceptor`, the ETag interceptor, and the entity-timestamp interceptor. See [integration-ef.md](integration-ef.md).

For composite VOs (e.g., `Money`), `ApplyTrellisConventions` registers `CompositeValueObjectConvention`, which maps owned types via table-splitting where valid and falls back to `{Owner}_{Property}` tables when nested owned navigations exist. See [trellis-api-efcore.md](../api_reference/trellis-api-efcore.md).

## Composition

`TryCreate` returns `Result<T>`, so primitives compose with the rest of Trellis (`Combine`, `Map`, `Bind`, `Ensure`).

```csharp
using Trellis;
using Trellis.Primitives;

[Trellis.StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }

[Trellis.Range(1, 1000)]
public partial class Quantity : RequiredInt<Quantity> { }

public sealed record PlaceOrderCommand(EmailAddress Email, ProductName Product, Quantity Qty);

public static Result<PlaceOrderCommand> ParseCommand(string email, string product, int qty) =>
    EmailAddress.TryCreate(email, "email")
        .Combine(ProductName.TryCreate(product, "product"))
        .Combine(Quantity.TryCreate(qty, "qty"))
        .Map(((EmailAddress e, ProductName p) ep, Quantity q) => new PlaceOrderCommand(ep.e, ep.p, q));
```

`Combine` aggregates field-level failures into one `Error.UnprocessableContent` so every invalid field is reported in a single response. See [trellis-api-core.md](../api_reference/trellis-api-core.md) for the full ROP surface.

## Practical guidance

- **Wrap IDs immediately.** Every entity gets a `RequiredGuid<TSelf>` id; never let a raw `Guid` cross the application boundary.
- **Reach for the built-ins first.** Use `EmailAddress`, `Url`, `Money`, `CountryCode`, ... before declaring your own. Define a custom primitive only when the name needs domain meaning, the validation rules differ, or the type should carry domain-specific behavior.
- **Use `TryCreate(...)` at boundaries** (HTTP, file, queue, CLI). Reserve `Create(...)` for trusted constants and tests.
- **Always use `Trellis.*` attributes**, never `System.ComponentModel.DataAnnotations.*`. The DataAnnotations attributes of the same name compile in some positions but are silently ignored by the generator.
- **Keep parsing out of handlers.** Convert transport primitives to value objects at the DTO / controller / application seam, then pass shaped commands inward.
- **Pick `MonetaryAmount` over `Money` only when currency is external policy** for the whole bounded context.
- **Prefer v7 GUIDs** (`NewUniqueV7()`) over v4 for new IDs — they are time-ordered and storage-friendly.

## Cross-references

- API surface — built-in VOs and JSON converters: [`trellis-api-primitives.md`](../api_reference/trellis-api-primitives.md)
- API surface — `Required*<TSelf>` bases, attributes, generated members, `Result<T>` / `Maybe<T>`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Symbolic enums in depth: [required-enum.md](required-enum.md)
- Specifications over primitives: [specifications.md](specifications.md)
- HTTP request pipeline + scalar binders: [integration-aspnet.md](integration-aspnet.md), [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
- EF Core mapping + LINQ translation: [integration-ef.md](integration-ef.md), [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md)
- Composite VO end-to-end recipe: [Cookbook Recipe 13](../api_reference/trellis-api-cookbook.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership)
- Value-object taxonomy: [`trellis-value-object-taxonomy.md`](../api_reference/trellis-value-object-taxonomy.md)
