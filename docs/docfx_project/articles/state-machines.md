---
title: State Machines
package: Trellis.StateMachine
topics: [state-machine, transition, guard, result, aggregate, ddd, stateless, lazy]
related_api_reference: [trellis-api-statemachine.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# State Machines

`Trellis.StateMachine` wraps the [Stateless](https://github.com/dotnet-state-machine/stateless) library so invalid transitions become typed `Result<TState>` failures instead of `InvalidOperationException`s — keeping workflow logic inside the same railway as the rest of your domain code.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Fire a Stateless trigger and get a `Result<TState>` | `stateMachine.FireResult(trigger)` | [Quick start](#quick-start) |
| Treat invalid transitions as 422 rule violations | Default `FireResult` behavior — match on reason code `state.machine.invalid.transition` | [What `FireResult` guarantees](#what-fireresult-guarantees) |
| Defer machine construction until entity state is populated (ORM materialization) | `LazyStateMachine<TState, TTrigger>` | [Lazy construction for aggregates](#lazy-construction-for-aggregates) |
| Compose a transition with domain side effects and events | `FireResult(...).Tap(...).Map(...)` | [Composition](#composition) |
| Block transitions on dynamic conditions | Stateless `PermitIf` / `IgnoreIf` (honored by `CanFire`) | [Guards](#guards) |

## Use this guide when

- The order of operations is part of the business rule (orders, approvals, publishing, fulfillment).
- You want invalid transitions to flow through the same `Result<T>` pipeline as validation errors and HTTP failures.
- An ORM (e.g., EF Core) materializes your aggregate before populating its state property, and an eagerly-constructed Stateless machine would read the wrong initial state.
- You need invalid-transition detection that survives Stateless library upgrades (no exception-message parsing).

## Surface at a glance

`Trellis.StateMachine` exposes one static class and one sealed wrapper. The `StateMachine<TState, TTrigger>` type itself comes from the upstream `Stateless` namespace and remains visible in user code.

| Type | Kind | Purpose |
|---|---|---|
| `StateMachineExtensions` | `static class` | Adds `FireResult<TState, TTrigger>(this StateMachine<TState, TTrigger>, TTrigger)` returning `Result<TState>`. |
| `LazyStateMachine<TState, TTrigger>` | `sealed class` | Defers machine creation and configuration until first access via `Machine` or `FireResult`. |

Both generics carry `where TState : notnull` and `where TTrigger : notnull` constraints.

Full signatures: [`trellis-api-statemachine.md`](../api_reference/trellis-api-statemachine.md).

## Installation

```bash
dotnet add package Trellis.StateMachine
```

## Quick start

Configure a Stateless machine, then call `FireResult` instead of `Fire`. Invalid transitions become a typed `Result` failure.

```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

public enum OrderState { Draft, Submitted, Approved, Shipped, Cancelled }
public enum OrderTrigger { Submit, Approve, Ship, Cancel }

var state = OrderState.Draft;
var machine = new StateMachine<OrderState, OrderTrigger>(() => state, s => state = s);

machine.Configure(OrderState.Draft)
    .Permit(OrderTrigger.Submit, OrderState.Submitted)
    .Permit(OrderTrigger.Cancel, OrderState.Cancelled);

machine.Configure(OrderState.Submitted)
    .Permit(OrderTrigger.Approve, OrderState.Approved)
    .Permit(OrderTrigger.Cancel, OrderState.Cancelled);

machine.Configure(OrderState.Approved)
    .Permit(OrderTrigger.Ship, OrderState.Shipped);

Result<OrderState> submit  = machine.FireResult(OrderTrigger.Submit);  // Ok(Submitted)
Result<OrderState> approve = machine.FireResult(OrderTrigger.Approve); // Ok(Approved)
Result<OrderState> invalid = machine.FireResult(OrderTrigger.Submit);  // Fail (UnprocessableContent)
```

## What `FireResult` guarantees

`FireResult` is intentionally narrow — see [`trellis-api-statemachine.md`](../api_reference/trellis-api-statemachine.md#statemachineextensions) for the exact signature and translation rules.

| Outcome | Result |
|---|---|
| `CanFire(trigger)` is `true` | Calls `Fire(trigger)`, returns `Result.Ok(stateMachine.State)`. |
| `CanFire(trigger)` is `false`, default unhandled-trigger handler throws | Returns `Error.UnprocessableContent` (HTTP 422) carrying a `RuleViolation` with reason code `state.machine.invalid.transition`. |
| `CanFire(trigger)` is `false`, custom `OnUnhandledTrigger` swallows the trigger | Returns `Result.Ok(stateMachine.State)` — state unchanged. |
| User entry/exit/transition/guard/accessor/mutator code throws | Exception propagates untouched. |

Invalid-transition detection uses `CanFire` (which honors `PermitIf` / `IgnoreIf` guards) — there is no Stateless message-string parsing, so the failure shape is independent of Stateless's exception text.

> [!NOTE]
> Because `FireResult` evaluates the guard once via `CanFire` and (when permitted) again via `Fire`, transition guards must be **idempotent and side-effect-free** — already a Stateless requirement. Guards run at most twice per call.

> [!WARNING]
> Neither `FireResult` nor `LazyStateMachine` makes Stateless thread-safe. Stateless is single-threaded by contract; concurrent callers on the same machine instance must synchronize externally.

## Guards

Guards are plain Stateless `PermitIf` / `IgnoreIf` predicates. `FireResult` honors them through `CanFire`, so a guard that returns `false` produces the same `Error.UnprocessableContent` as a missing transition.

```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

public enum InvoiceState { Draft, Approved }
public enum InvoiceTrigger { Approve }

bool hasLineItems = false;

var state = InvoiceState.Draft;
var machine = new StateMachine<InvoiceState, InvoiceTrigger>(() => state, s => state = s);

machine.Configure(InvoiceState.Draft)
    .PermitIf(InvoiceTrigger.Approve, InvoiceState.Approved, () => hasLineItems);

Result<InvoiceState> blocked = machine.FireResult(InvoiceTrigger.Approve); // Fail (guard false)
hasLineItems = true;
Result<InvoiceState> ok = machine.FireResult(InvoiceTrigger.Approve);      // Ok(Approved)
```

Because guards may run twice per `FireResult` call, do not mutate state inside them — read flags or value-object snapshots only.

## Lazy construction for aggregates

ORMs typically construct an entity instance, then populate its properties. A state machine wired up in the constructor with `() => Status` will read whatever the property holds at construction time, not the materialized value. `LazyStateMachine<TState, TTrigger>` defers the accessor call, the mutator wiring, and the `configure` callback until first use.

```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

public enum DocumentStatus { Draft, Published, Archived }
public enum DocumentTrigger { Publish, Archive }

public sealed class Document
{
    private readonly LazyStateMachine<DocumentStatus, DocumentTrigger> _machine;

    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;

    public Document()
    {
        _machine = new LazyStateMachine<DocumentStatus, DocumentTrigger>(
            () => Status,
            s => Status = s,
            Configure);
    }

    public Result<DocumentStatus> Publish() => _machine.FireResult(DocumentTrigger.Publish);
    public Result<DocumentStatus> Archive() => _machine.FireResult(DocumentTrigger.Archive);

    private static void Configure(StateMachine<DocumentStatus, DocumentTrigger> machine)
    {
        machine.Configure(DocumentStatus.Draft)
            .Permit(DocumentTrigger.Publish, DocumentStatus.Published);

        machine.Configure(DocumentStatus.Published)
            .Permit(DocumentTrigger.Archive, DocumentStatus.Archived);
    }
}
```

Key facts:

| Aspect | Value |
|---|---|
| Class modifier | `sealed` |
| Thread-safety | Not thread-safe; `_machine ??= CreateMachine()` has no locking. |
| Configuration timing | Once, on first access to `Machine` (or first `FireResult`). |
| Direct Stateless access | Available via the `Machine` property when you need raw Stateless APIs. |
| Constructor null checks | Throws `ArgumentNullException` for any `null` delegate. |

## Composition

The point of returning `Result<TState>` is that a transition composes with the rest of Trellis (`Tap`, `Map`, `Bind`, `Ensure`) — domain mutations, events, and validation chain off the same railway.

```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

public enum OrderStatus { Draft, Submitted, Approved }
public enum OrderTrigger { Submit, Approve }

public sealed class Order
{
    private readonly LazyStateMachine<OrderStatus, OrderTrigger> _machine;
    private readonly List<string> _events = [];

    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public IReadOnlyList<string> Events => _events;

    public Order()
    {
        _machine = new LazyStateMachine<OrderStatus, OrderTrigger>(
            () => Status,
            s => Status = s,
            machine =>
            {
                machine.Configure(OrderStatus.Draft)
                    .Permit(OrderTrigger.Submit, OrderStatus.Submitted);
                machine.Configure(OrderStatus.Submitted)
                    .Permit(OrderTrigger.Approve, OrderStatus.Approved);
            });
    }

    public Result<Order> Submit() =>
        _machine.FireResult(OrderTrigger.Submit)
            .Tap(_ => _events.Add("OrderSubmitted"))
            .Map(_ => this);

    public Result<Order> Approve() =>
        _machine.FireResult(OrderTrigger.Approve)
            .Tap(_ => _events.Add("OrderApproved"))
            .Map(_ => this);
}
```

The pattern is consistent: `FireResult(...)` for the transition, `Tap(...)` for domain side effects (events, audit), `Map(_ => this)` to return the richer aggregate.

Keep business mutations **after** `FireResult` succeeds. Do not place domain side effects inside Stateless `OnEntry`/`OnExit` callbacks — those are for transition mechanics only.

## Practical guidance

- **Use `FireResult`, not `Fire`.** The whole reason to take this dependency is to keep invalid transitions inside the result pipeline.
- **Distinguish state-machine 422s.** All `FireResult` failures share the reason code `state.machine.invalid.transition` — match on it when callers need to react specifically to workflow rejections.
- **422, not 409.** Invalid transitions are semantic rule violations, not concurrent-modification conflicts; retrying will not help. That is why the failure is `Error.UnprocessableContent`.
- **One state machine per aggregate instance.** They are not thread-safe; do not share across requests or threads.
- **Keep guards pure.** They run via `CanFire` and again via `Fire`, so any side effect would execute twice on the success path.
- **Use `LazyStateMachine` for ORM-materialized aggregates.** It removes the manual `_machine ??= Configure()` boilerplate and ensures the accessor reads the populated value.
- **Reach for state machines when the workflow is the rule.** Orders, approvals, publishing, onboarding — yes. Cosmetic UI flags or trivial CRUD lifecycles — no.

## Cross-references

- API surface: [`trellis-api-statemachine.md`](../api_reference/trellis-api-statemachine.md)
- `Result<T>`, `Error.UnprocessableContent`, `RuleViolation`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Cookbook recipe (CanFire + Fire pattern with `FireResult`): [`trellis-api-cookbook.md`](../api_reference/trellis-api-cookbook.md#recipe-9--state-machine-canfire--fire-pattern-with-fireresult)
- Upstream library: [Stateless on GitHub](https://github.com/dotnet-state-machine/stateless)
