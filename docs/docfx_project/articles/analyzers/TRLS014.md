# TRLS014 — Use async method variant for async lambda

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags synchronous Trellis methods such as `Map`, `Bind`, `Tap`, `Ensure`, and `TapOnFailure` when any supplied lambda or method group does async work.

## Why it matters
The sync method treats the returned `Task` or `ValueTask` as just another value. The async work does not get awaited by the Trellis pipeline.

> [!WARNING]
> This rule covers three cases: `async` lambdas, non-async lambdas whose converted return type is `Task` or `ValueTask`, and method groups returning `Task` or `ValueTask`.

## Bad example
```csharp
using System.Threading.Tasks;
using Trellis;

static class Example
{
    public static Result<Task<int>> Bad()
    {
        var result = Result.Ok("Ada");
        return result.Map(LookupLengthAsync);
    }

    static Task<int> LookupLengthAsync(string value) =>
        Task.FromResult(value.Length);
}
```

## Good example
```csharp
using System.Threading.Tasks;
using Trellis;

static class Example
{
    public static Task<Result<int>> Good()
    {
        var result = Result.Ok("Ada");
        return result.MapAsync(LookupLengthAsync);
    }

    static Task<int> LookupLengthAsync(string value) =>
        Task.FromResult(value.Length);
}
```

## Code fix available
Yes — renames the sync API to the matching async API, such as `MapAsync`, `BindAsync`, `TapAsync`, `EnsureAsync`, or `TapOnFailureAsync`.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS014.severity = none
```

```csharp
#pragma warning disable TRLS014
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS014
```

> [!TIP]
> If the callback does async work, move to the matching async API immediately instead of returning a `Task` from the sync overload.

