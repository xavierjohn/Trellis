# TRLS007 — *(removed in v2)*

This analyzer (`TryCreateValueAccessAnalyzer`) and its code fix (`UseCreateInsteadOfTryCreateValueCodeFixProvider`) were deleted in V2.

## Why it was removed

The pattern `SomeValueObject.TryCreate(...).Value` cannot compile in V2: `TryCreate` returns `Result<TValueObject>` and `Result<T>.Value` no longer exists (see [TRLS003](TRLS003.md)). With the offending pattern unrepresentable, the analyzer has nothing to detect.

## Recommended replacement

If you have a known-good input and want a throwing constructor, call `Create(...)` directly:

```csharp
using Trellis;

var email = EmailAddress.Create("alice@example.com"); // throws InvalidOperationException with the validation detail on bad input
```

If the input might be invalid, handle the `Result` explicitly:

```csharp
if (!EmailAddress.TryCreate(rawInput).TryGetValue(out var email))
    return Result.Fail<Order>(new Error.UnprocessableContent("email"));
```

Or compose with `Bind`/`Map` so the failure stays on the failure track.
