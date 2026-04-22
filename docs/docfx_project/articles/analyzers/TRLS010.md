# TRLS010 — Don't throw exceptions in Result chains

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `throw` statements and `throw` expressions inside lambdas passed to Trellis chain methods such as `Bind`, `Map`, `Tap`, `Ensure`, and their async and failure-track variants.

## Why it matters
Throwing inside a Result pipeline bypasses the railway and turns a modeled failure back into an exception.

> [!WARNING]
> This rule also applies to failure-track APIs like `TapOnFailure`, `MapOnFailure`, `RecoverOnFailure`, and `DebugOnFailure`.

## Bad example
```csharp
using System;
using Trellis;

static class Example
{
    public static Result<string> Bad(string value) =>
        Result.Ok(value).Map(text =>
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Value is required.");

            return text.Trim();
        });
}
```

## Good example
```csharp
using System;
using Trellis;

static class Example
{
    public static Result<string> Good(string value) =>
        Result.Ok(value).Bind(text =>
        {
            if (string.IsNullOrWhiteSpace(text))
                return Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(value)), "validation.error") { Detail = "Value is required." })));

            return Result.Ok(text.Trim());
        });
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS010.severity = none
```

```csharp
#pragma warning disable TRLS010
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS010
```

> [!TIP]
> Return `Result.Fail<T>(...)` when the callback discovers a business or validation problem. Reserve exceptions for truly exceptional situations.

