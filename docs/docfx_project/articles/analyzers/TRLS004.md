# TRLS004 — Result is double-wrapped

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags declared or inferred `Result<Result<T>>`, and also flags `Result.Ok(existingResult)` or `Result.Fail(existingResult)` when the value is already a `Result<T>`.

## Why it matters
Double-wrapped results are awkward to handle and usually mean the pipeline used `Map` where `Bind` was intended.

> [!WARNING]
> A nested `Result` is almost never the domain model you actually want. It usually signals a flattened pipeline that never got flattened.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static Result<Result<int>> Bad() =>
        Result.Ok(Result.Ok(42));
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Good() =>
        Result.Ok(42);
}
```

## Code fix available
No.

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
> If the inner expression already returns `Result<T>`, switch to `Bind` or return the inner result directly.

