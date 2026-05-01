# ADR-005 — Re-introduce `Trellis.Unit` and Collapse Non-Generic `Result` to a Static Factory

> **Status:** Accepted (counter-ADR to ADR-002 §3 "Unit removal").
>
> **Date:** 2026-04-30
>
> **Supersedes (if accepted):** ADR-002 §3 lines 502–570 (the "Unit is removed from the public API; non-generic `Result` represents success-or-failure-without-payload" decision).

---

## Context

ADR-002 §3 removed `Unit` from the public API and introduced a non-generic `Result` type as a peer to `Result<T>`, justified by:

1. Eliminating the `Trellis.Unit` vs `Mediator.Unit` (martinothamar/Mediator) namespace collision.
2. Call-site ergonomics: `Tap(() => log("done"))` reads cleaner than `Tap(_ => log("done"))`.
3. The claim that the receiver type would filter IntelliSense to ~12 overloads per verb regardless of the absolute overload count, so AI/IntelliSense never has to choose between unit and value shapes ("the call-site count goes *down*, not up" — ADR-002:559).

The honest cost the original ADR admitted (ADR-002:546–559):

- Verb shapes: 7 → 16 (Map, Bind, Tap, Tap, Ensure, Match, Recover all need unit + cross-shape variants).
- × 6 async forms = **96 overloads vs ~42** with `Result<Unit>`.
- Plus `BindZip` (T4 1..9 arity) and the LINQ surface get the same treatment, adding roughly another 30 overloads.

This ADR records the empirical evidence accumulated between 2026-04-27 and 2026-04-30 and proposes reverting the Unit-removal decision while preserving the namespace-collision fix.

---

## Evidence

Four head-to-head LLM lab runs against the Trellis framework + Order Management spec (recorded in `Trellis-lab-runs/LAB_HISTORY.md`):

- 2026-04-27: GPT-5.5 single-model baseline.
- 2026-04-29: 2-model (GPT-5.5 + Opus 4.7), the first run on the v2 surface after the rename.
- 2026-04-30: 3-model (GPT-5.5 + Opus 4.7 + Sonnet 4.6), the first run after the 14-file `trellis-api-*.md` rewrite.

These runs collectively executed Step 4 (initial implementation of an Order Management service) and Step 7 (`TRELLIS_FEEDBACK.md` self-feedback) on three independent models.

### Friction Mode 1 — `Map` / `Bind` arity confusion was the most-cited fluent-API friction

All three models in the 2026-04-30 run cited Map/Bind arity confusion as a friction point in their `TRELLIS_FEEDBACK.md`:

> "I had to remember whether the source was `Result` (non-generic) or `Result<T>` to know whether `Map`'s lambda took zero parameters or one. Several compile failures came from `Map(_ => ...)` on a non-generic source — the only available overload is `Map<TOut>(Func<TOut>)` (`Result.NonGeneric.Extensions.cs:38`), so the compiler rejects the discard-parameter form by ordinary overload resolution." — paraphrased from one feedback doc; equivalent statements appear in all three.

This contradicts the ADR-002:559 claim that the *call-site* surface is smaller. The call site sees:

- `Result.Ok().Map(() => 1)` — works
- `Result.Ok().Map(_ => 1)` — CS1593 (no overload accepts `Func<T,U>` here)
- `Result.Ok(0).Map(_ => 1)` — works
- `Result.Ok(0).Map(() => 1)` — CS1593 (no overload accepts `Func<U>` here)

The receiver type does narrow the overload list, but the overload list now contains *only one shape that compiles*, and the rule for which is which depends on remembering whether the receiver is generic or non-generic. With `Result<Unit>`, the lambda always takes one parameter; the discard is uniform; the compiler accepts both forms via the single generic overload (`Map.cs:26-35`).

### Friction Mode 2 — `Sequence(selector)` vs `Traverse(selector)` was a real shipping bug

