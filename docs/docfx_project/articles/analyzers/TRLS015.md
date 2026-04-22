# TRLS015 — Use SaveChangesResultAsync instead of SaveChangesAsync

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags direct `DbContext.SaveChanges()` and `DbContext.SaveChangesAsync()` calls when `Trellis.EntityFrameworkCore` is referenced.

## Why it matters
Raw EF Core save calls throw exceptions on database failures. Trellis save helpers keep persistence inside the `Result` pipeline.

> [!WARNING]
> The analyzer catches both sync and async saves. The code fix chooses `SaveChangesResultAsync` when the count is used and `SaveChangesResultUnitAsync` when it is discarded.

## Bad example
```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

static class Example
{
    public static async Task SaveAsync(AppDbContext db, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}

sealed class AppDbContext : DbContext
{
}
```

## Good example
```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore;

static class Example
{
    public static async Task SaveAsync(AppDbContext db, CancellationToken ct)
    {
        await db.SaveChangesResultUnitAsync(ct);
    }
}

sealed class AppDbContext : DbContext
{
}
```

## Code fix available
Yes — replaces `SaveChanges` or `SaveChangesAsync` with `SaveChangesResultAsync` or `SaveChangesResultUnitAsync`, and can add `await`, `async`, and missing `using` directives.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS015.severity = none
```

```csharp
#pragma warning disable TRLS015
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS015
```

> [!TIP]
> Add `using Trellis.EntityFrameworkCore;` and keep saves inside your Trellis pipeline so persistence errors become `Result` values.

