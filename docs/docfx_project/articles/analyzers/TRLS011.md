# TRLS011 — Maybe is double-wrapped

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags declared `Maybe<Maybe<T>>` in variables, properties, method return types, and parameters.

## Why it matters
Double optionality is usually a modeling smell. Consumers now have to unwrap presence twice before they can use the value.

> [!WARNING]
> Nested `Maybe` often appears after using `Map` with a transformation that already returns `Maybe<T>`.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static Maybe<Maybe<int>> Bad() =>
        Maybe.From(Maybe.From(42));
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static Maybe<int> Good() =>
        Maybe.From(42);
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS011.severity = none
```

```csharp
#pragma warning disable TRLS011
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS011
```

> [!TIP]
> If the inner computation can fail with details, consider `Result<T>` instead. Otherwise return a single `Maybe<T>`.

