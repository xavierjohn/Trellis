# ADR-001: Result API Surface — `Value` / `Error` Accessors

**Status:** Accepted
**Date:** 2026-04
**Supersedes:** the prior internal-accessor + `TryGetError`/`TryGetValue` design.

---

## Decision

The public surface of `Result` and `Result<T>` exposes:

| Member | Signature | Behavior on success | Behavior on failure |
|---|---|---|---|
| `Error` | `public Error?` | returns `null` | returns the error |
| `Value` (`Result<T>` only) | `public T` | returns the value | **throws** `InvalidOperationException` |
| `IsSuccess` | `public bool` | `true` | `false` |
| `IsFailure` | `public bool` | `false` | `true` |
| `TryGetValue(out T)` | `public bool` | sets value, returns `true` | sets default, returns `false` |
| `TryGetError(out Error?)` | `public bool` | sets `null`, returns `false` | sets the error, returns `true` |
| `Deconstruct(...)` | tuple | populates value | populates error |

`Error` is the canonical pattern-match target:

```csharp
if (result.Error is { } error)
    return error switch
    {
        Error.NotFound nf => HandleNotFound(nf),
        Error.UnprocessableContent uc => HandleValidation(uc),
        _ => HandleGeneric(error),
    };
return Use(result.Value);
```

---

## Why it looks the way it does

The asymmetry between `Error?` (nullable, never throws) and `Value` (throws on failure) is **intentional**. It is forced by the C# type system **for the primary property accessor** — `Value` cannot be made safely nullable when `T` is a struct. It is not strictly forced across the *whole* API surface (see "Known footguns" below — `TryGetValue`, `Deconstruct`, and `GetValueOrDefault` all expose silent-default paths by design, for callers who opt in). The decision is therefore: pick the safest default for the most-discoverable accessor and let the opt-in helpers preserve their conventional bool-gated semantics.

The remainder of this document records every variation that was tried, why it was rejected, and what evidence supported the rejection. New contributors who want to "fix" the asymmetry should read this document first; the tradeoffs are deeper than they look.

---

## Variations explored

### V1 (original): `Value` and `Error` both public, both throw on wrong-state access

```csharp
public T Value => IsSuccess ? _value! : throw new InvalidOperationException(...);
public Error Error => IsFailure ? _error! : throw new InvalidOperationException(...);
```

This is the original CSharpFunctionalExtensions design, and what Trellis inherited.

**Pros**
- Symmetric.
- Throws loudly on misuse.

**Cons**
- Pattern-matching is awkward: you must guard with `IsFailure` *before* reading `Error`, then read it again inside the switch — no concise binding form.
- The throw is purely a defensive guard; nullable reference types (NRT) would cover the same bug class for `Error`.
- Encourages defensive `IsFailure ? Error.Code : "OK"` ternaries everywhere instead of expressive pattern matches.

**Verdict:** Replaced.

---

### V2: `Value` and `Error` made `internal`; external callers use `TryGetValue` / `TryGetError` / `Deconstruct`

```csharp
internal T Value => IsSuccess ? _value! : throw ...;
internal Error Error => IsFailure ? _error! : throw ...;
public bool TryGetValue(out T value) { ... }
public bool TryGetError(out Error error) { ... }
public void Deconstruct(out bool, out T?, out Error?) { ... }
```

The motivation was "make illegal states unrepresentable" — force callers through `TryGet`/`Deconstruct` so they can never accidentally read the wrong side.

**Pros**
- Single safe API at the boundary.
- Matches Rust/F# discipline of "you can't get the value without first proving you have one".

**Cons** (discovered after adoption)
- `TryGetError(out var e); ... e switch { ... }` is verbose for what should be a one-line pattern match.
- C# *already* has nullable reference types, which encode the same constraint at compile time without forcing `out` parameters. The discipline was double-counted.
- Combinator extensions inside the assembly had to use the `internal` getter (with the throwing semantics still in place); two parallel APIs for one concept.
- The Rust/F# precedent doesn't transfer cleanly: those languages don't have nullable reference types, so they *must* use the discriminated-union/`match` pattern. C# does have NRT and switch expressions, so the cost/benefit is different.
- Migration cost when V2 was adopted was high (~100 source files churned) for a property of the language we already had for free.

**Verdict:** Reverted. The internal-only access pattern was over-engineering motivated by F# envy. NRT plus a single clear discriminator does the same work in idiomatic C#.

---

### V3 (considered, rejected): both nullable, `Error?` is the sole discriminator

```csharp
public Error? Error => _error;     // null ⇔ success
public T?     Value => _value;     // default(T) on failure
```

Rule: *"check `Error` first; then read `Value`."* No throws anywhere.

**Pros**
- Symmetric.
- No throws.
- Clean for reference types.

