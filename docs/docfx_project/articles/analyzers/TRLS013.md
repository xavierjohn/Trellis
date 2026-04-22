# TRLS013 — Unsafe access to Maybe.Value in LINQ expression

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `.Value` access on a `Maybe<T>` LINQ lambda parameter inside projections, grouping, ordering, and dictionary-building operations unless an earlier `Where(...)` guard proves the value is safe.

The Result-side equivalent was removed in V2 along with `Result<T>.Value` (see ADR-002 §3.1). This rule now applies only to `Maybe<T>`.

## Why it matters
Collection pipelines make it easy to forget that some items may be `Maybe<T>.None`. One unsafe `.Value` access can throw for the whole query.

> [!WARNING]
> The analyzer understands guard chains like `.Where(x => x.HasValue)`. Without that filter, `.Value` is treated as unsafe.

## Bad example
```csharp
using System.Collections.Generic;
using System.Linq;
using Trellis;

static class Example
{
    public static List<int> Bad(IEnumerable<Maybe<int>> values) =>
        values.Select(maybe => maybe.Value).ToList();
}
```

## Good example
```csharp
using System.Collections.Generic;
using System.Linq;
using Trellis;

static class Example
{
    public static List<int> Good(IEnumerable<Maybe<int>> values) =>
        values
            .Where(maybe => maybe.HasValue)
            .Select(maybe => maybe.Value)
            .ToList();
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS013.severity = none
```

```csharp
#pragma warning disable TRLS013
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS013
```

> [!TIP]
> Filter first, then project. If you need both present and absent paths, use `Match` inside the projection instead of `.Value`.
