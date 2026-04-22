# TRLS018 — Unsafe Result&lt;T&gt; deconstruction

**Category:** Usage — Result safety
**Severity:** Warning
**Enabled by default:** Yes

## What it flags

`Result<T>` deconstruction that reads the value slot without gating on the success/error component. The value slot is only meaningful when the Result is successful; reading it on a failed Result yields `default(T)` silently.

```csharp
// ❌ TRLS018
var (value, _) = GetOrderAsync(id).Result;   // value is default if GetOrderAsync failed
Console.WriteLine(value.Number);

// ✅ No warning
var result = await GetOrderAsync(id);
if (result.IsSuccess)
{
    var (value, _) = result;
    Console.WriteLine(value.Number);
}

// ✅ Preferred — use Match / TryGetValue / Unwrap
result.Match(
    onSuccess: order => Console.WriteLine(order.Number),
    onFailure: error => Console.WriteLine(error.Code));
```

## Why

`Result<T>` is a discriminated union. The deconstruction pattern exposes both slots as tuple elements for ergonomics, but consuming the value slot without a success check defeats the whole point of the Result abstraction — the failure is silently swallowed into `default(T)` and subsequent code operates on a value that may never have existed.

## How to fix

Use one of:

- `if (result.IsSuccess)` gate before deconstructing.
- `result.TryGetValue(out var value)` — returns false on failure.
- `result.Match(onSuccess, onFailure)` — exhaustive branching.
- `result.Unwrap()` — throws on failure (test helpers only).

## Suppressing

At a sanctioned site (e.g. a test that has already asserted success):

```csharp
[SuppressMessage("Trellis", TrellisDiagnosticIds.UnsafeResultDeconstruction,
    Justification = "Success already asserted above.")]
```

## See also

- TRLS003 — Unsafe `Maybe.Value` access
- TRLS013 — Unsafe `Maybe.Value` in LINQ
