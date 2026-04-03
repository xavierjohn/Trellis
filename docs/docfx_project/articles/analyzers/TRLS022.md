# TRLS022: Wrong \[StringLength\] or \[Range\] attribute namespace

## Cause

A type inheriting from a Trellis base class (`RequiredString<T>`, `RequiredInt<T>`, `RequiredDecimal<T>`, `RequiredLong<T>`, etc.) is decorated with `System.ComponentModel.DataAnnotations.StringLengthAttribute` or `System.ComponentModel.DataAnnotations.RangeAttribute` instead of the Trellis versions (`Trellis.StringLengthAttribute`, `Trellis.RangeAttribute`).

## Rule Description

Trellis `[StringLength]` and `[Range]` share names with the `System.ComponentModel.DataAnnotations` versions. Using the wrong namespace compiles silently, but the Trellis source generator ignores the DataAnnotations attributes — resulting in value objects without the expected validation constraints.

This typically happens when a `using System.ComponentModel.DataAnnotations;` directive is present (e.g., from a DTO or model class) and the attribute resolves to the wrong type.

This rule fires as a **Warning** because the code compiles without errors but the generated `TryCreate` method will not enforce the intended length or range constraint.

## How to Fix Violations

Replace the DataAnnotations attribute with the Trellis version. The `global using Trellis;` directive (included in the template) makes the Trellis attributes available without a namespace prefix.

### StringLength

```csharp
// ❌ Bad — System.ComponentModel.DataAnnotations.StringLength (generator ignores this)
[System.ComponentModel.DataAnnotations.StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }

// ✅ Good — Trellis.StringLength (generator uses this for validation)
[StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }
```

### Range

```csharp
// ❌ Bad — System.ComponentModel.DataAnnotations.Range (generator ignores this)
[System.ComponentModel.DataAnnotations.Range(1, 1000)]
public partial class Quantity : RequiredInt<Quantity> { }

// ✅ Good — Trellis.Range (generator uses this for validation)
[Range(1, 1000)]
public partial class Quantity : RequiredInt<Quantity> { }
```

## When to Suppress

Do not suppress this warning. If you intentionally need the DataAnnotations attribute for a non-Trellis purpose (e.g., ASP.NET model validation on a DTO), apply it to the DTO class instead of the Trellis value object.
