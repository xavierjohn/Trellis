# RequiredEnum

Regular C# enums are fast and familiar, but they are weak at domain modeling:

- invalid casts are possible
- behavior has to live somewhere else
- wire names and display names get bolted on afterward

`RequiredEnum<TSelf>` solves that by giving you a **finite symbolic set with behavior**.

## Start with a working example

```csharp
using Trellis;

namespace RequiredEnumExamples;

public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new(canShip: false, isTerminal: false);

    [EnumValue("awaiting-payment")]
    public static readonly OrderStatus AwaitingPayment = new(canShip: false, isTerminal: false);

    public static readonly OrderStatus Paid = new(canShip: true, isTerminal: false);
    public static readonly OrderStatus Shipped = new(canShip: false, isTerminal: false);
    public static readonly OrderStatus Cancelled = new(canShip: false, isTerminal: true);

    private OrderStatus(bool canShip, bool isTerminal)
    {
        CanShip = canShip;
        IsTerminal = isTerminal;
    }

    public bool CanShip { get; }
    public bool IsTerminal { get; }
}
```

Usage stays simple:

```csharp
using RequiredEnumExamples;

var paid = OrderStatus.Paid;
var parsed = OrderStatus.TryCreate("awaiting-payment");
var shipped = OrderStatus.TryFromName("Shipped");
var all = OrderStatus.GetAll();

bool readyToShip = paid.CanShip;
bool isOpenState = paid.Is(OrderStatus.Draft, OrderStatus.AwaitingPayment, OrderStatus.Paid);
bool isNotTerminal = paid.IsNot(OrderStatus.Cancelled);
```

## Why teams reach for `RequiredEnum<TSelf>`

It helps when your “enum” has to do more than hold an integer:

- state transitions
- policy flags
- wire-name overrides
- JSON/model binding
- richer equality semantics

## What the API actually gives you

`RequiredEnum<TSelf>` exposes these important members:

| Member | Purpose |
| --- | --- |
| `Value` | symbolic identity |
| `Ordinal` | declaration-order metadata only |
| `GetAll()` | returns every declared member |
| `TryFromName(...)` | case-insensitive symbolic lookup |
| `Is(params TSelf[])` | membership check |
| `IsNot(params TSelf[])` | negated membership check |

The source generator also adds:

- `TryCreate(string value)`
- `TryCreate(string? value, string? fieldName = null)`
- `Create(string value)`
- parsing and JSON support

> [!NOTE]
> Generated `TryCreate(...)` delegates to `TryFromName(...)`. There is **no separate `TryFromValue(...)` API path** in the current Trellis implementation.

## `Value` and `Ordinal` mean different things

This distinction is easy to miss:

- **`Value`** is the semantic identity
- **`Ordinal`** is just declaration-order metadata

`Ordinal` is useful for diagnostics or display ordering, but it should not be treated as a stable wire contract.

## Default names vs `[EnumValue]`

By default, the symbolic value is the field name:

```csharp
using RequiredEnumExamples;

bool usesFieldName = OrderStatus.Paid.Value == "Paid";
```

Use `[EnumValue(...)]` only when the external symbolic name must differ:

```csharp
using RequiredEnumExamples;

bool usesOverride = OrderStatus.AwaitingPayment.Value == "awaiting-payment";
```

That keeps one source of truth most of the time.

## Adding behavior is the real win

This is where `RequiredEnum<TSelf>` beats a raw enum.

```csharp
public partial class InvoiceStatus : RequiredEnum<InvoiceStatus>
{
    public static readonly InvoiceStatus Draft = new(canPost: false);
    public static readonly InvoiceStatus Approved = new(canPost: true);
    public static readonly InvoiceStatus Posted = new(canPost: false);

    private InvoiceStatus(bool canPost) => CanPost = canPost;

    public bool CanPost { get; }
}
```

Now the behavior travels with the symbolic value instead of being scattered through `switch` statements.

## State-machine style modeling

`RequiredEnum<TSelf>` works especially well for workflows:

```csharp
using Trellis;

namespace WorkflowExample;

public partial class ShipmentStatus : RequiredEnum<ShipmentStatus>
{
    public static readonly ShipmentStatus Draft = new();
    public static readonly ShipmentStatus Ready = new();
    public static readonly ShipmentStatus Sent = new();
    public static readonly ShipmentStatus Delivered = new();

    private ShipmentStatus() { }

    public bool CanTransitionTo(ShipmentStatus next) =>
        this switch
        {
            _ when this == Draft => next.Is(Ready),
            _ when this == Ready => next.Is(Sent),
            _ when this == Sent => next.Is(Delivered),
            _ => false
        };
}
```

## Serialization and web input

When you declare a concrete type as `partial`, the generator provides the plumbing for:

- JSON conversion
- ASP.NET Core model binding
- parsing helpers

That means `"Paid"` or `"awaiting-payment"` can flow naturally through APIs without hand-written converters.

## EF Core persistence

Persist the symbolic `Value`, not `Ordinal`.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RequiredEnumEfExample;

public sealed class Order
{
    public int Id { get; set; }
    public RequiredEnumExamples.OrderStatus Status { get; set; } = null!;
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(order => order.Status)
            .HasConversion(
                status => status.Value,
                value => RequiredEnumExamples.OrderStatus.Create(value))
            .IsRequired();
    }
}
```

> [!WARNING]
> Persisting `Ordinal` turns declaration order into a storage contract. That is usually a mistake.

## Best practices

1. Declare members as `public static readonly`
2. Keep constructors private
3. Prefer field names as the default symbolic value
4. Use `[EnumValue(...)]` only for true wire-name overrides
5. Put state behavior on the type itself
6. Use `Is(...)` and `IsNot(...)` for readable membership checks

## Summary

Use `RequiredEnum<TSelf>` when you need a finite set of domain values that:

- must be valid
- may carry behavior
- need stable symbolic names
- should work cleanly with JSON and model binding

If you just need an integer-backed constant, a regular enum is fine. If the values are part of your domain language, `RequiredEnum<TSelf>` is usually the better fit.

## See also

- [Primitive Value Objects](primitives.md)
- [Clean Architecture](clean-architecture.md)
- [Trellis primitives API reference](../api_reference/trellis-api-primitives.md)
