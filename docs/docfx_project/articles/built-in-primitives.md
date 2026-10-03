---
title: Built-in Value Objects
package: Trellis.Primitives
topics: [value-object, validation, primitives]
last_verified: 2026-10-02
audience: [developer]
---
# Use the built-in value objects

Before creating another email, URL, currency, or phone type, check `Trellis.Primitives`. Its input-facing built-ins share the same `TryCreate` and `Result<T>` workflow as custom scalar value objects, while keeping common normalization and validation rules in one place. Supporting query values such as `GeoBoundingBox` are obtained from `GeoBounds` or static members such as `GeoBoundingBox.World` instead of being constructed directly.

```bash
dotnet add package Trellis.Primitives
```

## Find the type by problem

| Problem | Types |
|---|---|
| Contact and network input | [EmailAddress](xref:Trellis.Primitives.EmailAddress), [PhoneNumber](xref:Trellis.Primitives.PhoneNumber), [Url](xref:Trellis.Primitives.Url), [Hostname](xref:Trellis.Primitives.Hostname), [IpAddress](xref:Trellis.Primitives.IpAddress), [Slug](xref:Trellis.Primitives.Slug) |
| Standardized codes | [CountryCode](xref:Trellis.Primitives.CountryCode), [CurrencyCode](xref:Trellis.Primitives.CurrencyCode), [LanguageCode](xref:Trellis.Primitives.LanguageCode) |
| Bounded numbers | [Age](xref:Trellis.Primitives.Age), [MonetaryAmount](xref:Trellis.Primitives.MonetaryAmount), [Percentage](xref:Trellis.Primitives.Percentage) |
| Currency-aware values | [Money](xref:Trellis.Primitives.Money) |
| Geographic values | [GeoCoordinate](xref:Trellis.Primitives.GeoCoordinate), [GeoBoundingBox](xref:Trellis.Primitives.GeoBoundingBox), [GeoBounds](xref:Trellis.Primitives.GeoBounds) |
| Weekly local-time availability | [WeeklyPeriod](xref:Trellis.Primitives.WeeklyPeriod), [WeeklySchedule](xref:Trellis.Primitives.WeeklySchedule) |

The generated .NET API pages linked above are the full member reference. This guide focuses on how the types fit into an application.

## Validate several built-ins together

The following `Program.cs` is runnable in a console project that references `Trellis.Primitives`:

```csharp
using Trellis;
using Trellis.Primitives;

var result = ContactCard.TryCreate(
    email: "ada@example.com",
    phone: "+1 (415) 555-1234",
    website: "https://example.com");

if (!result.TryGetValue(out var card, out var error))
{
    Console.Error.WriteLine(error);
    return;
}

Console.WriteLine($"{card.Email.Value} | {card.Phone.Value} | {card.Website.Host}");

public sealed record ContactCard(EmailAddress Email, PhoneNumber Phone, Url Website)
{
    public static Result<ContactCard> TryCreate(string? email, string? phone, string? website) =>
        EmailAddress.TryCreate(email, nameof(email))
            .Combine(PhoneNumber.TryCreate(phone, nameof(phone)))
            .Combine(Url.TryCreate(website, nameof(website)))
            .Map((validEmail, validPhone, validWebsite) =>
                new ContactCard(validEmail, validPhone, validWebsite));
}
```

Each factory reports against the matching input field. `Combine` preserves all independent validation failures, and the record is created only on the success track.

## Know what validation promises

Built-ins validate their documented shape; they do not replace application policy:

- `CurrencyCode` accepts a three-letter ISO-shaped code. If a payment provider supports only selected currencies, enforce that allow-list at the application boundary.
- `PhoneNumber` validates normalized E.164 shape. `GetCountryCode()` may still return `Maybe.None` for an unassigned prefix.
- `GeoBounds` produces a conservative candidate box, not an exact distance result.
- `WeeklySchedule` models recurring local-clock availability, not holidays, capacity, or job scheduling.

When a built-in's meaning or rules do not match your domain, create a [custom scalar value object](custom-primitives.md) instead of hiding extra rules around every call site.

## Continue with structured values

- [Money, amounts, and percentages](money.md)
- [Geographic coordinates and radius bounds](geographic-values.md)
- [Weekly schedules and overnight periods](weekly-schedules.md)

Browse all types in the generated [Trellis.Primitives API namespace](xref:Trellis.Primitives).
