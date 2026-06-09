# TRLS017 — Wrong [StringLength] or [Range] attribute namespace

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `System.ComponentModel.DataAnnotations.StringLengthAttribute` and `RangeAttribute` when they are applied to Trellis value-object base types.

## Why it matters
The `System.ComponentModel.DataAnnotations` attributes target properties, fields, or parameters — not classes — so applying them to a value object does not compile: `CS0592` when the attribute is fully qualified (or only the DataAnnotations namespace is imported), or `CS0104` (ambiguous reference) for an unqualified attribute when the `Trellis` namespace is also in scope. The Trellis source generator reads only the `Trellis` versions, which target the class declaration.

> [!WARNING]
> This is a namespace problem, not a syntax problem. The attribute name looks right, but the DataAnnotations version cannot be applied to a class, so the build fails until you switch to the `Trellis` attribute.

## Bad example
```csharp
using Trellis;

[System.ComponentModel.DataAnnotations.StringLength(50)]   // CS0592 — not valid on a class
public sealed partial class FirstName : RequiredString<FirstName>
{
}
```

## Good example
```csharp
using Trellis;

[StringLength(50)]
public sealed partial class FirstName : RequiredString<FirstName>
{
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS017.severity = none
```

```csharp
#pragma warning disable TRLS017
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS017
```

> [!TIP]
> Import or fully qualify `Trellis.StringLength` and `Trellis.Range` on Trellis value objects.