**Cons (the deal-breaker)**
- For value-type `T` (e.g. `Result<int>`, `Result<Guid>`, `Result<DateTimeOffset>`), `default(T)` is `0` / `Guid.Empty` / `default` — **indistinguishable from a real successful value of zero/empty/default**.
- A caller who forgets to check `Error` first reads `0` from a failed `Result<int>` and quietly propagates a fake value through the system. No exception, no warning, no NRT diagnostic.
- NRT analysis cannot help: struct fields don't carry nullability, and `T?` for unconstrained generic `T` is just an annotation, not `Nullable<T>`.

**Verdict:** Rejected. The silent-default failure mode is a real bug class with no compile-time defense.

---

### V4 (considered, rejected): both nullable, `IsSuccess` / `IsFailure` are the sole discriminators

Same as V3 but with the rule reframed to "always check `IsFailure` first, like `Task<T>`".

```csharp
public bool   IsFailure => _error is not null;
public bool   IsSuccess => _error is null;
public Error? Error => _error;     // valid to read; meaningful when IsFailure
public T?     Value => _value;     // valid to read; meaningful when IsSuccess
```

This is the `Task<T>` model: `Task.Result`, `Task.Exception`, `Task.IsFaulted`. Read either accessor at any time; the bool flags tell you which is meaningful.

**Pros**
- Symmetric.
- No throws.
- Matches BCL precedent.

**Cons**
- Same value-type silent-default problem as V3.
- The `Task<T>` analogy is misleading: `Task.Exception` is always a reference type (`Exception`) with a clean `null` representation. Our `Value` can be a struct, where `null` is unrepresentable and `default(T)` is ambiguous.

**Verdict:** Rejected for the same reason as V3. The BCL analogy holds for `Error` (a reference type) but breaks for `Value` (which may be a struct).

---

### V5 (considered, rejected): discriminated union via inheritance

```csharp
public abstract record Result<T>
{
    private Result() { }
    public sealed record Success(T Value) : Result<T>;
    public sealed record Failure(Error Error) : Result<T>;
}

// Usage:
return result switch {
    Result<T>.Success s => Use(s.Value),
    Result<T>.Failure f => Handle(f.Error),
};
```

`Value` only exists on `Success`; `Error` only exists on `Failure`. The compiler enforces correctness by case.

**Pros**
- Bullet-proof: you cannot read the wrong side, by construction.
- Pattern matching is exhaustive at the language level.
- No throws, no nullable confusion.
- Mirrors Rust/F# directly.

**Cons (the deal-breaker)**
- Every `Result<T>` becomes a **heap allocation**. The reason is structural to the C# type system as of C# 13:
  - C# does not support struct inheritance. You cannot write `abstract record struct Result<T>` and derive `Success`/`Failure` from it. `record struct` exists, but it is sealed-by-construction — it cannot be `abstract` and cannot serve as a base type.
  - Therefore `abstract record Result<T>` (and any inheritance-based discriminated union) is necessarily a **class**. Every `new Result<T>.Success(value)` and `new Result<T>.Failure(err)` is a reference-type allocation on the GC heap.
  - The current `readonly struct Result<T>` is stack-allocated when local, and inlined into containing types when stored as a field — zero GC pressure on the hot path.
- Hot-path code (Mediator pipelines, validation loops, batch workflows, generated `Combine` overloads) constructs tens of thousands of `Result` instances per request. Converting that to per-instance heap allocations is a measurable throughput regression, and the cost compounds when the result flows through `Task<Result<T>>` and async state machines (each await would now keep the boxed result rooted).
- Existing call sites all use struct semantics (default-able, copy-by-value, no null check on the result itself). Migrating is a much bigger change than the rest of the V6 transition.

**Verdict:** Rejected on performance grounds. The allocation cost is non-negotiable for the framework's primary throughput path. Worth revisiting if and when C# adds **true sum types over value types** (discriminated unions / closed hierarchies of structs — proposal in flight as of 2026) that allow heap-free exhaustive matching.

---

### V6 (Accepted): asymmetric — `Error?` nullable, `Value` throws on failure

```csharp
public Error? Error => _error;          // null on success, never throws
public T      Value => IsSuccess
                          ? _value!
                          : throw new InvalidOperationException(
                              "Cannot access Value on a failed result. Check IsFailure or Error first.");
```

**Why it works**

| Concern | Why this design handles it |
|---|---|
| Pattern match on error | `result.Error is { } e` binds in one expression. |
| Silent default on `Value` for struct `T` | Throw makes the misuse loud. No silent corruption. |
| NRT support | `Error?` is annotated; flow analysis catches careless dereference. |
| `Value` for reference-type `T` returning null | The throw applies whether `T` is reference or value — uniform behavior. |
| Allocation | Both types remain `readonly struct`. |
| Migration cost from V2 | Minimal: just unseal `Error` and add the throw to `Value`. Existing `TryGetValue` keeps working as a convenience for callers that prefer the pattern. |

