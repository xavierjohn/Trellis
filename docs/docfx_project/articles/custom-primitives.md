---
title: Create Custom Scalar Value Objects
package: Trellis.Core
topics: [value-object, validation, source-generator, ddd]
last_verified: 2026-10-02
audience: [developer]
---
# Create custom scalar value objects

Use a custom scalar value object when a single CLR value has domain meaning that a raw primitive cannot express. `OrderId`, `ProductName`, and `Quantity` may still store a `Guid`, `string`, and `int`, but they should not be interchangeable.

Install `Trellis.Core` directly, or use the transitive reference supplied by `Trellis.Primitives`:

```bash
dotnet add package Trellis.Core
```

## Pick the matching base

| Underlying value | Generated base |
|---|---|
| `string` | [RequiredString<TSelf>](xref:Trellis.RequiredString`1) |
| `Guid` | [RequiredGuid<TSelf>](xref:Trellis.RequiredGuid`1) |
| `int` | [RequiredInt<TSelf>](xref:Trellis.RequiredInt`1) |
| `long` | [RequiredLong<TSelf>](xref:Trellis.RequiredLong`1) |
| `decimal` | [RequiredDecimal<TSelf>](xref:Trellis.RequiredDecimal`1) |
| `bool` | [RequiredBool<TSelf>](xref:Trellis.RequiredBool`1) |
| `DateTime` | [RequiredDateTime<TSelf>](xref:Trellis.RequiredDateTime`1) |
| `DateTimeOffset` | [RequiredDateTimeOffset<TSelf>](xref:Trellis.RequiredDateTimeOffset`1) |
| A finite symbolic set | [RequiredEnum<TSelf>](xref:Trellis.RequiredEnum`1) and the [symbolic values guide](required-enum.md) |

Declare a `partial class` and place Trellis attributes on the class itself:

```csharp
using Trellis;

[NotDefault]
public sealed partial class OrderId : RequiredGuid<OrderId>;

[Trim, NotDefault, StringLength(120, MinimumLength = 2)]
public sealed partial class ProductName : RequiredString<ProductName>;

[Range(1, 1_000)]
public sealed partial class Quantity : RequiredInt<Quantity>;

public sealed record AddOrderLine(OrderId OrderId, ProductName Product, Quantity Quantity)
{
    public static Result<AddOrderLine> TryCreate(string? product, int? quantity) =>
        Result.Combine(
                ProductName.TryCreate(product, nameof(product)),
                Quantity.TryCreate(quantity, nameof(quantity)))
            .Map((validProduct, validQuantity) =>
                new AddOrderLine(OrderId.NewUniqueV7(), validProduct, validQuantity));
}
```

The generator supplies the factories, scalar `Value`, parsing, equality, formatting, and JSON converter. Do not redeclare those members in the partial class.

## Make the default as strict as your domain

The word "Required" means the input cannot be `null`; it does not reject every CLR sentinel automatically.

| Base | Accepted unless you opt out |
|---|---|
| `RequiredString<TSelf>` | `""` and whitespace, without trimming |
| `RequiredGuid<TSelf>` | `Guid.Empty` |
| Numeric bases | `0` |
| Date/time bases | `MinValue` |
| `RequiredBool<TSelf>` | Both `false` and `true` are valid |

Use [TrimAttribute](xref:Trellis.TrimAttribute) to normalize a string and [NotDefaultAttribute](xref:Trellis.NotDefaultAttribute) to reject its sentinel. Combining `[Trim, NotDefault]` rejects whitespace after trimming. Add [StringLengthAttribute](xref:Trellis.StringLengthAttribute) or [RangeAttribute](xref:Trellis.RangeAttribute) for bounded values.

These are `Trellis` attributes, not similarly named `System.ComponentModel.DataAnnotations` attributes.

## Add a domain-specific rule

Attributes cover common constraints. Implement the generated `ValidateAdditional` hook when the value has its own rule:

```csharp
using Trellis;

[Trim, NotDefault, StringLength(12, MinimumLength = 8)]
public sealed partial class Sku : RequiredString<Sku>
{
    static partial void ValidateAdditional(
        string value,
        string fieldName,
        ref string? errorMessage,
        ref string? errorCode)
    {
        if (value.StartsWith("SKU-", StringComparison.Ordinal))
            return;

        errorMessage = $"{fieldName} must start with SKU-.";
        errorCode = "catalog.sku.prefix";
    }
}
```

Set both the message and a stable application-owned code. The code lets clients react without parsing prose.

## Choose the right factory

| Factory | Use it when |
|---|---|
| `TryCreate(value, fieldName)` | Input may be invalid. Keep the returned `Result<T>` in the pipeline. |
| `Create(value)` | The value is a trusted constant or test fixture. Invalid input throws. |
| `Parse` / `TryParse` | A .NET API requires `IParsable<T>`. |
| `NewUniqueV7()` | You are creating a new `RequiredGuid<TSelf>` identity. |

Pass the transport field name to `TryCreate`. ASP.NET Core can then return a validation pointer that matches the request instead of the type's default name.

## Serialization and persistence

Generated scalar value objects carry [ParsableJsonConverter<T>](xref:Trellis.ParsableJsonConverter`1), so their JSON shape is the underlying scalar rather than an object containing `Value`.

For request binding and validation responses, continue with [ASP.NET Core integration](integration-aspnet.md). For EF Core mapping and runtime query rewriting, wire the [conventions and interceptors](integration-ef.md#conventions-and-interceptors). The single-argument `StartsWith`, `Contains`, and `EndsWith` helpers plus `Length` on `RequiredString<TSelf>` are translated after `AddTrellisInterceptors()` is registered; use the generated [RequiredString<TSelf> API](xref:Trellis.RequiredString`1) for their complete signatures and in-memory comparison semantics.

For the complete generated and inherited member lists, use the .NET API pages linked in the base-class table.
