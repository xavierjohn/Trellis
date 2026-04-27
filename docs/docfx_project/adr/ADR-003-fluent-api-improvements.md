# ADR-003 — Trellis v3 Fluent API Improvements

> **Status:** Accepted for v3 implementation.
>
> **Context:** ADR-002 defines the v2 package map and phasing. This ADR records a later v3 ergonomics pass focused on making the Trellis fluent API safer and easier for AI agents to use correctly from examples and API references.

## Context

Trellis v2 already exposes a coherent fluent API vocabulary: `Result.Ok`, `Result.Fail`, `Map`, `Bind`, `Tap`, `TapOnFailure`, `Ensure`, `Check`, `Recover`, `Combine`, `Traverse`, `ToHttpResponse(...)`, and `AddTrellis(...)`.

The remaining friction is not a lack of fluent aliases. The problems are concentrated in a few places:

- AI-facing examples sometimes use tuple `Item1` / `Item2` access instead of the existing tuple lambda overloads.
- `Trellis.Http` has a no-argument `ToResultAsync()` overload that keeps every HTTP response on the success track, so callers can accidentally turn a `404` or `409` into an `InternalServerError` later in `ReadJsonAsync`.
- Mediator handlers returning `ValueTask<Result<T>>` often use target-typed `new(...)` wrappers around synchronous result chains.
- Resource-oriented errors require verbose `new ResourceRef("Order", id)` construction.
- `Maybe<T>.Value` is visible in IntelliSense even though it throws when absent and should normally be replaced with `Match`, `TryGetValue`, `GetValueOrDefault`, or `ToResult`.
- `UseFluentValidation()` supports adapter-only registration with no assemblies, but `UseResourceAuthorization()` currently throws for the same no-assembly shape.

## Decision

### 1. Keep the fluent vocabulary small and canonical

Do not add broad aliases such as `Then`, `Pipe`, `AndThen`, `OnSuccess`, or `OnError`. Trellis will instead make the existing vocabulary more discoverable through examples, API references, and a cookbook decision matrix.

### 2. Make no-argument `Trellis.Http.ToResultAsync()` strict in v3

In v3, no-argument `ToResultAsync()` maps non-2xx HTTP responses to failure results by default. This is an intentional breaking behavior change at the existing call site.

Known HTTP statuses map to the closest Trellis `Error` case. Unknown statuses and generic 5xx responses map to `Error.InternalServerError`.

The existing defensive non-success branches in `ReadJsonAsync` and `ReadJsonMaybeAsync` remain unless a later implementation review proves them redundant. The canonical path is to map status before reading JSON.

### 3. Add a first-class optional-resource HTTP terminal

Add a terminal helper for optional HTTP reads, shaped as:

```csharp
Task<Result<Maybe<T>>> ReadJsonOrNoneOn404Async<T>(
    this Task<HttpResponseMessage> response,
    JsonTypeInfo<T> jsonTypeInfo,
    CancellationToken ct = default)
    where T : notnull;
```

`404 Not Found` maps to `Result.Ok(Maybe<T>.None)`. Other non-2xx statuses use the strict status-to-error mapping. `204`, `205`, empty body, and JSON `null` retain the existing `ReadJsonMaybeAsync` semantics and also map to `Maybe.None`.

This terminal shape avoids sentinel values and makes optional resource reads obvious at the call site.

### 4. Add result-only task adapters

Add public Core extension methods:

```csharp
Task<Result<T>> AsTask<T>(this Result<T> result);
Task<Result> AsTask(this Result result);
ValueTask<Result<T>> AsValueTask<T>(this Result<T> result);
ValueTask<Result> AsValueTask(this Result result);
```

These adapters are limited to `Result` and `Result<T>`. They do not support `Maybe<T>`.

Adapters preserve the exact result state they receive, including `default(Result)` and `default(Result<T>)` sentinel failure semantics.

### 5. Add `ResourceRef.For(...)` helpers