**The asymmetry is intentional and reflects the asymmetric semantics of the two cases:**

- "What error happened?" on a success has a meaningful answer: *none*. We return `null`.
- "Give me the value" on a failure has no meaningful answer. We throw.

Compare `Task<T>`: `Task.Exception` is null on success (analogous to our `Error?`); `Task.Result` blocks-or-throws on a faulted task (analogous to our throwing `Value`).

The `Task<T>` analogy is partial — it works for `Error` because `Exception` is always a reference type — but the underlying *principle* (asymmetric semantics → asymmetric API) holds for both.

---

## Known footguns (and how to mitigate them)

V6 keeps `Value`'s throw as the only structural protection. Several adjacent APIs deliberately do **not** throw, and contributors must understand the tradeoffs:

### 1. `Deconstruct` returns `default(T)` on failure

```csharp
var (isSuccess, value, error) = failedResultInt;   // value == 0, no throw
```

This is the same silent-default hazard that disqualified V3/V4 *as the primary accessor*. We accept it on `Deconstruct` because:

- The deconstruction triplet form (`isSuccess`, `value`, `error`) self-documents that the value is conditional on the bool.
- Idiomatic deconstruction usage is `var (ok, v, e) = result; if (!ok) return e!; Use(v);` — the bool gate is right there.
- The alternative (throwing inside `Deconstruct`) would make pattern-match destructuring crash on routine error-checking code.

**Guidance:** Always read the `isSuccess` (or `error`) component first. Treat `value` as meaningful only when `isSuccess` is true. The Trellis analyzers should ideally flag deconstructions where `value` is read without a preceding check on the bool — this is a candidate analyzer for a future PR.

### 2. `TryGetValue(out T)` returns `default(T)` on failure

This is conventional `Try*` semantics. Annotate with `[MaybeNullWhen(false)] out T value` for callers using NRT.

### 3. NRT flow analysis on `Error` requires `MemberNotNullWhen`

A naive caller writes:

```csharp
if (result.IsFailure)
    return result.Error.Code;   // CS8602: dereference of possibly null reference
```

Without flow attributes, `IsFailure == true` does not narrow `Error?` to non-null. The fix is on the *type*, not the caller:

```csharp
[MemberNotNullWhen(true,  nameof(Error))]
public bool IsFailure => _error is not null;

[MemberNotNullWhen(false, nameof(Error))]
public bool IsSuccess => _error is null;
```

With these annotations, the natural form `if (result.IsFailure) { use result.Error.Foo; }` flows non-null and compiles cleanly. These attributes are **part of the V6 contract** — without them, the public `Error?` property is harder to use than the old `TryGetError`.

### 4. Property patterns over `Value` are not safe guards

```csharp
// UNSAFE: throws if result is failure
var x = result switch
{
    { Value: var v } => Use(v),
    _                => null,
};
```

The compiler will happily accept this pattern, but evaluating `Value` on a failed result throws. Always pattern-match against `Error` (which is null-safe) and only touch `Value` from a branch where success is already proven:

```csharp
return result.Error is { } err
    ? Handle(err)
    : Use(result.Value);
```

This guidance belongs in the developer-facing docs and (eventually) in an analyzer rule.

---

## Empirical evidence

Before adopting V6, we ran the following experiment against
`CSharpFunctionalExtensions` (a comparable library that ships the V1 design):

> Strip the throw guards from `Result.Error`, `Result<T>.Error`, `Result<T>.Value`, `Result<T,E>.Error`, `Result<T,E>.Value`, and `UnitResult<E>.Error`. Run the test suite.

**Result:** 4220 of 4226 tests passed. The 6 failures were all *specification tests* of the throw itself:

- `SucceededResultTests.Cannot_access_Error_non_generic_version`
- `SucceededResultTests.Cannot_access_Error_generic_version`
- `SucceededResultTests.Cannot_access_Error_generic_error_version`
- `SucceededResultTests.Cannot_access_Error_UnitResult_version`
- `FailedResultTests.Cannot_access_Value_property`
- `FailedResultTests.Cannot_access_Value_property_with_a_generic_error`

**No behavioral test relied on the throw.** Every other usage path correctly checked `IsFailure`/`IsSuccess` before accessing the property and was unaffected by the change.

This confirmed:

1. The throw on `Error` adds no real safety in practice (NRT already covers it). → drop it.
2. The throw on `Value` adds no measurable obstruction either, but the *type-system reasoning* (silent default for struct `T`) shows the protection it provides cannot be replaced by NRT. → keep it.

