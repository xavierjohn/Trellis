# TRLS009 — Incorrect async Result usage

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags blocking access on `Task<Result<T>>` and `ValueTask<Result<T>>`: `.Result`, `.Wait()`, and `.GetAwaiter().GetResult()`.

## Why it matters
Blocking async Result pipelines can deadlock, hide cancellation behavior, and makes failure handling harder to reason about.

> [!WARNING]
> This rule covers both `Task` and `ValueTask`. Replacing `.Result` with `.GetAwaiter().GetResult()` does not avoid the diagnostic.

## Bad example
```csharp
using System.Threading.Tasks;
using Trellis;

static class Example
{
    public static Result<int> Bad()
    {
        var pending = GetCountAsync();
        return pending.GetAwaiter().GetResult();
    }

    static ValueTask<Result<int>> GetCountAsync() =>
        new(Result.Ok(42));
}
```

## Good example
```csharp
using System.Threading.Tasks;
using Trellis;

static class Example
{
    public static async ValueTask<Result<int>> Good() =>
        await GetCountAsync();

    static ValueTask<Result<int>> GetCountAsync() =>
        new(Result.Ok(42));
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS009.severity = none
```

```csharp
#pragma warning disable TRLS009
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS009
```

> [!TIP]
> Once a method touches `Task<Result<T>>` or `ValueTask<Result<T>>`, keep the method async and `await` the result all the way through.

