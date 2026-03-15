# Trellis.Stateless — State Machine Integration

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Stateless.svg)](https://www.nuget.org/packages/Trellis.Stateless)

Wraps the [Stateless](https://github.com/dotnet-state-machine/stateless) library's `Fire()` method to return `Result<TState>` instead of throwing on invalid transitions.

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [How It Works](#how-it-works)
- [Related Packages](#related-packages)
- [License](#license)

## Installation

```bash
dotnet add package Trellis.Stateless
```

## Quick Start

```csharp
using Stateless;
using Trellis;
using Trellis.Stateless;

var machine = new StateMachine<OrderState, OrderTrigger>(OrderState.New);
machine.Configure(OrderState.New)
    .Permit(OrderTrigger.Submit, OrderState.Submitted);

// Returns Result<OrderState> — no exceptions on invalid transitions
Result<OrderState> result = machine.FireResult(OrderTrigger.Submit);
// result.IsSuccess == true, result.Value == OrderState.Submitted

Result<OrderState> invalid = machine.FireResult(OrderTrigger.Cancel);
// invalid.IsFailure == true, invalid.Error is DomainError
```

## LazyStateMachine

Aggregates with state machines face a materialization problem with ORMs like EF Core: the parameterless constructor runs before properties are populated, so a `stateAccessor` lambda like `() => Status` reads a default or uninitialized value — reference-type states throw, while enum states silently start the machine in the wrong state. `LazyStateMachine<TState, TTrigger>` defers machine construction until first use:

```csharp
public class Order : Aggregate<OrderId>
{
    private readonly LazyStateMachine<OrderStatus, string> _machine;

    public OrderStatus Status { get; private set; }

    public Order()
    {
        _machine = new LazyStateMachine<OrderStatus, string>(
            () => Status,
            s => Status = s,
            ConfigureStateMachine);
    }

    public Result<Order> Submit() =>
        _machine.FireResult("submit")
            .Map(_ => this);

    private static void ConfigureStateMachine(StateMachine<OrderStatus, string> machine)
    {
        machine.Configure(OrderStatus.Draft)
            .Permit("submit", OrderStatus.Submitted);
    }
}
```

- **Constructor-safe** — `stateAccessor`/`stateMutator` are not invoked until first `FireResult` or `Machine` access
- **Configure once** — the configuration callback runs exactly once on first access
- **Direct access** — use `.Machine` to access the underlying `StateMachine<TState, TTrigger>` for `CanFire()` checks

## How It Works

- Uses Stateless's `CanFire()` to check before firing — **no try/catch internally**
- Returns `Result<TState>` with the new state on success
- Returns a `DomainError` on invalid transitions
- Inherits Stateless thread-safety constraints — do not call `FireResult(...)` concurrently on the same machine instance without external synchronization

## Related Packages

- [Trellis.Results](https://www.nuget.org/packages/Trellis.Results) — Core `Result<T>` type
- [Stateless](https://www.nuget.org/packages/Stateless) — State machine library (required dependency)

## License

MIT — see [LICENSE](../LICENSE) for details.
