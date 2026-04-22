# TRLS025 — *(removed in v2)*

This analyzer (`UnsafeResultValuePropertyPatternAnalyzer`) was deleted in V2.

## Why it was removed

The patterns the rule flagged — `result is { Value: ... }` or `result switch { { Value: var v } => ... }` — cannot compile in V2 because `Result<T>.Value` was removed (see [TRLS003](TRLS003.md)). Property patterns over a non-existent member are a compile error, so there is nothing left for the analyzer to catch at warning level.

## Recommended replacement

Discriminate on the success state first, or use `Match` / `TryGetValue`:

```csharp
using Trellis;

static string Describe(Result<int> result) =>
    result.Match(
        onSuccess: v => $"value: {v}",
        onFailure: e => $"error: {e.Detail}");

// or
static string DescribeImperative(Result<int> result)
{
    if (result.TryGetValue(out var v))
        return $"value: {v}";
    return $"error: {result.Error!.Detail}";
}
```
