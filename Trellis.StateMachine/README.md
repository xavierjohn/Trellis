# Trellis.StateMachine

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.StateMachine.svg)](https://www.nuget.org/packages/Trellis.StateMachine)

A thin Trellis wrapper for [Stateless](https://github.com/dotnet-state-machine/stateless) that returns `Result<TState>` for transitions.

## Installation
```bash
dotnet add package Trellis.StateMachine
```

## Quick Example
```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

enum OrderState { Draft, Submitted }
enum OrderTrigger { Submit }

var machine = new StateMachine<OrderState, OrderTrigger>(OrderState.Draft);
machine.Configure(OrderState.Draft).Permit(OrderTrigger.Submit, OrderState.Submitted);

Result<OrderState> result = machine.FireResult(OrderTrigger.Submit);
```

## Key Features
- Convert invalid transitions into typed domain failures instead of raw exceptions.
- Keep state-machine code inside the same Result pipeline as the rest of your app.
- Support lazy state-machine construction for materialized aggregates.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/state-machines.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