Add:

```csharp
ResourceRef.For(string type, object? id = null);
ResourceRef.For<TResource>(object? id = null);
```

`ResourceRef.For<TResource>(id)` uses `typeof(TResource).Name` exactly. It does not trim suffixes and does not inspect entity instances for an `Id` member. Callers pass IDs explicitly:

```csharp
ResourceRef.For<Order>(order.Id)
ResourceRef.For<Order>(orderId)
ResourceRef.For("order", id)
```

ID conversion uses invariant formatting when possible:

```csharp
id switch
{
    null => null,
    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
    _ => id.ToString(),
}
```

Generic type names are accepted as-is. Callers who need a custom resource name use the string overload.

### 6. Make `UseResourceAuthorization()` adapter-only with no assemblies

`UseResourceAuthorization()` accepts zero assemblies and registers adapter-only resource authorization behavior, matching `UseFluentValidation()`.

When assemblies are supplied, Trellis scans them for resource authorization components as it does today.

Because zero assemblies can also indicate accidental misconfiguration, the implementation should add a diagnostic path, such as startup validation or logging, when no resource authorization services are registered.

### 7. Hide `Maybe<T>.Value` from IntelliSense and review TRLS003 severity

Apply `[EditorBrowsable(EditorBrowsableState.Never)]` to `Maybe<T>.Value` as IntelliSense polish only. This does not remove the property and does not provide enforcement.

Unsafe access enforcement remains the job of analyzer rule TRLS003. For v3, escalate TRLS003 unsafe `Maybe.Value` access from warning to error if analyzer compatibility review confirms this is acceptable.

Guarded expression-tree usage remains documented and supported:

```csharp
order.SubmittedAt.HasValue && order.SubmittedAt.Value < cutoff
```

## Alternatives considered

### Keep no-argument `ToResultAsync()` pass-through

Rejected for v3. Pass-through is easy to misuse and creates poor AI-generated code because the most obvious call leaves non-success HTTP responses on the success track.

### Add a new strict method and obsolete no-argument `ToResultAsync()`

Rejected for this v3 pass after review. This would provide a compile-time migration signal, but v3 already allows behavior-changing API corrections. The chosen path intentionally makes the canonical method safe by default and documents the breaking change in the HTTP API reference.

### Infer resource IDs from entity instances

Rejected. Reflecting over `Id`, introducing marker interfaces, or overloading based on entity instances would make a simple helper harder to reason about. The generic type supplies the resource type name; callers supply the identifier explicitly.

### Hide `Maybe<T>.Value` as the primary safety mechanism

Rejected. IntelliSense visibility is not enforcement, especially within the same solution or non-IDE builds. TRLS003 remains the safety mechanism.

## Consequences

- Existing no-argument `ToResultAsync()` call sites may change behavior in v3 and must be reviewed.
- HTTP clients get a safer golden path for AI-generated code.
- Optional HTTP reads have a single clear terminal helper instead of a fragile multi-step sentinel flow.
- Mediator handlers can return `ValueTask<Result<T>>` without target-typed `new(...)` wrappers.
- Resource-oriented error construction becomes shorter and more uniform.
- `Maybe<T>.Value` remains available for guarded code but becomes less prominent in IntelliSense.
- Documentation must be updated after the API changes so examples train AI on the new canonical forms.

## Implementation notes

- Add tests before implementation for each behavior change.
- Update `trellis-api-core.md`, `trellis-api-http.md`, `trellis-api-servicedefaults.md`, `trellis-api-analyzers.md`, the cookbook, and package READMEs as needed.
- Remove or rename test-only `AsTask()` / `AsValueTask()` helpers once public adapters exist.
- Add cookbook task-lookup rows for strict HTTP, optional HTTP reads, task adapters, and `ResourceRef.For(...)`.
- Run focused package tests, analyzer tests, documentation checks, `dotnet build`, and `dotnet test` before merging.

