# TRLS004 — Unsafe access to Result.Error

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `result.Error` when the analyzer cannot prove that the access is guarded by a failure check or another safe Trellis pattern.

## Why it matters
`Result.Error` throws when the result is a success. Unchecked access swaps a clean result flow for an exception.

> [!WARNING]
> This often appears in logging and HTTP mapping code. Make the failure path explicit before reading `Error`.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static string Bad(Result<int> result) =>
        result.Error.Detail;
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static string Good(Result<int> result)
    {
        if (result.IsFailure)
            return result.Error.Detail;

        return "No error";
    }
}
```

## Code fix available
Yes — wraps the current usage in an `if (result.IsFailure)` guard.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS004.severity = none
```

```csharp
#pragma warning disable TRLS004
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS004
```

> [!TIP]
> When you need to translate both branches, `Match` (with a `switch` expression on the closed `Error` ADT) usually keep the code cleaner than a manual `Error` read.

