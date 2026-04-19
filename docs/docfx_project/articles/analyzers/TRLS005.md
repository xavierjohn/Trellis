# TRLS005 — *(removed in v2)*

This analyzer (`UseMatchErrorAnalyzer`) was deleted in V2.

## Why it was removed

In V1 the rule encouraged use of the `MatchError(...)` extension to discriminate error types. With the V2 closed-ADT `Error` (see [ADR-001](../../../adr/ADR-001-result-api-surface.md)), a `switch` expression over an `Error` reference is exhaustive at the language level — the C# compiler verifies that every nested case is handled, and adding a new case lights up every site that doesn't handle it. Manual `switch` patterns are now the recommended idiom.

## Recommended replacement

```csharp
using Trellis;

static string Render(Result<int> result) =>
    result.Match(
        onSuccess: value => $"Value: {value}",
        onFailure: error => error switch
        {
            Error.UnprocessableContent uc => $"Validation: {uc.GetDisplayMessage()}",
            Error.NotFound nf             => $"Missing: {nf.Detail}",
            _                              => $"Error: {error.Detail}"
        });
```

The `MatchError` / `SwitchError` extensions and the `FlattenValidationErrors` helper were removed alongside this analyzer; use `switch` patterns and `Combine` instead.
