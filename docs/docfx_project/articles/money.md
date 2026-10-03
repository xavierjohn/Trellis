---
title: Money, Amounts, and Percentages
package: Trellis.Primitives
topics: [money, value-object, percentage, validation]
last_verified: 2026-10-02
audience: [developer]
---
# Work with money, amounts, and percentages

Financial values need more than a `decimal`. They need a clear currency policy, controlled rounding, and failures that remain visible when a calculation is invalid.

## Choose the value that carries the right meaning

| Type | Use it when |
|---|---|
| [MonetaryAmount](xref:Trellis.Primitives.MonetaryAmount) | The bounded context has one external currency policy, so the value's identity is only a non-negative amount. |
| [Money](xref:Trellis.Primitives.Money) | Currency travels with the amount and participates in equality and arithmetic. |
| [Percentage](xref:Trellis.Primitives.Percentage) | The domain uses a value from `0` through `100`, or receives a fraction from `0` through `1`. |
| [CurrencyCode](xref:Trellis.Primitives.CurrencyCode) | You need a standalone three-letter currency-shaped code. |

Install the package:

```bash
dotnet add package Trellis.Primitives
```

## Keep calculations on the railway

This complete console example validates a subtotal and tax fraction, then calculates the total without introducing an exception path:

```csharp
using Trellis;
using Trellis.Primitives;

var result = Checkout.TotalWithTax(
    subtotal: 120m,
    currency: "USD",
    taxFraction: 0.0825m);

if (!result.TryGetValue(out var total, out var error))
{
    Console.Error.WriteLine(error);
    return;
}

Console.WriteLine(total);

public static class Checkout
{
    public static Result<Money> TotalWithTax(
        decimal subtotal,
        string currency,
        decimal taxFraction) =>
        Money.TryCreate(
                subtotal,
                currency,
                nameof(subtotal),
                nameof(currency))
            .Bind(money =>
                Percentage.FromFraction(taxFraction, nameof(taxFraction))
                    .Bind(rate =>
                        money.Multiply(rate.AsFraction())
                            .Bind(money.Add)));
}
```

`Money.Multiply` and `Money.Add` return `Result<Money>`. A negative subtotal, invalid fraction, arithmetic overflow, or currency mismatch remains a typed failure for the caller to handle.

## Currency is part of the rule

`Money.Add`, `Subtract`, and `Sum` require matching currencies. `CurrencyCode` normalizes case and validates a three-letter ASCII shape, but it does not enforce the active ISO 4217 list or a payment provider's supported set. Add that policy at your application boundary.

`Money` rounds according to the currency's minor units. Use `MonetaryAmount` only when a single-currency policy is already guaranteed outside the value itself.

## Split and aggregate deliberately

[Money.Allocate](xref:Trellis.Primitives.Money.Allocate(System.Int32[])) divides an amount by positive ratios while distributing the minor-unit remainder. [Money.Sum](xref:Trellis.Primitives.Money.Sum(System.Collections.Generic.IEnumerable{Trellis.Primitives.Money})) rejects an empty sequence and mixed currencies; its fallback overload lets the caller provide a meaningful currency for an empty sequence.

For percentage input, use `Percentage.TryCreate` when the input is already in `0..100`, and `Percentage.FromFraction` for `0..1`. `AsFraction()` converts back before multiplication.

Use the generated API pages for the complete arithmetic and overload reference:

- [Money](xref:Trellis.Primitives.Money)
- [MonetaryAmount](xref:Trellis.Primitives.MonetaryAmount)
- [Percentage](xref:Trellis.Primitives.Percentage)
