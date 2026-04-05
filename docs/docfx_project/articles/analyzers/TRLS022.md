# TRLS022 — Wrong [StringLength] or [Range] attribute namespace

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `System.ComponentModel.DataAnnotations.StringLengthAttribute` and `RangeAttribute` when they are applied to Trellis value-object base types.

## Why it matters
The code compiles, but the Trellis source generator only understands the Trellis versions of these attributes. Your intended validation rules never make it into the generated type.

> [!WARNING]
> This is a namespace problem, not a syntax problem. The attribute name looks right, but the generator ignores the DataAnnotations version.

## Bad example
```csharp
using Trellis;

[System.ComponentModel.DataAnnotations.StringLength(50)]
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
dotnet_diagnostic.TRLS022.severity = none
```

```csharp
#pragma warning disable TRLS022
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS022
```

> [!TIP]
> Import or fully qualify `Trellis.StringLength` and `Trellis.Range` on Trellis value objects.

