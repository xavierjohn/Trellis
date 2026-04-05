# TRLS002 — Use Bind instead of Map when lambda returns Result

- **Severity:** Info
- **Category:** Trellis

## What it detects
Flags `Map` and `MapAsync` when the transformation already returns `Result<T>`, `Task<Result<T>>`, or `ValueTask<Result<T>>`. It covers lambdas, method groups, and member-access method groups.

## Why it matters
`Map` wraps the transformation result as a value, which leads to `Result<Result<T>>` or async equivalents. `Bind` and `BindAsync` flatten the pipeline correctly.

> [!WARNING]
> This rule is broader than a simple `x => Result.Success(...)` lambda. A method group like `Map(ParseAsync)` can trigger it too if `ParseAsync` already returns a `Result`.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Bad()
    {
        var result = Result.Success("Ada");
        return result.Map(ParseLength);
    }

    static Result<int> ParseLength(string value) =>
        Result.Success(value.Length);
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Good()
    {
        var result = Result.Success("Ada");
        return result.Bind(ParseLength);
    }

    static Result<int> ParseLength(string value) =>
        Result.Success(value.Length);
}
```

## Code fix available
Yes — replaces `Map` with `Bind`, or `MapAsync` with `BindAsync`.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS002.severity = none
```

```csharp
#pragma warning disable TRLS002
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS002
```

> [!TIP]
> Use `Map` when you turn a success value into a plain value. Use `Bind` when you turn a success value into another `Result`.

