# Primitive Value Objects

Primitive obsession makes code look simple right up until `"USD"`, `"Pending"`, `"john@example.com"`, `42`, and `true` all start carrying business meaning the compiler cannot see.

Trellis primitives solve that by turning raw values into **small, validated domain types**.

## Start with a practical example

```csharp
using Trellis;
using Trellis.Primitives;

namespace PrimitiveExamples;

public partial class CustomerId : RequiredGuid<CustomerId> { }

[Trellis.StringLength(200)]
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
```

Now your model says what the values **mean**, not just what CLR type they happen to use.

## Why primitives help

They give you:

- **type safety** — `DisplayName` and `EmailAddress` cannot be mixed up
- **centralized validation** — every creation path enforces the same rules
- **better APIs** — invalid input fails early through `Result<T>`
- **clearer domain language** — code reads like the business language

## The core base classes

All custom primitives are declared with the generic self type:

- `RequiredString<EmailAddress>`
- `RequiredGuid<OrderId>`
- `RequiredInt<Quantity>`

Not just `RequiredString` or `RequiredGuid`.

| Base class | Use for | Notes |
| --- | --- | --- |
| `RequiredString<TSelf>` | names, codes, titles | exposes `Length`, `StartsWith`, `Contains`, `EndsWith` |
| `RequiredGuid<TSelf>` | IDs | source generator adds `NewUniqueV4()` and `NewUniqueV7()` |
| `RequiredInt<TSelf>` | counts, quantities | supports range validation |
| `RequiredDecimal<TSelf>` | scalar decimals | supports range validation |
| `RequiredLong<TSelf>` | large integer values | available for high-range counters/identifiers |
| `RequiredBool<TSelf>` | explicit domain flags | useful when `bool` has real business meaning |
| `RequiredDateTime<TSelf>` | required timestamps | invariant round-trip formatting |
| `RequiredEnum<TSelf>` | symbolic finite sets | separate symbolic type family |

> [!NOTE]
> `ScalarValueObject<TSelf, T>` implements both `IConvertible` and `IFormattable`. That is why Trellis scalar primitives behave naturally in formatting and conversion scenarios.

## Creating your own primitive

Most custom primitives are tiny:

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

That `partial` keyword matters. It allows the generator to add factory methods, parsing, JSON support, and model-binding helpers.

## Factory methods: use the right one

Every primitive follows the same creation story:

- `TryCreate(...)` for user input, file input, API input, or anything that may fail
- `Create(...)` for trusted test data or hard-coded values

```csharp
using Trellis;
using Trellis.Primitives;

public partial class OrderId : RequiredGuid<OrderId> { }

var emailResult = EmailAddress.TryCreate("alice@example.com");
var orderId = OrderId.NewUniqueV7();
var amount = MonetaryAmount.Create(49.95m);
```

In a request workflow, stay on the railway:

```csharp
using Trellis;

[Trellis.StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }

[Trellis.Range(1, 1000)]
public partial class Quantity : RequiredInt<Quantity> { }

var command = EmailAddress.TryCreate("alice@example.com", "email")
    .Combine(ProductName.TryCreate("Trellis Mug", "name"))
    .Combine(Quantity.TryCreate(2, "quantity"));
```

## `RequiredString<TSelf>` feels like a string on purpose

`RequiredString<TSelf>` exposes the string members you need most:

- `Length`
- `StartsWith(string)`
- `Contains(string)`
- `EndsWith(string)`

```csharp
using Trellis;

[Trellis.StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }

var name = ProductName.Create("Trellis Mug");

_ = name.Length;
_ = name.StartsWith("Trellis");
_ = name.Contains("Mug");
_ = name.EndsWith("Mug");
```

That makes your code more natural and helps EF Core queries read cleanly.

## Built-in primitives you can use immediately

Trellis also ships ready-made types in `Trellis.Primitives`.

