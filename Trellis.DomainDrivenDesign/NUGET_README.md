# Trellis.DomainDrivenDesign

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.DomainDrivenDesign.svg)](https://www.nuget.org/packages/Trellis.DomainDrivenDesign)

DDD building blocks for aggregates, entities, value objects, specifications, and domain events.

## Installation
```bash
dotnet add package Trellis.DomainDrivenDesign
```

## Quick Example
```csharp
using System;
using Trellis;

public sealed record OrderId(Guid Value);
public sealed record OrderPlaced(OrderId OrderId, DateTime OccurredAt) : IDomainEvent;

public sealed class Order : Aggregate<OrderId>
{
    private Order(OrderId id) : base(id) { }

    public static Result<Order> Create()
    {
        var order = new Order(new OrderId(Guid.NewGuid()));
        order.DomainEvents.Add(new OrderPlaced(order.Id, DateTime.UtcNow));
        return Result.Success(order);
    }
}
```

## Key Features
- Base types for aggregates, entities, value objects, and specifications.
- Built-in domain event tracking through `Aggregate<TId>`.
- Equality and modeling primitives that fit clean architecture codebases.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/clean-architecture.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.

