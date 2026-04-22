# TRLS003 — *(removed in v2)*

This analyzer (`UnsafeValueAccessAnalyzer` for `Result<T>.Value`) was deleted in V2.

## Why it was removed

In V1, `Result<T>.Value` was a property that threw `InvalidOperationException` on a failure result, and TRLS003 flagged unguarded accesses. In V2 the property was removed entirely (see [ADR-002](../../../adr/ADR-002-v2-redesign-plan.md) §3.1 and the `ga-03` commit). `result.Value` no longer compiles, so the analyzer has nothing left to detect.

## Recommended replacement

Use `TryGetValue`, `Match`, or deconstruction to extract the success value safely:

```csharp
using Trellis;

static int Render(Result<int> result)
{
    if (result.TryGetValue(out var value))
        return value;
    return 0;
}

// or
static int RenderViaMatch(Result<int> result) =>
    result.Match(
        onSuccess: value => value,
        onFailure: _ => 0);
```

The TRLS006 (`Maybe<T>.Value`) rule is unchanged — `Maybe<T>` still exposes `Value` and still requires a `HasValue` guard.
