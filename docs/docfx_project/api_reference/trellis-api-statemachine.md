---
package: Trellis.StateMachine
namespaces: [Trellis.StateMachine]
types: [IStateMachine<,>, StateBuilder<,>, Transition<,>]
version: v3
last_verified: 2026-05-01
audience: [llm, developer]
---
# Trellis.StateMachine — API Reference

## Header

- **Package:** `Trellis.StateMachine`
- **Namespace:** `Trellis.StateMachine`
- **Purpose:** Wraps Stateless state transitions in Trellis `Result<TState>` APIs and provides lazy state-machine construction for aggregate materialization scenarios.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

## Use this file when

- You are wrapping Stateless transitions in Trellis `Result<TState>` values.
- You need lazy state-machine construction for aggregates materialized by an ORM.
- You need the exact invalid-transition behavior of `FireResult`.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Fire a Stateless trigger and get a Trellis result | `stateMachine.FireResult(trigger)` | [`StateMachineExtensions`](#statemachineextensions) |
| Store a state machine inside an aggregate | `LazyStateMachine<TState,TTrigger>` with state accessor/mutator delegates | [`LazyStateMachine<TState, TTrigger>`](#lazystatemachinetstate-ttrigger) |
| Treat invalid transitions as validation failures | Let `FireResult` map default unhandled-trigger `InvalidOperationException` to `Error.UnprocessableContent` | [Behavioral notes](#behavioral-notes) |
| Apply business mutations after successful transition | Call `.FireResult(...)`, then mutate/domain-event in a `.Tap(...)` or explicit success branch | [Code examples](#code-examples), [Cookbook Recipe 9](trellis-api-cookbook.md#recipe-9--state-machine-canfire--fire-pattern-with-fireresult) |

## Common traps

- Do not model triggers as raw strings when the domain already has a typed enum/value object.
- Do not put business side effects in Stateless configuration unless they are purely transition mechanics. Keep domain mutation and events after `FireResult` succeeds.
- `FireResult` does not make Stateless thread-safe; external synchronization is still required for concurrent use.

## Types

### `StateMachineExtensions`

**Declaration**

```csharp
public static class StateMachineExtensions
```

**Constructors**

- None. This is a static class.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| None | — | This static class exposes no public properties. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TState> FireResult<TState, TTrigger>(this StateMachine<TState, TTrigger> stateMachine, TTrigger trigger) where TState : notnull where TTrigger : notnull` | `Result<TState>` | Pre-checks with `stateMachine.CanFire(trigger)` (which honors `PermitIf`/`IgnoreIf` guards). When permitted, calls `stateMachine.Fire(trigger)` and returns `Result.Ok(stateMachine.State)`. When not permitted, still invokes `Fire(trigger)` so any user-configured `OnUnhandledTrigger` callback runs: an `InvalidOperationException` from that path is translated to `Error.UnprocessableContent.ForRule("state.machine.invalid.transition", $"Trigger '{trigger}' is not permitted from state '{stateMachine.State}'.")` (HTTP 422 — invalid transitions are semantic rule violations, not concurrent-modification conflicts). If a custom unhandled-trigger handler swallows the trigger, returns `Result.Ok(stateMachine.State)` (state unchanged). Other exception types from user entry/exit/transition actions propagate untouched. Independent of Stateless's exception message format. |

### `LazyStateMachine<TState, TTrigger>`

**Declaration**

```csharp
public sealed class LazyStateMachine<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
```

**Constructors**

- `public LazyStateMachine(Func<TState> stateAccessor, Action<TState> stateMutator, Action<StateMachine<TState, TTrigger>> configure)`  
  Throws `ArgumentNullException` when `stateAccessor`, `stateMutator`, or `configure` is `null`. The constructor stores the delegates only; it does not invoke `stateAccessor`, `stateMutator`, or `configure`.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `Machine` | `StateMachine<TState, TTrigger>` | Returns `_machine ??= CreateMachine()`. First access constructs `new StateMachine<TState, TTrigger>(_stateAccessor, _stateMutator)`, invokes `_configure(machine)`, caches the instance, and returns it. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public Result<TState> FireResult(TTrigger trigger)` | `Result<TState>` | Delegates to `Machine.FireResult(trigger)`. On first use, this also triggers lazy creation and configuration of the underlying `StateMachine<TState, TTrigger>`. |

## Extension methods

### `StateMachineExtensions`

```csharp
public static Result<TState> FireResult<TState, TTrigger>(
    this StateMachine<TState, TTrigger> stateMachine,
    TTrigger trigger)
    where TState : notnull
    where TTrigger : notnull
```

## Behavioral notes

- `StateMachineExtensions.FireResult` does **not** make Stateless thread-safe. Concurrent use of the same `StateMachine<TState, TTrigger>` instance still requires external synchronization. Because Stateless is single-threaded by contract, the `CanFire`+`Fire` pre-check pattern is race-free when used as documented.
- `StateMachineExtensions.FireResult` does not null-check `stateMachine`; a `null` receiver will fail before any Trellis error conversion occurs.
- `LazyStateMachine<TState, TTrigger>` is also **not** thread-safe. Its lazy initialization uses `_machine ??= CreateMachine()` with no locking.
- Invalid-transition detection uses `StateMachine.CanFire(trigger)` (which honors `PermitIf`/`IgnoreIf` guards) — no message-string parsing, so it is resilient to Stateless library upgrades.
- When `CanFire` returns `false`, `Fire` is still invoked so any user-configured `OnUnhandledTrigger` callback runs. If the default unhandled-trigger handler throws `InvalidOperationException`, that path is translated to `Error.UnprocessableContent.ForRule("state.machine.invalid.transition", $"Trigger '{trigger}' is not permitted from state '{stateMachine.State}'.")` (HTTP 422). A custom handler that swallows the trigger results in `Result.Ok(stateMachine.State)` with state unchanged.
- Exceptions thrown by user entry, exit, transition, guard, accessor, mutator, or configuration code are not swallowed.
- `LazyStateMachine<TState, TTrigger>` exists to defer state-machine construction until after entity state is available, which is useful when ORMs materialize an object before populating its state properties.

## Code examples

### Use `FireResult` on a regular Stateless machine

```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

enum OrderState { Draft, Submitted, Cancelled }
enum OrderTrigger { Submit, Cancel }

var machine = new StateMachine<OrderState, OrderTrigger>(OrderState.Draft);
machine.Configure(OrderState.Draft)
    .Permit(OrderTrigger.Submit, OrderState.Submitted)
    .Permit(OrderTrigger.Cancel, OrderState.Cancelled);

Result<OrderState> submitResult = machine.FireResult(OrderTrigger.Submit);
Result<OrderState> invalidResult = machine.FireResult(OrderTrigger.Submit);
```

### Use `LazyStateMachine<TState, TTrigger>`

```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

enum DocumentState { Draft, Published }
enum DocumentTrigger { Publish }

var state = DocumentState.Draft;

var lazyMachine = new LazyStateMachine<DocumentState, DocumentTrigger>(
    () => state,
    s => state = s,
    machine => machine.Configure(DocumentState.Draft)
        .Permit(DocumentTrigger.Publish, DocumentState.Published));

Result<DocumentState> result = lazyMachine.FireResult(DocumentTrigger.Publish);
StateMachine<DocumentState, DocumentTrigger> machine = lazyMachine.Machine;
```

## Cross-references

- [trellis-api-core.md](trellis-api-core.md)
- [trellis-api-core.md](trellis-api-core.md)

## Breaking changes from v1

- **Package renamed:** `Trellis.Stateless` → `Trellis.StateMachine`. Vendor independence in the *name*, not the *implementation* — the underlying [Stateless](https://github.com/dotnet-state-machine/stateless) library is still referenced directly, and `StateMachine<TState, TTrigger>` from the `Stateless` namespace remains visible in user code.
- **Namespace renamed:** `Trellis.Stateless` → `Trellis.StateMachine`. Replace `using Trellis.Stateless;` with `using Trellis.StateMachine;`.
- **Public surface is otherwise identical:** `StateMachineExtensions.FireResult<TState, TTrigger>(...)` and `LazyStateMachine<TState, TTrigger>` are unchanged.
- **No metapackage redirect.** The old `Trellis.Stateless` package is not shipped and there is no shim. Update your `PackageReference` directly.
