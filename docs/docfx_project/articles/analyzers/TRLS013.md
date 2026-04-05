# TRLS013 — Consider using GetValueOrDefault or Match

- **Severity:** Info
- **Category:** Trellis

## What it detects
Flags the pattern `result.IsSuccess ? result.Value : fallback` when the success check and the value access refer to the same stable result expression.

## Why it matters
Trellis already provides `GetValueOrDefault(...)` and `Match(...)` for this exact scenario. They read better and avoid direct `Value` access.

> [!WARNING]
> The analyzer intentionally skips unstable receivers like repeated method calls so it does not change behavior.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static int Bad(Result<int> result) =>
        result.IsSuccess ? result.Value : 0;
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static int Good(Result<int> result) =>
        result.GetValueOrDefault(0);
}
```

## Code fix available
Yes — replaces the ternary with `GetValueOrDefault(...)` or `Match(...)`, depending on the fallback expression.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS013.severity = none
```

```csharp
#pragma warning disable TRLS013
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS013
```

> [!TIP]
> Use `GetValueOrDefault(...)` for simple fallback values and `Match(...)` when the failure branch needs more logic.

