# TRLS016 — HasIndex references a Maybe<T> property

- **Severity:** Warning
- **Category:** Trellis

## What it detects
Flags `EntityTypeBuilder.HasIndex(...)` lambda expressions that reference a `Maybe<T>` property when Trellis.EntityFrameworkCore's Maybe convention is active.

## Why it matters
EF Core indexes the generated storage member, not the `Maybe<T>` CLR property. A normal `HasIndex` call can silently fail to create the index you thought you configured.

> [!WARNING]
> The diagnostic tells you both the `Maybe<T>` property name and the generated storage-member fallback name so you can see what EF Core actually maps.

## Bad example
```csharp
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trellis;

sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasIndex(order => order.SubmittedAt);
    }
}

sealed class Order
{
    public string Status { get; set; } = string.Empty;
    public Maybe<DateTime> SubmittedAt { get; set; }
}
```

## Good example
```csharp
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trellis;
using Trellis.EntityFrameworkCore;

sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasTrellisIndex(order => new { order.Status, order.SubmittedAt });
    }
}

sealed class Order
{
    public string Status { get; set; } = string.Empty;
    public Maybe<DateTime> SubmittedAt { get; set; }
}
```

## Code fix available
No.

## Configuration
Use standard Roslyn configuration if you need to suppress this rule in a specific scope.

```ini
dotnet_diagnostic.TRLS016.severity = none
```

```csharp
#pragma warning disable TRLS016
// Intentional: documented exception or test-only pattern.
#pragma warning restore TRLS016
```

> [!TIP]
> Prefer `HasTrellisIndex(...)` for mixed regular and `Maybe<T>` properties. Use string-based `HasIndex(...)` only when you need the storage member directly.

