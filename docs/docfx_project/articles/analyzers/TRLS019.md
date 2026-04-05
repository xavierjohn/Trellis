# TRLS019 — Combine chain exceeds maximum supported tuple size

- **Severity:** Error
- **Category:** Trellis

## What it detects
Flags the outermost `Combine(...)` or `CombineAsync(...)` chain when it grows past Trellis's supported tuple width of nine elements.

## Why it matters
Downstream Trellis tuple-based APIs also stop at nine elements. Large combine chains are a sign that related inputs should be grouped first.

> [!WARNING]
> This is the one rule where the bad example usually also causes a compile error: the tenth `Combine` has no matching overload. The analyzer gives you a clearer explanation of what to refactor.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static void Bad(
        Result<int> r1,
        Result<int> r2,
        Result<int> r3,
        Result<int> r4,
        Result<int> r5,
        Result<int> r6,
        Result<int> r7,
        Result<int> r8,
        Result<int> r9,
        Result<int> r10)
    {
        var combined = r1
            .Combine(r2)
            .Combine(r3)
            .Combine(r4)
            .Combine(r5)
            .Combine(r6)
            .Combine(r7)
            .Combine(r8)
            .Combine(r9)
            .Combine(r10);
    }
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static void Good(
        Result<int> r1,
        Result<int> r2,
        Result<int> r3,
        Result<int> r4,
        Result<int> r5,
        Result<int> r6,
        Result<int> r7,
        Result<int> r8,
        Result<int> r9,
        Result<int> r10)
    {
        var customerGroup = r1
            .Combine(r2)
            .Combine(r3)
            .Combine(r4)
            .Combine(r5);

        var orderGroup = r6
            .Combine(r7)
            .Combine(r8)
            .Combine(r9)
            .Combine(r10);

        var combined = customerGroup.Combine(orderGroup);
    }
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS019.severity = none
```

```csharp
#pragma warning disable TRLS019
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS019
```

> [!TIP]
> Create intermediate value objects or grouped validation results, then combine those smaller units.