**Caveat on the strength of this evidence.** This experiment proves that *no behavioral test in CFE depended on the throws*. It does **not** by itself prove the API is ergonomically optimal — those tests were written by people accustomed to the throwing API and may have unconsciously avoided patterns the throws prevented. The experiment is best read as supporting evidence for "the throw is not load-bearing", not as a decisive proof of design correctness. The design-correctness argument rests on the type-system reasoning above (silent default for struct `T`).

---

## Consequences

### Public API

- **Public:** `Error?` (nullable, never throws), `Value` (throws on failure for `Result<T>`), `IsSuccess`, `IsFailure`, `TryGetValue(out T)`, `TryGetError(out Error?)`, `Deconstruct(...)`.
- **Two complementary error-consumption styles, both first-class:**
  - Pattern-match on the property: `if (result.Error is { } err) { ... }` — concise, idiomatic for switch expressions and one-line guards.
  - `Try*` style: `if (result.TryGetError(out var err)) { ... }` — bool-gated, gives a non-null local without NRT acrobatics, idiomatic in imperative branches.

### Migration

- External call sites that previously couldn't reach `Value` / `Error` directly can now do so; pattern matches on `result.Error` become idiomatic.
- Internal combinator extensions inside `Trellis.Core` continue to work unchanged (the property name and shape match the previous `internal` accessors, just with the Error guard removed).

### Documentation and tooling — required follow-up

When V6 is adopted, several artifacts still describe earlier worlds and must be updated in the same change or a tightly-coupled follow-up:

- `docs/api_reference/trellis-api-core.md` — if it still documents `TryGetError` or "Error throws on success", rewrite to the V6 surface.
- `Trellis.Analyzers/src/DiagnosticDescriptors.cs` — if it still states `Result.Error` may throw, update wording. Consider new analyzer rules for the footguns listed above (deconstruction without bool check; property pattern over `Value`).
- `copilot-instructions.md` — examples that use `TryGetError` patterns must migrate to `if (result.Error is { } e)`.
- Active redesign-plan documents — align any sections that assumed the V2 internal-only model.
- `Trellis.Testing` assertions — verify that error-assertion helpers consume `Error?` cleanly.

Examples in all docs should use the `if (result.Error is { } error) ...` form for boundary code, and pattern-match exhaustively over the closed `Error` ADT.

---

## Variants considered but not separately enumerated

### V6 + retain `TryGetError`

The accepted V6 design includes `TryGetError(out Error?)` alongside the public `Error?` property. The two are deliberately complementary, not redundant:

- `Error?` is the natural fit for switch expressions and one-line pattern guards (`if (result.Error is { } e)`).
- `TryGetError` is the natural fit for imperative branches that want a non-null local binding without relying on NRT flow analysis.

An earlier draft of this ADR proposed deleting `TryGetError` for narrative purity ("there is one canonical way to consume errors"). That was reconsidered: the deletion forced caller churn (every existing `TryGetError(out var e)` site had to migrate) without any safety or expressiveness benefit, since neither pattern excludes the other. Keeping both is cheap (3 lines per type) and respects the two idiomatic C# styles.

---

## Future revisits

Re-open this ADR only if:

1. Profiling shows the `throw` on `Value` mis-access is being hit in production hot paths (would indicate a discipline problem, not an API problem).
2. C# adds true sum types / discriminated unions over value types (in flight as of 2026) that allow heap-free exhaustive matching. At that point, V5 becomes the obvious successor.
3. The `Error` type becomes a value type for some reason — at which point the `Error?` story breaks down and the entire decision needs to be re-litigated.
4. Field evidence shows the "Known footguns" section (deconstruction, `Value` property patterns) is causing real bugs at meaningful frequency; that would justify shipping analyzers or revising the surface further.

Otherwise, the asymmetric design stands.

## Addendum — Error.Conflict.Resource nullability

The `Conflict` case originally required a non-null `ResourceRef`. During the v3-alpha cascade we relaxed it to `ResourceRef?`:

```csharp
public sealed record Conflict(ResourceRef? Resource, string ReasonCode) : Error;
```

Rationale:

- RFC 9110 § 15.5.10 implies the target resource via the request URI; the response body is not required to identify it.
- Real-world 409 cases lack a specific addressable resource: stateless workflow engines, state-machine guards (e.g. `Trellis.StateMachine.StateMachineExtensions.FireResult`), library code with no aggregate context, multi-resource transactions.
- Forcing a placeholder `new ResourceRef("StateMachine", null)` would lie about meaning. `ResourceRef` is meant to identify; nullable is the honest type.
- `NotFound`/`Gone` keep their non-null `Resource` — they are definitionally about a specific resource. Same applies to `PreconditionFailed` (the precondition is evaluated against a specific resource).

Migration impact: zero for callers that already supply a `ResourceRef`; new flexibility for library/extension authors.

