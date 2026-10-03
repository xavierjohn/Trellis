---
title: Symbolic Value Objects with RequiredEnum
package: Trellis.Core
topics: [required-enum, value-object, validation, source-generator, json, ddd]
last_verified: 2026-10-02
audience: [developer]
---
# Model symbolic values with RequiredEnum

A C# `enum` is compact, but any underlying integer can be cast into it and it cannot carry domain behavior. [RequiredEnum<TSelf>](xref:Trellis.RequiredEnum`1) models a finite set as named singleton objects with stable string values, validation, and optional behavior.

Use it for values such as order status, priority, or fulfillment method when the set is closed and each member may answer domain questions.

```bash
dotnet add package Trellis.Core
```

## Define members and behavior together

Replace `Program.cs` in a console project with this runnable example:

```csharp
using Trellis;

var result = OrderStatus.TryCreate("awaiting-payment", "status");

if (!result.TryGetValue(out var status, out var error))
{
    Console.Error.WriteLine(error);
    return;
}

Console.WriteLine($"{status.Value}: canShip={status.CanShip}");
Console.WriteLine(string.Join(", ", OrderStatus.GetAll().Select(value => value.Value)));

public sealed partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new(canShip: false);

    [EnumValue("awaiting-payment")]
    public static readonly OrderStatus AwaitingPayment = new(canShip: false);

    public static readonly OrderStatus Paid = new(canShip: true);
    public static readonly OrderStatus Shipped = new(canShip: false);

    private OrderStatus(bool canShip)
    {
        CanShip = canShip;
    }

    public bool CanShip { get; }
}
```

Each `public static readonly` field is one valid member. By default, its external `Value` is the field name. [EnumValueAttribute](xref:Trellis.EnumValueAttribute) lets the code name and wire name differ without changing either accidentally.

The class must be `partial`. The source generator adds `Create`, parsing, and JSON conversion; the base supplies lookup, membership, equality, `Value`, `Ordinal`, and `GetAll()`.

## Parse at the boundary

Use `TryCreate(value, fieldName)` for request, file, or message input:

```csharp
Result<OrderStatus> status = OrderStatus.TryCreate(input, "status");
```

Lookup is case-insensitive and rejects names that are not declared members. Passing the field name keeps validation output aligned with the transport shape.

Use `Create(value)` only for trusted constants and tests. It throws when the value is invalid.

## Keep identity stable

`Value` is the serialized and persisted identity. `Ordinal` reflects declaration order and is useful for ordering, but it is not a stable wire or storage contract.

Prefer leaving the field name and `Value` identical. Use `[EnumValue("...")]` when an established external contract requires a different spelling, such as a kebab-case JSON value.

Equality is based on `Value` and is case-insensitive. Use `Is(...)` and `IsNot(...)` when a rule accepts several members:

```csharp
bool canStillChange = status.Is(
    OrderStatus.Draft,
    OrderStatus.AwaitingPayment);
```

## JSON and persistence

The generator applies [RequiredEnumJsonConverter<T>](xref:Trellis.RequiredEnumJsonConverter`1), so JSON is a string such as `"awaiting-payment"`, not an object containing `Value` and `Ordinal`. Deserialization routes through `TryCreate` and rejects undeclared values.

For ASP.NET Core request validation, continue with the [ASP.NET Core integration guide](integration-aspnet.md). For string-backed EF Core storage and conventions, continue with the [Entity Framework Core guide](integration-ef.md).

## Choose another shape when needed

- Use a [custom scalar value object](custom-primitives.md) when valid strings are open-ended rather than a finite set.
- Use [ValueObject](xref:Trellis.ValueObject) when identity is composed from several fields.
- Use a built-in from [Trellis.Primitives](built-in-primitives.md) when the domain meaning already exists.

Generated .NET API:

- [RequiredEnum<TSelf>](xref:Trellis.RequiredEnum`1)
- [EnumValueAttribute](xref:Trellis.EnumValueAttribute)
- [RequiredEnumJsonConverter<T>](xref:Trellis.RequiredEnumJsonConverter`1)