| Type | Category | Good for |
| --- | --- | --- |
| `EmailAddress` | scalar string | validated email input |
| `PhoneNumber` | scalar string | E.164 phone values |
| `Url` | scalar string | absolute HTTP/HTTPS URLs |
| `Hostname` | scalar string | host names |
| `IpAddress` | scalar string | IPv4/IPv6 values |
| `Slug` | scalar string | URL-friendly identifiers |
| `CountryCode` | scalar string | ISO alpha-2 country codes |
| `CurrencyCode` | scalar string | ISO 4217 codes |
| `LanguageCode` | scalar string | ISO language codes |
| `Age` | scalar int | bounded age values |
| `Percentage` | scalar decimal | percentages and fraction helpers |
| `MonetaryAmount` | scalar decimal | **single-currency** amounts |
| `Money` | structured value object | amount + currency together |

### `MonetaryAmount` vs `Money`

This distinction matters:

| Type | Use it when | Shape |
| --- | --- | --- |
| `MonetaryAmount` | the whole bounded context uses one currency policy | scalar |
| `Money` | currency is part of the value's identity | structured |

`MonetaryAmount` is a scalar primitive. `Money` is not.

```csharp
using Trellis.Primitives;

var subtotal = MonetaryAmount.Create(120.00m);
var tax = subtotal.Multiply(0.08m).Value;
var total = subtotal.Add(tax).Value;

var usdPrice = Money.Create(120.00m, "USD");
var shipping = Money.Create(10.00m, "USD");
var grandTotal = usdPrice.Add(shipping).Value;
```

## Built-in examples

```csharp
using System;
using Trellis.Primitives;

var email = EmailAddress.Create("alice@example.com");
var url = Url.Create("https://example.com/orders/42");
var percent = Percentage.Create(12.5m);

Console.WriteLine(url.Host);
Console.WriteLine(percent.AsFraction());
Console.WriteLine(percent.Of(80m));
```

## Culture-aware parsing

Numeric and date primitives support culture-aware parsing through `IFormattableScalarValue<TSelf, TPrimitive>`.

Use the invariant overload by default:

```csharp
var amount = MonetaryAmount.TryCreate("12.34");
```

Use the culture-aware overload when the locale is known:

```csharp
using System.Globalization;
using Trellis;
using Trellis.Primitives;

public partial class LastPurchaseAt : RequiredDateTime<LastPurchaseAt> { }

var amount = MonetaryAmount.TryCreate("12,34", CultureInfo.GetCultureInfo("fr-FR"));
var timestamp = LastPurchaseAt.TryCreate("2024-12-31T18:30:00", CultureInfo.InvariantCulture);
```

## EF Core queries stay readable

Trellis primitives are designed to work cleanly in LINQ. In many cases you do **not** need to reach into `.Value`.

```csharp
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Trellis;

namespace EfCoreQueryExample;

[Trellis.StringLength(200)]
public partial class DisplayName : RequiredString<DisplayName> { }

public partial class IsVipCustomer : RequiredBool<IsVipCustomer> { }

public sealed class Customer
{
    public int Id { get; set; }
    public DisplayName DisplayName { get; set; } = null!;
    public IsVipCustomer IsVipCustomer { get; set; } = null!;
}

public sealed class AppDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public static IQueryable<Customer> BuildQuery(AppDbContext dbContext) =>
    dbContext.Customers
        .Where(customer => customer.DisplayName.StartsWith("Tre"))
        .Where(customer => customer.DisplayName.Length > 3)
        .Where(customer => customer.IsVipCustomer == IsVipCustomer.Create(true));
```

> [!NOTE]
> String helper translation such as `StartsWith`, `Contains`, `EndsWith`, and `Length` requires `AddTrellisInterceptors()` in your EF Core configuration.

## When to use built-in types vs custom types

Choose a built-in primitive when the validation rules already match your domain.

Create a custom primitive when:

- the name needs domain meaning
- the validation rules differ
- the type should carry behavior specific to your domain

For example, you might still create your own email type:

```csharp
using Trellis;

[Trellis.StringLength(100)]
public partial class CompanyEmailAddress : RequiredString<CompanyEmailAddress> { }
```

## Practical guidance

1. Wrap IDs immediately
2. Prefer domain names over technical names
3. Use `TryCreate(...)` at boundaries
4. Reach for built-ins first, custom types second
5. Use `MonetaryAmount` only when currency is truly external policy

## See also

- [RequiredEnum](required-enum.md)
- [Specifications](specifications.md)
- [Trellis primitive taxonomy](../api_reference/trellis-value-object-taxonomy.md)
- [Trellis primitives API reference](../api_reference/trellis-api-primitives.md)
