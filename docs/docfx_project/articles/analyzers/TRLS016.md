# TRLS016 — Error message should not be empty

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags empty or whitespace-only messages passed to Trellis error factory methods such as `Error.Validation(...)`, `Error.NotFound(...)`, `Error.Conflict(...)`, `Error.Unauthorized(...)`, `Error.Forbidden(...)`, and `Error.Unexpected(...)`.

## Why it matters
Empty error messages make logs, diagnostics, and HTTP responses much harder to understand.

> [!WARNING]
> `string.Empty`, `""`, whitespace-only literals, and interpolated strings that contain only whitespace all trigger this rule.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Bad(string quantity) =>
        Result.Fail<int>(Error.Validation("", nameof(quantity)));
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Good(string quantity) =>
        Result.Fail<int>(Error.Validation("Quantity must be a whole number.", nameof(quantity)));
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS016.severity = none
```

```csharp
#pragma warning disable TRLS016
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS016
```

> [!TIP]
> Write the message for the next person who will debug the failure. A short, specific sentence is enough.

