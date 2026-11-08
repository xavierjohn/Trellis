---
name: recipe-20-fail-fast-vs-accumulating
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Contrasts fail-fast Sequence and Traverse with accumulating SequenceAll and TraverseAll. Use when choosing how a collection operation reports errors."
---
# Recipe 20 — Fail-fast vs accumulating: `Sequence`/`Traverse` vs `SequenceAll`/`TraverseAll`

**Requires:** `Trellis.Core`, plus `Trellis.Primitives`.

**Problem.** A single pipeline contains two distinct collection-level concerns:

1. **Form-style validation** of a batch payload — every invalid row should be reported in one response, not just the first.
2. **A fan-out fetch** — once one upstream fetch fails, finishing the rest is wasted I/O; the first failure should win.

Trellis ships both shapes on the same surface so you can pick the semantics per call site.

```csharp
using Trellis;
using Trellis.Primitives;

public sealed record CreateContactRow(string Email, string Name);

// 1) Accumulating: every row's failure surfaces in the response.
public Result<IReadOnlyList<EmailAddress>> ValidateAddresses(IEnumerable<CreateContactRow> rows) =>
    rows.TraverseAll(row => EmailAddress.TryCreate(row.Email));
//        ↑ TraverseAll runs the selector for every row.
//          - All succeed   → Ok(list)
//          - One bad email → that single Error.InvalidInput (no Aggregate wrap)
//          - Many bad      → one merged Error.InvalidInput whose
//                            Fields/Rules concatenate every per-item violation
//          - Mixed kinds   → flat Error.Aggregate of every distinct error

// Indexed accumulation: errors identify the original row, e.g. /contacts/2/email.
public static Result<IReadOnlyList<EmailAddress>> ValidateAddressesIndexed(IEnumerable<CreateContactRow> rows) =>
    rows.TraverseAll((row, index) => EmailAddress.TryCreate(row.Email,
        InputPointer.Root.AppendProperty("contacts").AppendIndex(index).AppendProperty("email").Path));

// Application-owned async validation receives each row, index, and token.
public static Task<Result<IReadOnlyList<EmailAddress>>> ValidateAddressesIndexedAsync(
    IEnumerable<CreateContactRow> rows,
    Func<CreateContactRow, int, CancellationToken, Task<Result<EmailAddress>>> validateAsync,
    CancellationToken ct) =>
    rows.TraverseAllAsync((row, index, token) => validateAsync(row, index, token), ct);

// 2) Fail-fast: stop on the first upstream miss.
public Task<Result<IReadOnlyList<Order>>> LoadOrders(IEnumerable<OrderId> ids, CancellationToken ct) =>
    ids.TraverseAsync((id, c) => repo.LoadAsync(id, c), ct);
//        ↑ TraverseAsync short-circuits on the first failure — no
//          subsequent repository calls are issued.
```

**What it shows.**

- `TraverseAll` / `SequenceAll` exist precisely to solve "show me every error". They use the same `Error.Combine` extension as `EnsureAll`, so two `InvalidInput` failures merge and unrelated failures flatten into `Error.Aggregate`.
- `Traverse` / `Sequence` exist precisely to solve "stop wasting work on the first failure". They never *accumulate into* an `Error.Aggregate`; they propagate the first failure as-is (which means if a selector itself returns `Result.Fail<T>(new Error.Aggregate(...))`, that `Aggregate` flows through unchanged — not because Traverse created it, but because Traverse preserves whatever the failing selector produced).
- The unindexed `TraverseAll` family ships the same async surface as `Traverse`: sync, `Task`, `Task` + `CancellationToken`, `ValueTask`, `ValueTask` + `CancellationToken`, plus a `Task<Result<Unit>>` + `CancellationToken` overload. `SequenceAll` is sync-only because `Sequence` is sync-only; if async siblings ever land for `Sequence`, they land for `SequenceAll` at the same time.
- Indexed `TraverseAll` accepts `(item, index)`; indexed `TraverseAllAsync` accepts `(item, index, token)` for Task, ValueTask, and no-payload Task selectors. The token argument is optional, but the lambda keeps all three parameters to distinguish it from the existing `(item, token)` overload. Indices are zero-based source positions, not success counts; async selectors run sequentially and failures do not stop subsequent items.
- Already have an `IEnumerable<Result<T>>` (e.g. from a `Select` over a `TryCreate`)? Pick `.Sequence()` (fail-fast) or `.SequenceAll()` (accumulating); they're the identity-selector forms of `Traverse` / `TraverseAll`.

**Anti-pattern → fix.**

```csharp
// ❌ Manual loop with early return: loses every error after the first.
foreach (var row in rows)
{
    var r = EmailAddress.TryCreate(row.Email);
    if (r.IsFailure) return r.Map(_ => default(IReadOnlyList<EmailAddress>)!);
    parsed.Add(r.Unwrap());
}
// ✅ Explicit choice between fail-fast and accumulating semantics:
return rows.TraverseAll(row => EmailAddress.TryCreate(row.Email));
```
