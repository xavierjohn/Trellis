# TRLS001 — Result return value is not handled

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `Result<T>` values that are produced and immediately discarded. That includes bare expression statements like `CreateUser(...)` and discarded `await` expressions whose awaited type is `Task<Result<T>>` or `ValueTask<Result<T>>`.

## Why it matters
A `Result<T>` exists to carry success or failure. If you drop it on the floor, you also drop the error information and break the pipeline.

> [!WARNING]
> Ignoring a `Result<T>` is easy to do in async code. `await SomeCallAsync();` still counts as discarded if the awaited value is a `Result<T>`.

## Bad example
```csharp
using System.Threading.Tasks;
using Trellis;

static class Example
{
    public static async Task SaveAsync(string email)
    {
        await CreateCustomerAsync(email);
    }

    static ValueTask<Result<int>> CreateCustomerAsync(string email) =>
        new(Result.Success(email.Length));
}
```

## Good example
```csharp
using System.Threading.Tasks;
using Trellis;

static class Example
{
    public static async Task<Result<int>> SaveAsync(string email)
    {
        var result = await CreateCustomerAsync(email);
        return result;
    }

    static ValueTask<Result<int>> CreateCustomerAsync(string email) =>
        new(Result.Success(email.Length));
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS001.severity = none
```

```csharp
#pragma warning disable TRLS001
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS001
```

> [!TIP]
> If you are at a boundary, return the `Result`, convert it with `Match` or `MatchError`, or explicitly assign it so the next step can handle success and failure.

