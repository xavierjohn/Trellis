---
package: Trellis.StateMachine
namespaces: [Trellis.StateMachine]
types: [StateMachineExtensions, "LazyStateMachine<TState, TTrigger>"]
version: v3
last_verified: 2026-06-21
audience: [llm]
---
# Trellis.StateMachine — API Reference

## Header

- **Package:** `Trellis.StateMachine`
- **Namespace:** `Trellis.StateMachine`
- **Purpose:** Wraps Stateless state transitions in Trellis `Result<TState>` APIs and provides lazy state-machine construction for aggregate materialization scenarios.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-9--state-machine-canfire--fire-pattern-with-fireresult) — recipes using this package.

## Use this file when

- You are wrapping Stateless transitions in Trellis `Result<TState>` values.
- You need lazy state-machine construction for aggregates materialized by an ORM.
- You need the exact invalid-transition behavior of `FireResult`.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Fire a Stateless trigger and get a Trellis result | `stateMachine.FireResult(trigger)` | [`StateMachineExtensions`](#statemachineextensions) |
| Store a state machine inside an aggregate | `LazyStateMachine<TState,TTrigger>` with state accessor/mutator delegates | [`LazyStateMachine<TState, TTrigger>`](#lazystatemachinetstate-ttrigger) |
| Treat invalid transitions as domain-invariant breaches | Let `FireResult` short-circuit when `CanFire` returns `false` and return `Error.InvariantViolation` | [Behavioral notes](#behavioral-notes) |
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
| `public static Result<TState> FireResult<TState, TTrigger>(this StateMachine<TState, TTrigger> stateMachine, TTrigger trigger) where TState : notnull where TTrigger : notnull` | `Result<TState>` | Pre-checks with `stateMachine.CanFire(trigger)` (which honors `PermitIf`/`IgnoreIf` guards). When permitted, calls `stateMachine.Fire(trigger)` and returns `Result.Ok(stateMachine.State)`. When not permitted, returns `Error.InvariantViolation.ForReason("state.machine.invalid.transition", $"Trigger '{trigger}' is not permitted from state '{stateMachine.State}'.")` (HTTP 422) without invoking `Fire(trigger)`, so user-configured `OnUnhandledTrigger` callbacks do not run through `FireResult`. Consumers who need those callbacks must call Stateless `Fire` directly. `InvalidOperationException` thrown while evaluating a guard is translated to `Error.InvariantViolation` with the exception message; other exception types and exceptions from user entry/exit/transition actions propagate untouched. Independent of Stateless's exception message format. |

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
- `StateMachineExtensions.FireResult` null-checks `stateMachine` and throws `ArgumentNullException` for a `null` receiver before any Trellis error conversion occurs.
- `LazyStateMachine<TState, TTrigger>` is also **not** thread-safe. Its lazy initialization uses `_machine ??= CreateMachine()` with no locking.
- Invalid-transition detection uses `StateMachine.CanFire(trigger)` (which honors `PermitIf`/`IgnoreIf` guards) — no message-string parsing, so it is resilient to Stateless library upgrades.
- When `CanFire` returns `false`, `FireResult` returns `Error.InvariantViolation.ForReason("state.machine.invalid.transition", $"Trigger '{trigger}' is not permitted from state '{stateMachine.State}'.")` (HTTP 422) without invoking `Fire`, so user-configured `OnUnhandledTrigger` callbacks do not run. Call Stateless `Fire` directly when that callback policy is desired.
- `InvalidOperationException` thrown while evaluating a guard is translated to `Error.InvariantViolation` with the guard exception message. Other exception types and exceptions thrown by user entry, exit, transition, accessor, mutator, or configuration code are not swallowed.
- `LazyStateMachine<TState, TTrigger>` exists to defer state-machine construction until after entity state is available, which is useful when ORMs materialize an object before populating its state properties.

## Scope boundary — async and parameterized triggers

`Trellis.StateMachine` deliberately wraps only the synchronous, parameterless trigger shape `Fire(TTrigger)`. Stateless also supports:

- `FireAsync(TTrigger)` and the parameterized async overloads for state machines with `OnEntryAsync`/`OnExitAsync`/`OnActivateAsync` callbacks.
- Parameterized triggers via `SetTriggerParameters<TArg>(...)` (returning a `TriggerWithParameters<TArg>` token) and `Fire(triggerWithParameters, arg)` / `FireAsync(triggerWithParameters, arg)` for passing context (timestamps, cancellation reasons, etc.) into transition actions.

These shapes are **not** wrapped today. Consumers who need them must call the underlying Stateless APIs directly and translate exceptions themselves; the Trellis `Result<TState>` pipeline ends at the sync, no-arg boundary. If a future Trellis version adds `FireResultAsync` / parameterized overloads, they will follow the same `CanFire` short-circuit design and will use library-source `ConfigureAwait(false)` on awaited Stateless operations.

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

- [trellis-api-core.md](trellis-api-core.md#public-abstract-record-error) — `Result<T>`, `Error.InvariantViolation`.
- [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-9--state-machine-canfire--fire-pattern-with-fireresult) — Recipe 9: state-machine `CanFire` + `Fire` pattern with `FireResult`.
- [trellis-api-asp.md](trellis-api-asp.md#domain--http-boundary-mapping) — how `Error.InvariantViolation` renders as HTTP 422 (top-level `detail` reads `Error.Detail`).

## Breaking changes from v1

- **Package renamed:** `Trellis.Stateless` → `Trellis.StateMachine`. Vendor independence in the *name*, not the *implementation* — the underlying [Stateless](https://github.com/dotnet-state-machine/stateless) library is still referenced directly, and `StateMachine<TState, TTrigger>` from the `Stateless` namespace remains visible in user code.
- **Namespace renamed:** `Trellis.Stateless` → `Trellis.StateMachine`. Replace `using Trellis.Stateless;` with `using Trellis.StateMachine;`.
- **Method signatures are unchanged:** `StateMachineExtensions.FireResult<TState, TTrigger>(...)` and `LazyStateMachine<TState, TTrigger>` keep the same shape.
- **Rejected transitions now return `Error.InvariantViolation`** (reason code `state.machine.invalid.transition`), not `Error.InvalidInput`. A disallowed lifecycle transition is a domain-invariant breach, not inbound-input validation. Both still map to HTTP 422, so the wire status is unchanged; but code that matches on the error type or reads `Error.InvalidInput.Rules` must update to `Error.InvariantViolation` and its `ReasonCode`.
- **No metapackage redirect.** The old `Trellis.Stateless` package is not shipped and there is no shim. Update your `PackageReference` directly.
