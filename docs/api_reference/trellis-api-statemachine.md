# Trellis.StateMachine — API Reference

## Header

- **Package:** `Trellis.StateMachine`
- **Namespace:** `Trellis.StateMachine`
- **Purpose:** Wraps Stateless state transitions in Trellis `Result<TState>` APIs and provides lazy state-machine construction for aggregate materialization scenarios.

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
| `public static Result<TState> FireResult<TState, TTrigger>(this StateMachine<TState, TTrigger> stateMachine, TTrigger trigger) where TState : notnull where TTrigger : notnull` | `Result<TState>` | Calls `stateMachine.Fire(trigger)` once and returns the resulting `stateMachine.State` on success. Converts only recognized Stateless invalid-transition `InvalidOperationException` instances into `new Error.Conflict(null, "state.machine.invalid.transition") { Detail = exception.Message }`. Other exceptions are rethrown. |

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

- `StateMachineExtensions.FireResult` does **not** make Stateless thread-safe. Concurrent use of the same `StateMachine<TState, TTrigger>` instance still requires external synchronization.
- `StateMachineExtensions.FireResult` does not null-check `stateMachine`; a `null` receiver will fail before any Trellis error conversion occurs.
- `LazyStateMachine<TState, TTrigger>` is also **not** thread-safe. Its lazy initialization uses `_machine ??= CreateMachine()` with no locking.
- Invalid-transition conversion is intentionally narrow. The caught exception must:
  - be an `InvalidOperationException`;
  - have `exception.Source == typeof(StateMachine<,>).Assembly.GetName().Name`; and
  - have a message that either starts with `"No valid leaving transitions are permitted from state '"` or contains `" is valid for transition from state '"`.
- Invalid transitions become `new Error.Conflict(null, "state.machine.invalid.transition") { Detail = exception.Message }`.
- Exceptions thrown by user entry, exit, transition, guard, accessor, mutator, or configuration code are not swallowed unless they satisfy the exact invalid-transition filter above.
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

- **Package renamed:** `Trellis.Stateless` → `Trellis.StateMachine` (ADR-002 §9). Vendor independence in the *name*, not the *implementation* — the underlying [Stateless](https://github.com/dotnet-state-machine/stateless) library is still referenced directly, and `StateMachine<TState, TTrigger>` from the `Stateless` namespace remains visible in user code.
- **Namespace renamed:** `Trellis.Stateless` → `Trellis.StateMachine`. Replace `using Trellis.Stateless;` with `using Trellis.StateMachine;`.
- **Public surface is otherwise identical:** `StateMachineExtensions.FireResult<TState, TTrigger>(...)` and `LazyStateMachine<TState, TTrigger>` are unchanged.
- **No metapackage redirect.** Clean cut at v2.0.0 — the old `Trellis.Stateless` package is not shipped in v2 and there is no shim. Update your `PackageReference` directly.
