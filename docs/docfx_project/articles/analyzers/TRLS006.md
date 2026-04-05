# TRLS006 — Unsafe access to Maybe.Value

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `maybe.Value` when the analyzer cannot prove the `Maybe<T>` definitely contains a value.

## Why it matters
`Maybe.Value` throws when the instance is empty. That turns optional data into an exception path.

> [!WARNING]
> This commonly shows up in DTO mapping, logging, and formatting code where an empty `Maybe<T>` is easy to overlook.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static string Bad(Maybe<string> nickname) =>
        nickname.Value.ToUpperInvariant();
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static string Good(Maybe<string> nickname) =>
        nickname.GetValueOrDefault("unknown").ToUpperInvariant();
}
```

## Code fix available
Yes — wraps the current usage in an `if (maybe.HasValue)` guard.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS006.severity = none
```

```csharp
#pragma warning disable TRLS006
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS006
```

> [!TIP]
> Prefer `GetValueOrDefault`, `TryGetValue`, or a `HasValue` guard when you only need a fallback or a conditional branch.

