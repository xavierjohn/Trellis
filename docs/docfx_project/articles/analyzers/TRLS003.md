# TRLS003 — Unsafe access to Result.Value

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `result.Value` when the analyzer cannot prove that the access is guarded by a success check or another safe Trellis pattern.

## Why it matters
`Result.Value` throws when the result is a failure. Unchecked access turns a modeled failure into an exception.

> [!WARNING]
> The analyzer understands many guard patterns, but it will still warn when the safety is unclear. When in doubt, prefer `IsSuccess`, `TryGetValue`, or `Match`.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static int Bad(Result<int> result) =>
        result.Value + 1;
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static int Good(Result<int> result)
    {
        if (result.IsSuccess)
            return result.Value + 1;

        return 0;
    }
}
```

## Code fix available
Yes — wraps the current usage in an `if (result.IsSuccess)` guard.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS003.severity = none
```

```csharp
#pragma warning disable TRLS003
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS003
```

> [!TIP]
> `Match`, `TryGetValue`, and early-return guards usually read better than reaching for `Value` directly.

