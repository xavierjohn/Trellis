# TRLS007 — Use Create instead of TryCreate().Value

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags direct `.Value` access on a static `TryCreate(...)` call when the target type also exposes a static `Create(...)` method.

## Why it matters
`TryCreate(...).Value` throws a generic `InvalidOperationException` if validation fails. `Create(...)` expresses that you expect success and preserves the validation detail in the exception message.

> [!WARNING]
> If you truly want non-throwing behavior, keep the `Result` from `TryCreate(...)` and handle it. Do not force it with `.Value`.

## Bad example
```csharp
using Trellis.Primitives;

static class Example
{
    public static EmailAddress Bad(string input) =>
        EmailAddress.TryCreate(input).Value;
}
```

## Good example
```csharp
using Trellis.Primitives;

static class Example
{
    public static EmailAddress Good(string input) =>
        EmailAddress.Create(input);
}
```

## Code fix available
Yes — replaces `TryCreate(...).Value` with `Create(...)` when the replacement binds correctly.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS007.severity = none
```

```csharp
#pragma warning disable TRLS007
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS007
```

> [!TIP]
> Use `Create(...)` when invalid input is a programmer error. Use `TryCreate(...)` when invalid input is part of normal control flow.

