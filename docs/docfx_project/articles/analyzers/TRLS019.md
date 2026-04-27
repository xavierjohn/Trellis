# TRLS019 — Avoid `default(Result)`, `default(Result<T>)`, and `default(Maybe<T>)`

- **Severity:** Warning
- **Category:** Trellis

## What it detects

Flags explicit `default` expressions whose type is `Trellis.Result`, `Trellis.Result<T>`, or `Trellis.Maybe<T>`. Detection is operation-based (`IDefaultValueOperation`), so all surface forms are covered:

- `default(Result)`, `default(Result<int>)`, `default(Maybe<string>)` (typeof-style)
- Target-typed `default` (e.g. `return default;` in a method returning `Result<T>`)
- Null-suppressed `default!`

## Why it matters

Per [ADR-002 §3.5.1](../../adr/ADR-002-v2-redesign-plan.md), `default(Result)` and `default(Result<T>)` are typed **failures** carrying the shared `new Error.Unexpected("default_initialized")` sentinel. They are observationally equivalent to `Result.Fail(sentinel)` / `Result.Fail<T>(sentinel)`. The explicit `default` literal at a call site obscures intent — readers see `default` and assume "the zero value of this type", but the runtime semantics are *failure*.

`default(Maybe<T>)` happens to equal `Maybe<T>.None` (the type uses an `_isValueSet` discriminator that defaults to `false`). The runtime behavior is correct, but writing `default(Maybe<T>)` instead of `Maybe<T>.None` is poor style: the explicit `None` factory communicates intent.

> [!IMPORTANT]
> Never use `default(Result<T>)` to "convert" a failure short-circuit. The result is a typed failure with a sentinel, not a placeholder. Use `Result.Fail<T>(error)` with a meaningful `Error` case so downstream consumers can pattern-match on the actual problem.

## Bad examples

```csharp
// Misleading: looks like a "no-op" but actually returns Result.Fail(sentinel)
public Result<User> Lookup(Guid id)
{
    if (id == Guid.Empty)
        return default;                       // TRLS019
    return _repo.Get(id);
}

// Same problem in a typeof form
public Result Save() => default(Result);      // TRLS019

// Style violation on Maybe
public Maybe<int> First() => default(Maybe<int>);   // TRLS019
```

## Good examples

```csharp
public Result<User> Lookup(Guid id)
{
    if (id == Guid.Empty)
        return Result.Fail<User>(new Error.BadRequest("id-empty") { Detail = "id is required" });
    return _repo.Get(id);
}

public Result Save() => Result.Ok();

public Maybe<int> First() => Maybe<int>.None;
```

## Code fix available

No. The right replacement depends on intent — success vs. failure for `Result`, value vs. `None` for `Maybe`. The analyzer cannot guess which one is correct.

## Configuration

Standard Roslyn configuration applies.

```ini
dotnet_diagnostic.TRLS019.severity = none
```

For sanctioned sentinel/test-helper sites — for example, an analyzer test stub that intentionally returns `default` for shape-only methods — use a targeted suppression rather than disabling the rule globally:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Trellis", "TRLS019",
    Justification = "Shape-only test stub; returned value is never observed.")]
public static Result<T> StubFail<T>() => default;
```

```csharp
#pragma warning disable TRLS019 // Sentinel for default-state regression test.
Result<int> r = default;
#pragma warning restore TRLS019
```

> [!TIP]
> If you are migrating from v1 where `default(Result<T>)` was a silent success, search for `return default;` in `Result`-returning methods first. Most should become `Result.Ok(...)` (intentional success) or `Result.Fail<T>(...)` (intentional failure with a meaningful `Error` case).