Sonnet 4.6's 2026-04-30 feedback (`F2`) identified that `template/.github/copilot-instructions.md:302` writes:

```csharp
products.Sequence(p => p.ReleaseStock(...))
```

…but `Sequence<T>` has no selector overload (`Trellis.Core/src/Result/Extensions/Traverse.cs:270`). The selector form is `Traverse<TIn,TOut>` (line 40). This was a real bug in template-shipped guidance that no other model caught.

Root cause: when `Result` and `Result<T>` are peer types, the framework needs *two* operators to express "apply a fallible function across a collection" — one that returns `Result` (the non-generic, when the function is unit-returning) and one that returns `Result<TOut>` (the generic). The natural names — `Sequence` and `Traverse` — diverge in selector arity in a way that mirrors the Map/Bind asymmetry above. Under `Result<Unit>`, both collapse to a single `Traverse<TIn,TOut>` where `TOut = Unit` is just one of many call sites and no special operator is required.

### Friction Mode 3 — Cross-shape `Bind` matrix is incomplete by design

The non-generic `Result` introduced cross-shape `Bind` overloads to bridge unit↔value (centralized in `Result.NonGeneric.Extensions.cs:124-169`):

- `Result.Bind(Func<Result> next)` — unit→unit ✓ exists
- `Result.Bind<U>(Func<Result<U>> next)` — unit→value ✓ exists
- `Result<T>.Bind(Func<T, Result> next)` — value→unit ✓ exists
- `Result<T>.Bind(Func<Result<U>> next)` — value→value (ignoring T) — **does not exist** (consistent with ADR-002 "no Bind that ignores its source")

This is internally coherent but produces a footgun: AI models reach for the missing shape (write `someValueResult.Bind(() => DoNext())` expecting it to compile), get a CS1593 overload-resolution error, and then have to either add a `_ =>` discard or restructure into a `.Tap(_ => ...).Map(_ => ...)` chain. The 2026-04-29 baseline run logged 4 instances of this across both models (Opus and GPT). The 2026-04-30 run logged 3 more.

Under `Result<Unit>`:
- `Result<T>.Bind(Func<T, Result<U>>)` — exists (the only shape).
- "Value-returning step that ignores prior value" is written `r.Bind(_ => DoNext())` — uniform across all chains.

There is no missing shape because there are no shape variants.

### Friction Mode 4 — `Result` cannot be the LINQ identity for `Result<T>`

Trellis exposes a LINQ surface (`Trellis.Core/src/Result/Extensions/Linq.cs`) that lets `from x in result1 from y in result2 select x + y` desugar to monadic `Bind`. The LINQ pattern requires:

- `SelectMany<TIn,TBind,TOut>(this Result<TIn>, Func<TIn,Result<TBind>>, Func<TIn,TBind,TOut>) → Result<TOut>`

When the right-hand side is unit (`Result`), the synthesized `TBind` cannot be inferred — the LINQ compiler has no way to match `Func<TIn, Result>` against the expected `Func<TIn, Result<TBind>>`. The current implementation handles this by *omitting* unit-returning steps from the LINQ surface entirely — they must be expressed as `.Tap(...)` or `.Bind(...)` at the chain boundary, breaking out of LINQ.

