# TRLS018 — Unsafe access to Value in LINQ expression

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `.Value` access on a LINQ lambda parameter inside projections, grouping, ordering, and dictionary-building operations unless an earlier `Where(...)` guard proves the value is safe.

## Why it matters
Collection pipelines make it easy to forget that some items may be failures or empty values. One unsafe access can throw for the whole query.

> [!WARNING]
> The analyzer understands guard chains like `.Where(x => x.IsSuccess)` and `.Where(x => x.HasValue)`. Without that filter, `.Value` is treated as unsafe.

## Bad example
```csharp
using System.Collections.Generic;
using System.Linq;
using Trellis;

static class Example
{
    public static List<int> Bad(IEnumerable<Result<int>> values) =>
        values.Select(result => result.Value).ToList();
}
```

## Good example
```csharp
using System.Collections.Generic;
using System.Linq;
using Trellis;

static class Example
{
    public static List<int> Good(IEnumerable<Result<int>> values) =>
        values
            .Where(result => result.IsSuccess)
            .Select(result => result.Value)
            .ToList();
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS018.severity = none
```

```csharp
#pragma warning disable TRLS018
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS018
```

> [!TIP]
> Filter first, then project. If you need both success and failure paths, use `Match` inside the projection instead of `.Value`.

