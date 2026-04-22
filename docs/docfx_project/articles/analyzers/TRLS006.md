# TRLS006 — Use specific error type instead of base Error class

- **Severity:** Info
- **Category:** Trellis

## What it detects
Flags direct construction of the base `Error` type, including implicit `new(...)`, when the created type is exactly Trellis `Error`.

## Why it matters
Specific error types such as validation, not found, and conflict are easier to match, log, and translate at boundaries.

> [!WARNING]
> A plain `Error` works, but it throws away domain meaning that Trellis error factories already encode for you.

## Bad example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Bad() =>
        Result.Fail<int>(new Error("Unknown customer", "customer.not_found"));
}
```

## Good example
```csharp
using Trellis;

static class Example
{
    public static Result<int> Good() =>
        Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource")) { Detail = "Unknown customer" });
}
```

## Code fix available
No.

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
> Reach for `new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = ... }`, `new Error.NotFound(new ResourceRef("Resource")) { Detail = ... }`, `new Error.Conflict(null, "conflict") { Detail = ... }`, and similar factories before constructing `Error` yourself.

