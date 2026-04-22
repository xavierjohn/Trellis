# TRLS004 — *(removed in v2)*

This analyzer (`UnsafeValueAccessAnalyzer` for `Result<T>.Error`) was deleted in V2.

## Why it was removed

In V1, `Result<T>.Error` threw `InvalidOperationException` on a success result. In V2 the property is `Error?` — it returns `null` on success and the failure value (or the `Error.Unexpected("default_initialized")` sentinel for `default(Result<T>)`) otherwise. Reading `.Error` never throws, so the original "unsafe access" framing no longer applies. Genuine misuse (forcing the value with `!` or dereferencing without a null check) is caught natively by C# nullable-reference-type analysis, which is strictly more precise than the deleted analyzer.

## Recommended pattern

Use ordinary nullable patterns. NRT will warn if you dereference without a guard:

```csharp
using Trellis;

static string Describe(Result<int> result)
{
    if (result.Error is { } error)
        return error switch
        {
            Error.NotFound nf            => $"Missing: {nf.Detail}",
            Error.UnprocessableContent u => $"Invalid: {u.Detail}",
            _                            => $"Error: {error.Detail}"
        };

    return result.TryGetValue(out var value) ? value.ToString() : "?";
}
```

`TryGetError` and the `[MemberNotNullWhen(true, nameof(Error))] IsFailure` attribute remain available for flow-typed access.
