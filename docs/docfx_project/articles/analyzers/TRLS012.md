# TRLS012 — Don't compare Result or Maybe to null

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `== null`, `!= null`, `is null`, and `is not null` when the checked value is a Trellis `Result<T>` or `Maybe<T>`.

## Why it matters
These are structs. Presence and success are represented by state members such as `HasValue`, `HasNoValue`, `IsSuccess`, and `IsFailure`, not by nullability.

> [!WARNING]
> A null check can look harmless, but it asks the wrong question and hides the actual success or optionality rule.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static string Bad(Maybe<string> nickname) =>
        nickname == null ? "Anonymous" : nickname.Value;
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static string Good(Maybe<string> nickname) =>
        nickname.HasNoValue ? "Anonymous" : nickname.Value;
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
> Check the state you actually care about: `HasValue` for `Maybe<T>`, `IsSuccess` or `IsFailure` for `Result<T>`.