Under `Result<Unit>`, every step has a synthesizable `TBind` (it's `Unit`). The LINQ compiler is happy. No "break out of LINQ for unit steps" rule.

This is the only friction mode that is *strictly* impossible to fix while keeping non-generic `Result` as a peer type.

### Friction Mode 5 — Mediator handler signatures are noisier, not cleaner

ADR-002 claimed `Task<Result>` is an improvement over `Task<Result<Unit>>`. In practice in the lab runs, handler bodies look like:

```csharp
// Current (non-generic Result peer type)
public async ValueTask<Result> Handle(SubmitOrderCommand cmd, CancellationToken ct)
{
    var order = await _repo.GetByIdAsync(cmd.OrderId, ct);
    return order
        .ToResult(new Error.NotFound(...))
        .Bind(o => o.Submit())     // Result<Order>
        .AsUnit();                 // <-- existing operator that drops the value
}
```

`AsUnit()` already exists (`Trellis.Core/src/Result/Result{TValue}.cs:304`) and bridges `Result<T>` → non-generic `Result`. The friction is not the bridging operator itself — it is that the chain has to *cross a type boundary* at the end. Under `Result<Unit>` the same expression is:

```csharp
public async ValueTask<Result<Unit>> Handle(SubmitOrderCommand cmd, CancellationToken ct)
{
    var order = await _repo.GetByIdAsync(cmd.OrderId, ct);
    return order
        .ToResult(new Error.NotFound(...))
        .Bind(o => o.Submit())   // Result<Order>
        .AsUnit();               // <-- now returns Result<Unit>; same shape, no boundary crossing
}
```

`AsUnit()` is repurposed in v3 to return `Result<Unit>` (see §What changes #4 below). The chain stays uniform — every step returns `Result<TSomething>`, no shape switch happens. Note: today `Discard()` (`Trellis.Core/src/Result/Extensions/Discard.cs:19`) returns `void` and is *not* a chain-preserving operator — it is an end-of-chain "I am intentionally ignoring the outcome" marker, semantically distinct from `AsUnit()`. ADR-005 does not change `Discard()`.

---

## Decision

Re-introduce `Trellis.Unit` as public API and collapse non-generic `Result` to **Option B: a static factory holder**, in a single v3 breaking transition.

### What changes

1. **`Trellis.Unit` becomes public.** `internal record struct Unit { }` → `public readonly record struct Unit { public static readonly Unit Default; }`. Single canonical instance, value type, default-constructible.

2. **Non-generic `Result` (instance type) is replaced by `Result` (static class).** `Trellis.Core/src/Result/Result.cs` (354 lines, instance type with `IsSuccess`/`IsFailure`/`Error`/`TryGetError`/`Deconstruct` plus all factories) is replaced by a static factory class with no instance shape:

    ```csharp
    public static class Result
    {
        public static Result<TValue> Ok<TValue>(TValue value);
        public static Result<TValue> Fail<TValue>(Error error);

        // Unit-returning factories — return Result<Unit>
        public static Result<Unit> Ok();              // Result<Unit> success
        public static Result<Unit> Fail(Error error); // Result<Unit> failure

        public static Result<TValue> Try<TValue>(Func<TValue> work, Func<Exception, Error>? map = null);
        public static Result<Unit> Try(Action work, Func<Exception, Error>? map = null);
        // ... TryAsync overloads ...
        public static Result<Unit> Ensure(bool condition, Error error);
        public static Result<Unit> Ensure(Func<bool> predicate, Error error);
    }
    ```

   Call sites that today read `Result.Ok()` / `Result.Fail(err)` / `Result.Ensure(cond, err)` continue to compile; the *returned type* is now `Result<Unit>` instead of the instance `Result`. Call sites that today read `Result.Ok(value)` are unchanged.

   The non-generic `Result` is also a `partial readonly struct` across `Result.cs`, `Result.Combine.cs`, and the T4-generated outputs of `CombineTs.g.tt` and `ParallelAsyncs.g.tt`. All four files are migrated together; the T4 templates are updated to emit only `Result<TValue>` shapes.

3. **`Result.NonGeneric.Extensions.cs` (828 lines, all non-generic and cross-shape verb overloads) is deleted.** Verbs apply uniformly to `Result<T>` where `T` may be `Unit`. Cross-shape `Bind`/`Map`/`Tap`/`Ensure`/`Match`/`Recover` overloads in this file collapse to ordinary single-shape generic verbs.

4. **`Result<T>.AsUnit()` is repurposed in v3.** Today it returns non-generic `Result` (`Result{TValue}.cs:304`). In v3 it returns `Result<Unit>`. The semantic — "discard the value, preserve the error if any" — is unchanged; only the return type shape changes. Documented in v3 release notes as a breaking signature change.

5. **`Trellis.Asp` HTTP mapping restores `Result<Unit> → 204 No Content`.** This is **not** new design work — it is a restoration of the PR #209 (`6759e7d`, "Return HTTP 204 No Content for successful Result<Unit> operations") mechanism that was deleted by PR #394 (`dc6e028`, "Replace Result<Unit> with non-generic Result (Phase 1a PR5)") as part of the original ADR-002 §3 implementation. The original mechanism was a one-line type test inside the generic `Result<TValue>` HTTP mapper:

   ```csharp
   if (typeof(TValue) == typeof(Unit))
       return Results.NoContent();
   ```

   The same check is reintroduced at the head of the current `TrellisHttpResult<TDomain,TBody>` projector (and its `Task` / `ValueTask` extension entry points). The OpenAPI generator analogously suppresses the response schema and emits `Status204NoContent` metadata for `Result<Unit>` returns instead of `Status200OK + typeof(Body)`. PR #209's tests were comprehensive (covered both Mvc and minimal-API paths, both `Task<Result<Unit>>` and `ValueTask<Result<Unit>>` overloads); their successors port back as part of the v3 cut. The existing `TrellisEmptyResult` type that handles non-generic `Result` (`Trellis.Asp/src/Response/HttpResponseExtensions.cs:300`) is deleted along with the non-generic mapping path in §What changes #2.

6. **`IFailureFactory<TSelf>` keeps its current shape but is implemented only by `Result<TValue>` (including `Result<Unit>`).** The non-generic `Result.IFailureFactory<Result>` implementation is removed; mediator pipeline behaviors that today close over `TResponse = Result` close over `TResponse = Result<Unit>` instead.

### Wider migration blast radius (kept call-shape, breaking signature)

The following public-API surfaces currently typed `Task<Result>` / `ValueTask<Result>` migrate to `Task<Result<Unit>>` / `ValueTask<Result<Unit>>`:

- `Trellis.EntityFrameworkCore.IUnitOfWork.CommitAsync()` (`IUnitOfWork.cs:13-22`)
- `Trellis.EntityFrameworkCore.DbContextExtensions.SaveChangesResultUnitAsync(...)` (`DbContextExtensions.cs:94-120`) — name retained; signature changes
- `Trellis.Asp.HttpResponseExtensions.ToHttpResponse(this Task<Result>)` and `(this ValueTask<Result>)` (`HttpResponseExtensions.cs:48-58`)
- All mediator handler signatures across `Trellis.Mediator` and consumer code (`Task<Result>` → `Task<Result<Unit>>`)
- All `IRepositoryBase<TAggregate, TId>` write methods returning `Task<Result>` (unit-of-work pattern)

Migration is mechanical — a sed/codemod replaces `Task<Result>` with `Task<Result<Unit>>` everywhere, and the generic verb chain re-types automatically.

### What stays

1. **`IResult` and `IResult<TValue>` stay.** The polymorphic abstraction is necessary for pipeline behaviors (per ADR-002 §3 retention rationale), and removing it would force every handler-discovery code path to switch on closed types.

2. **`Result<T>` instance type stays unchanged** — same surface (`IsSuccess`, `IsFailure`, `Error`, `TryGetValue`, `TryGetError`, `Deconstruct`), same verbs, same async matrix. Only the v3 `AsUnit()` return type changes (§What changes #4).

3. **All other ROP primitives stay** — `Error` (and its closed-ADT cases), `Maybe<T>`, `Combine`, `Traverse`, `Recover`, `BindZip`, `WhenAll`, `ParallelAsync`, the LINQ surface, the `Match` terminal.

4. **Renames stay** — `Result.Ok` and `Result.Fail` (vs `Success`/`Failure`) are not reverted. The shorter names match cross-language priors and were a separate, uncontested decision.

5. **`Trellis.Unit` vs `Mediator.Unit` collision is solved by namespace, not by deletion.** `Trellis.Mediator` pipeline behaviors today are already abstract over `TResponse : IResult` / `IFailureFactory<TResponse>` (`ValidationBehavior.cs`, `AuthorizationBehavior.cs`, `ResourceAuthorizationBehavior.cs`, `ExceptionBehavior.cs`, `LoggingBehavior.cs`, `TracingBehavior.cs`) and do *not* reference `Mediator.Unit`. The collision is consumer-visible only when a consumer file imports both `Trellis` and `Mediator` namespaces — the standard `using MediatorUnit = Mediator.Unit;` (or the reverse) resolves it. No framework-internal translation layer is needed because none currently exists or is required.

### Migration path

**Single v3 breaking transition.** No additive v2.x deprecation phase is offered, because C# does not permit overloading by return type — adding a new `Result.Ok()` returning `Result<Unit>` while the existing `Result.Ok()` returns non-generic `Result` is impossible without renaming one of them, and a transient name (e.g., `Result.OkUnit()`) imposes a worse migration cost on consumers than a single mechanical cutover.

Justified by:

- Trellis is pre-1.0 alpha; there is no large external-consumer base requiring a deprecation window.
- The migration is mechanical (`Task<Result>` → `Task<Result<Unit>>`, callers of `Result.Ok()`/`Result.Fail(err)` unchanged at the source level, mediator pipeline behaviors unchanged at the source level, ASP behavior preserved by the §What changes #5 specialization).
- ADR-002 itself ships in a single v2 breaking cut (no deprecation period was offered for Unit-removal); ADR-005 reverses it in the same shape.

The v3 cut bundles:

1. The `Unit` public reintroduction.
2. The `Result` static-class replacement.
3. The non-generic-extensions deletion.
4. The `AsUnit()` return-type change.
5. The Trellis.Asp `Result<Unit>` 204 No Content specialization.
6. Mechanical updates to template, examples, samples, `trellis-api-*.md`, cookbook, and the audit gate.

### Code-reduction measurement

Net source-line reduction under Option B (v3 complete):

| Removed | File / surface | Lines |
|---|---|---:|
| `Result.cs` (instance type, all factories) | `Trellis.Core/src/Result/Result.cs` | 354 |
| `Result.Combine.cs` (partial struct contribution) | `Trellis.Core/src/Result/Result.Combine.cs` | 39 |
| Non-generic verb extensions (54 overloads) | `Trellis.Core/src/Result/Extensions/Result.NonGeneric.Extensions.cs` | 828 |
| Non-generic-targeted tests | `Trellis.Core/tests/Results/ResultTests.cs` (73) + `Results/Extensions/NonGenericResultTests.cs` (297) + `Results/Extensions/NonGenericResultTapOnFailureTests.cs` (166) | 536 |
| **Subtotal removed** | | **~1,757** |
| Added | | |
| `Unit.cs` (public surface + tests) | `Trellis.Core/src/Result/Unit.cs` (~30) + `Trellis.Core/tests/Results/UnitTests.cs` (~80) | ~110 |
| `Result.cs` (static factory class) | `Trellis.Core/src/Result/Result.cs` | ~80 |
| Trellis.Asp `Result<Unit>` 204 specialization | `Trellis.Asp/src/HttpResponseExtensions.cs` (delta) + `Trellis.Asp/src/TrellisHttpResult.cs` (delta) + tests | ~200 |
| `Result.Ok()` / `Result.Fail(Error)` factory tests | `Trellis.Core/tests/Results/ResultFactoryTests.cs` | ~80 |
| **Subtotal added** | | **~470** |
| **Net reduction (source code)** | | **~1,287 lines** |

Plus the `audit-stale-docs.ps1` ban list (16 patterns covering `Unit.Value`, `Unit.Default`, `new Unit(`, `Result<Unit>`, `Result&lt;Unit&gt;`, `Result{Unit}`, `record struct Unit`, `Unit-shaped`, `Unit result`, `Result of Unit`, `Unit Results`, `Unit support`, `void/Unit`, `non-generic non-generic`, `Unit.cs`-allowlist) is removed (~30 lines). Per-verb files (`Bind.cs`, `Map.cs`, `Tap.cs`, `Ensure.cs`, etc.) are *not* part of the reduction — spot-checks confirm they currently contain only `Result<T>` overloads with no non-generic or cross-shape variants (those are centralized in `Result.NonGeneric.Extensions.cs`).

Documentation/wording cost (recipes, `trellis-api-*.md`, cookbook callouts, XML doc references) is harder to quantify but is roughly proportional to the source reduction — every recipe that today distinguishes "use `Result.Bind(...)` here vs `Result<T>.Bind(...)` there" collapses to a single rule.

---

## Variations explored and rejected

### Option A — Re-introduce `Unit`, delete non-generic `Result` instance type, no static-class shim

`Result.Ok()` would not compile; consumers would write `Result<Unit>.Ok()` or `Result.Ok(Unit.Default)`. **Rejected:** loses the cross-language ergonomic match (Rust `Ok(())`, F# `Ok ()`, FluentResults `Result.Ok()`) and forces every consumer call site to learn that "the no-payload success has a parameter."

### Option C — Keep both as instance types with implicit conversion

`public static implicit operator Result(Result<Unit>)`. **Rejected:** ADR-001 §3.1 forbids implicit operators on `Result` for AI-correctness reasons (they look magical and AI gets the direction wrong). Adding one for `Unit` interop reintroduces exactly the footgun that section was written to prevent.

### Option D — Type alias `using Result = Result<Unit>;`

C# 12 `using` aliases on generic types exist but cannot be made global from a library — every consumer file would need `global using Result = Trellis.Result<Trellis.Unit>;` in their `GlobalUsings.cs`. **Rejected:** unfriendly to template-generated consumer projects, and the alias resolution at hover/IntelliSense is inconsistent across IDE versions.

### Option E — Defer to a hypothetical v4

Wait until cross-shape friction accumulates further, then revert in v4. **Rejected:** the lab evidence is already sufficient (4 runs, 3 models, convergent friction modes). Deferring extends the cost-paid-daily window for a benefit-paid-once decision.

---

## Open question for sign-off

1. **`AsUnit()` v3 semantic change — accept or rename?** Today `Result<T>.AsUnit()` returns non-generic `Result`. In v3 it returns `Result<Unit>`. The expression value is preserved (success-or-failure-without-payload), but any code that explicitly typed the return as `Result` will break. Recommend **accept the breaking signature change** — `AsUnit()` is the right name for the operation in both versions, and the v3 type annotation is more accurate.

2. **Audit gate behavior during transition.** Once ADR-005 is accepted, the audit-stale-docs.ps1 patterns banning `Result<Unit>` etc. become inverted (we now *want* this surface visible in current docs). Should the gate be flipped *before* implementation lands (so docs/ADRs can use the new surface during preparation), or as part of the v3 cut? Recommend flipping with the v3 cut to avoid documentation drift.

---

## What this ADR does not change

- `Error` ADT, `Maybe<T>`, `Combine`, `Traverse`, `BindZip`, `WhenAll`, `ParallelAsync`, the LINQ surface, the `Match` terminal — all unchanged.
- `Result.Ok` / `Result.Fail` factory names (the rename in ADR-002:561 stays).
- `IResult` / `IResult<TValue>` interfaces (kept per the original §What stays argument).
- `IFailureFactory<TSelf>` (kept per ADR-002:504 retention rationale).
- The `default(Result<Unit>)` invariant inherits from `default(Result<T>)` per ADR-002 §3.5.1 — same guarantees.
- The `WriteOutcome<T>` repository return shape (ADR-002:572+) is independent and unchanged.
