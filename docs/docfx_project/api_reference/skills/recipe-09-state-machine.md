---
name: recipe-09-state-machine
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows the CanFire and Fire pattern with FireResult for aggregate state machines. Use when adding a state transition to an aggregate."
---
# Recipe 9 — State machine: `CanFire` + `Fire` pattern with `FireResult`

**Requires:** `Trellis.Core`, plus `Trellis.StateMachine`.

**Problem.** Drive an order through `Draft → Submitted → Shipped` using Stateless, but expose every transition as `Result<TState>` so the mediator pipeline composes naturally.

```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

// States and triggers as RequiredEnum value objects (Trellis convention) —
// equality is symbolic, so Stateless's TState/TTrigger generic constraints are satisfied.
public partial class DocumentState : RequiredEnum<DocumentState>
{
    public static readonly DocumentState Draft     = new();
    public static readonly DocumentState Submitted = new();
    public static readonly DocumentState Approved  = new();
}

public partial class DocumentTrigger : RequiredEnum<DocumentTrigger>
{
    public static readonly DocumentTrigger Submit  = new();
    public static readonly DocumentTrigger Approve = new();
    public static readonly DocumentTrigger Reject  = new();
}

public sealed class DocumentService
{
    public Result<DocumentState> Submit(Document doc)
    {
        var machine = new StateMachine<DocumentState, DocumentTrigger>(doc.State);
        machine.Configure(DocumentState.Draft).Permit(DocumentTrigger.Submit, DocumentState.Submitted);
        machine.Configure(DocumentState.Submitted)
               .Permit(DocumentTrigger.Approve, DocumentState.Approved)
               .Permit(DocumentTrigger.Reject,  DocumentState.Draft);

        // FireResult pre-checks CanFire and converts invalid transitions to an
        // Error.InvariantViolation (HTTP 422) with Code
        // "state-machine.invalid-transition" — a rejected transition is a domain-invariant
        // breach, not inbound-input validation or a concurrent-modification conflict.
        Result<DocumentState> result = machine.FireResult(DocumentTrigger.Submit);
        return result.Tap(newState => doc.State = newState);
    }
}
```

**What it shows.** `StateMachineExtensions.FireResult(...)` honors `PermitIf`/`IgnoreIf` guards via `CanFire(...)` rather than parsing exception messages, so invalid-transition detection is independent of Stateless exception text. For aggregates whose state lives in a backing field (e.g., loaded from EF), use `LazyStateMachine<TState, TTrigger>` to defer machine creation until the first `FireResult` call.

**Side-effect placement.** Keep Stateless configuration declarative: states, triggers, permitted transitions, and pure/idempotent guards. Put business mutation, domain events, outbox writes, and other side effects after `FireResult` succeeds, usually in `.Tap(...)` as shown above. `FireResult` short-circuits when `CanFire(...)` is false and does **not** invoke `Fire(...)`, so configured `OnUnhandledTrigger` callbacks do not run from the typed-result path. Consumers who want an `OnUnhandledTrigger` callback to run must call Stateless `Fire(...)` directly. If side effects live in `OnEntry`, `OnExit`, transition callbacks, or `OnUnhandledTrigger`, they can run outside the visible ROP success/failure path and make handler behavior diverge from tests.

> **HTTP semantics.** Invalid state-machine transitions surface as `Error.InvariantViolation` (HTTP 422), not `Error.InvalidInput` or `Error.Conflict` (HTTP 409). The reasoning: a rejected transition ("you asked for `Submit` on a `Cancelled` order") is a breach of the aggregate's lifecycle invariant evaluated against its current state — the request itself is well-formed (so it is not inbound-input validation), and it is not a concurrent-modification conflict that a retry could resolve. Both `InvalidInput` and `InvariantViolation` map to 422 and share the on-wire ProblemDetails `kind` `unprocessable-content`, so the HTTP response is unchanged; the distinction is the domain error type (its `Kind` slug becomes `invariant-violation`). Callers that need to distinguish state-machine rejections from other 422s can match on the `Code` value `state-machine.invalid-transition`.

```csharp
// Asserting on a state-machine rejection in tests:
var invariant = result.Error.Should().BeOfType<Error.InvariantViolation>().Subject;
invariant.Code.Should().Be("state-machine.invalid-transition");
```
