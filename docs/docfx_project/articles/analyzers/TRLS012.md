# TRLS012 — Consider using Result.Combine

- **Severity:** Info
- **Category:** Trellis

## What it detects
Flags conditional logic that manually combines two or more `Result` state checks, such as `&&` chains over `.IsSuccess` or `||` chains over `.IsFailure`.

## Why it matters
`Result.Combine` expresses intent more clearly and scales better than repeated manual branching.

> [!WARNING]
> Manual combination logic tends to duplicate error-selection code and becomes noisy as soon as you add a third or fourth result.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Bad(Result<int> first, Result<int> second)
    {
        if (first.IsSuccess && second.IsSuccess)
            return Result.Success(first.Value + second.Value);

        return first.IsFailure
            ? Result.Failure<int>(first.Error)
            : Result.Failure<int>(second.Error);
    }
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Good(Result<int> first, Result<int> second) =>
        first
            .Combine(second)
            .Map((left, right) => left + right);
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS012.severity = none
```

```csharp
#pragma warning disable TRLS012
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS012
```

> [!TIP]
> Use `Result.Combine(...)` or `.Combine(...)` chaining, then continue with `Map`, `Bind`, or `Match`.

