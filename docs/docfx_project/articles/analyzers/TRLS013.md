# TRLS013 — *(removed in v2)*

This analyzer (`TernaryValueOrDefaultAnalyzer`) and its code fix (`UseFunctionalValueOrDefaultCodeFixProvider`) were deleted in V2.

## Why it was removed

The pattern the rule targeted — `result.IsSuccess ? result.Value : fallback` — cannot compile in V2 because `Result<T>.Value` was removed (see [TRLS003](TRLS003.md)). There is no surface form left for the analyzer to flag.

## Recommended replacement

Use `GetValueOrDefault` for value extraction, or `Match` when you also need to react to the failure:

```csharp
using Trellis;

static int Pick(Result<int> result, int fallback) =>
    result.GetValueOrDefault(fallback);

static string Render(Result<int> result) =>
    result.Match(
        onSuccess: v => v.ToString(),
        onFailure: e => e.Detail);
```
