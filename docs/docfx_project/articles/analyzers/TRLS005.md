# TRLS005 — Consider using MatchError for error type discrimination

- **Severity:** Info
- **Category:** Trellis

## What it detects
Flags manual error-type discrimination on Trellis errors, such as `switch` statements, `switch` expressions, and `is` checks over `Error` or a derived error type.

## Why it matters
Manual pattern matching spreads error handling logic across branches. `MatchError` keeps the success case and the error-specific cases in one focused Trellis API.

> [!WARNING]
> If you branch on concrete error types in multiple places, your code becomes harder to extend when a new error type appears.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static string Bad(Result<int> result)
    {
        if (result.IsSuccess)
            return $"Value: {result.Value}";

        return result.Error switch
        {
            ValidationError validation => $"Validation: {validation.Detail}",
            NotFoundError notFound => $"Missing: {notFound.Detail}",
            _ => $"Error: {result.Error.Detail}"
        };
    }
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static string Good(Result<int> result) =>
        result.MatchError(
            onSuccess: value => $"Value: {value}",
            onValidation: validation => $"Validation: {validation.Detail}",
            onNotFound: notFound => $"Missing: {notFound.Detail}",
            onError: error => $"Error: {error.Detail}");
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS005.severity = none
```

```csharp
#pragma warning disable TRLS005
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS005
```

> [!TIP]
> Use `MatchError` when you want strongly typed handlers for `ValidationError`, `NotFoundError`, and similar types plus a final catch-all.

