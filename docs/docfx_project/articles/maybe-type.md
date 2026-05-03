---
title: Maybe Type
package: Trellis.Core
topics: [maybe, optional, none, nullable, json, ef-core, validation, linq]
related_api_reference: [trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Maybe Type

`Maybe<T>` is Trellis' explicit, composable optional. It models *expected absence* in the domain so optional values flow through `Map`/`Bind`/LINQ and convert to `Result<T>` at the boundary where absence becomes an error.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Wrap a possibly-null value | `Maybe.From(value)` / `Maybe<T>.None` / implicit `T → Maybe<T>` | [Creating values](#creating-values) |
| Read a value without throwing | `Match`, `TryGetValue`, `GetValueOrDefault` | [Reading values](#reading-values) |
| Transform an optional value | `Map`, `Bind`, `Where`, `Tap`, `Or` | [Transforming values](#transforming-values) |
| Compose multiple optionals | LINQ `from … from … select` (`Select`/`SelectMany`) | [LINQ query syntax](#linq-query-syntax) |
| Pull the first/only present item out of a sequence | `TryFirst`, `TryLast`, `Choose` | [Collections](#collections) |
| Validate an optional input ("null OK; if present, must be valid") | `Maybe.Optional(value, validate)` | [Boundary validation](#boundary-validation) |
| Enforce cross-field invariants over `Maybe<T>` properties | `MaybeInvariant.AllOrNone` / `Requires` / `MutuallyExclusive` / `ExactlyOne` / `AtLeastOne` | [Multi-field invariants](#multi-field-invariants) |
| Convert `Maybe<T>` ↔ `Result<T>` | `ToResult(error)` / `ToResultAsync(...)` / `Result<T>.ToMaybe()` | [Bridging to Result](#bridging-to-result) |
| Convert `Maybe<T>` ↔ nullable | `AsMaybe()` / `AsNullable()` | [Interop with nullable](#interop-with-nullable) |
| Serialize `Maybe<scalar value object>` over JSON | `MaybeScalarValueJsonConverter` (Trellis.Asp) | [JSON serialization](#json-serialization) |
| Query a `Maybe<T>` column in EF Core | `MaybeQueryableExtensions` / `AddTrellisInterceptors()` (Trellis.EntityFrameworkCore) | [EF Core integration](#ef-core-integration) |

## Use this guide when

- You want optionality to be **part of the domain model** rather than a per-call nullability annotation.
- You need optional values that compose with `Result<T>` pipelines and tolerate `Bind`/`Map`/`Match`/LINQ.
- A value is allowed to be missing partway through a workflow but must become a typed `Error` at the boundary.
- You map domain models to EF Core columns or JSON payloads where "present vs. absent" is meaningful.

For ordinary nullable primitives on a DTO or for plain nullable interop, prefer `T?` / `Nullable<T>`.

## Surface at a glance

`Maybe<T>` is a `readonly struct` defined in `Trellis.Core`. `default(Maybe<T>)` equals `Maybe<T>.None` (analyzer **TRLS019** flags explicit `default(Maybe<T>)` and recommends `Maybe<T>.None`).

| Member | Kind | Purpose |
|---|---|---|
| `Maybe.From<T>(T? value) where T : notnull` | static factory (non-generic class) | Wraps a nullable; `null` → `None`. |
| `Maybe<T>.From(T? value)` | static factory (generic struct) | Same as above with explicit `T`. |
| `Maybe<T>.None` | static property | The empty instance. |
| `implicit operator Maybe<T>(T value)` | conversion | Lets a bare `T` flow into a `Maybe<T>` slot. |
| `HasValue` / `HasNoValue` | properties | Presence flags. |
| `Value` | property | Throws on `None`. Hidden from IntelliSense; analyzer **TRLS003** flags unguarded reads. Use `Match`/`TryGetValue`/`GetValueOrDefault`. |
| `GetValueOrThrow(string?)` | method | Throwing extractor with optional message. |
| `GetValueOrDefault(T)` / `GetValueOrDefault(Func<T>)` | methods | Non-throwing extractors (eager / deferred). |
| `TryGetValue(out T)` | method | TryParse-style extractor. |
| `Map<TResult>(Func<T, TResult>)` | method | Transform present value. |
| `Bind<TResult>(Func<T, Maybe<TResult>>)` | method | Flat-map. |
| `Match<TResult>(Func<T, TResult> some, Func<TResult> none)` | method | Branch on presence. |
| `Where(Func<T, bool>)` | method | Keep value only if predicate holds. |
| `Tap(Action<T>)` | method | Side effect on present value. |
| `Or(T)` / `Or(Func<T>)` / `Or(Maybe<T>)` / `Or(Func<Maybe<T>>)` | methods | Fallback (eager / deferred / maybe / deferred maybe). |
| `Equals(Maybe<T>)` / `Equals(T?)` / `Equals(object?)` / `==` / `!=` (vs `T`, `Maybe<T>`) | equality | Structural equality including raw values. Use `Equals(object?)` for boxed comparisons. |
| `MaybeExtensions.AsMaybe<T>` / `AsNullable<T>` | extension methods | `T?` (struct or class) → `Maybe<T>` and back. |
| `MaybeExtensions.ToResult<T>(Error)` / `ToResult(Func<Error>)` | extension methods | Bridge to `Result<T>`. |
| `MaybeExtensionsAsync.ToResultAsync` / `MatchAsync` | extension methods | `Task`/`ValueTask` overloads of `ToResult` and `Match`. |
| `MaybeChooseExtensions.Choose` | extension methods | Flatten `IEnumerable<Maybe<T>>` (with optional projector). |
| `MaybeCollectionExtensions.TryFirst` / `TryLast` (with optional predicate) | extension methods | First/last hit as `Maybe<T>`. |
| `MaybeLinqExtensions.Select` / `SelectMany` | extension methods | Enables LINQ query syntax. |
| `Maybe.Optional<TIn, TOut>(TIn?, Func<TIn, Result<TOut>>)` | static method (class & struct overloads) | "Null OK; if present, validate." Returns `Result<Maybe<TOut>>`. |
| `MaybeInvariant.AllOrNone` / `Requires` / `MutuallyExclusive` / `ExactlyOne` / `AtLeastOne` | static methods | Cross-field invariants returning `Result<Unit>`. |
| `Result<T>.ToMaybe()` / `ToMaybeAsync()` | extension methods (Result side) | Failure → `None`. |

Full signatures: [trellis-api-core.md](../api_reference/trellis-api-core.md).

## Installation

```bash
dotnet add package Trellis.Core
```

JSON converters for `Maybe<scalar value object>` ship in `Trellis.Asp`. EF Core query/update helpers ship in `Trellis.EntityFrameworkCore`.

## Quick start

```csharp
using Trellis;

Maybe<string> middleName = Maybe.From("Byron");
Maybe<string> noNickname = Maybe<string>.None;

string display = middleName.Match(
    some: name => name,
    none: () => "(none)");

Result<string> required = noNickname.ToResult(
    new Error.NotFound(ResourceRef.For("Nickname", "primary"))
    {
        Detail = "A nickname is required for the marketing payload.",
    });
```

## Creating values

| Form | Result |
|---|---|
| `Maybe.From(value)` | `Some(value)` if non-null, otherwise `None`. |
| `Maybe<T>.From(value)` | Same, with explicit type. |
| `Maybe<T>.None` | The empty instance (preferred over `default(Maybe<T>)`). |
| `Maybe<T> m = value;` | Implicit conversion from `T`. Useful in expression-bodied returns. |
| `Maybe.From((string?)null)` | `None`. |
| `value.AsMaybe()` (`T : class` or `T : struct` via `T?`) | Extension form — handy in LINQ. |

```csharp
using Trellis;

Maybe<string> some         = Maybe.From("Ada");
Maybe<string> alsoSome     = Maybe<string>.From("Ada");
Maybe<string> implicitSome = "Ada";
Maybe<int>    missingCount = Maybe<int>.None;

string? input = null;
Maybe<string> fromNullable = Maybe.From(input);  // None
```

## Reading values

`Value` exists but throws on `None` and is gated by analyzer **TRLS003**. Prefer the safe readers below.

| API | When to use |
|---|---|
| `Match(some, none)` | You always want a result, branched on presence. |
| `TryGetValue(out var v)` | Imperative early-return style. |
| `GetValueOrDefault(fallback)` | Eager fallback when constructing it is cheap. |
| `GetValueOrDefault(() => fallback)` | Deferred fallback (expensive default, allocations). |
| `GetValueOrThrow("msg")` | Genuinely unexpected absence (programmer error). |

```csharp
using Trellis;

Maybe<int> count = Maybe.From(3);

int total = count.Match(
    some: n => n,
    none: () => 0);

if (count.TryGetValue(out var n))
    Console.WriteLine(n);

string title = Maybe<string>.None.GetValueOrDefault("Untitled");
string lazy  = Maybe<string>.None.GetValueOrDefault(() => $"generated-{Guid.NewGuid():N}");
```

## Transforming values

| Operator | Signature | Behaviour |
|---|---|---|
| `Map` | `Maybe<T>.Map<TResult>(Func<T, TResult>)` | Project the inner value; `None` propagates. A selector that returns `null` collapses to `None` (Maybe never holds `null`). |
| `Bind` | `Maybe<T>.Bind<TResult>(Func<T, Maybe<TResult>>)` | Flat-map for the next step that itself returns `Maybe<TResult>`. |
| `Where` | `Maybe<T>.Where(Func<T, bool>)` | Drop the value to `None` when the predicate is `false`. |
| `Tap` | `Maybe<T>.Tap(Action<T>)` | Run a side effect on the present value, return `this` unchanged. |
| `Or` | `Or(T)` / `Or(Func<T>)` / `Or(Maybe<T>)` / `Or(Func<Maybe<T>>)` | First-non-none fallback chain. |

```csharp
using Trellis;

static Maybe<string> GetManagerEmail(string userId) =>
    userId == "42" ? Maybe.From("manager@example.com") : Maybe<string>.None;

Maybe<string> upperEmail = Maybe.From("ada@example.com")
    .Map(value => value.ToUpperInvariant());

Maybe<string> chained = Maybe.From("42")
    .Bind(GetManagerEmail);

Maybe<int> validQuantity = Maybe.From(3).Where(v => v > 0);

Maybe<string> name = Maybe<string>.None
    .Or(Maybe.From("Ada Lovelace"))
    .Or("Unknown");
```

## Equality

`Maybe<T>` implements `IEquatable<Maybe<T>>` and `IEquatable<T>`, with `==` / `!=` overloads against `T` and `Maybe<T>`. Two `None` values are equal; two `Some` values are equal iff their inner values are. `Maybe<T>.None.Equals((T?)null)` returns `true` — the absence of a value converges with the canonical `null` sentinel; if you need to distinguish the two at a boundary, use `HasValue` / `HasNoValue` rather than `==`. To compare against a boxed value use the `Equals(object?)` instance method (the `(Maybe<T>, object?)` operator overload was removed because it silently absorbed cross-type comparisons such as `Maybe<int> == "literal"`).

```csharp
using Trellis;

Maybe<int> some = Maybe.From(42);
Maybe<int> none = Maybe<int>.None;

Console.WriteLine(some == 42);                  // True
Console.WriteLine(some == Maybe.From(42));      // True
Console.WriteLine(some != 0);                   // True
Console.WriteLine(none == Maybe<int>.None);     // True
Console.WriteLine(some.Equals(Maybe.From(42))); // True
```

## LINQ query syntax

`MaybeLinqExtensions` adds `Select` and `SelectMany`, so multiple `Maybe<T>` values compose via query syntax. Any `None` in the chain short-circuits the entire query to `None`.

```csharp
using Trellis;

Maybe<string> first = Maybe.From("Ada");
Maybe<string> last  = Maybe.From("Lovelace");

Maybe<string> fullName =
    from f in first
    from l in last
    select $"{f} {l}";
```

## Collections

| Helper | Returns | Notes |
|---|---|---|
| `IEnumerable<T>.TryFirst()` | `Maybe<T>` | First element, or `None` if empty. |
| `IEnumerable<T>.TryFirst(predicate)` | `Maybe<T>` | First match, or `None`. |
| `IEnumerable<T>.TryLast()` / `TryLast(predicate)` | `Maybe<T>` | Last element / last match. |
| `IEnumerable<Maybe<T>>.Choose()` | `IEnumerable<T>` | Drops `None`s, yields inner values. |
| `IEnumerable<Maybe<T>>.Choose(selector)` | `IEnumerable<TResult>` | Drops `None`s, projects inner values. |

```csharp
using Trellis;

var numbers = new[] { 1, 2, 3, 4 };
Maybe<int> firstEven = numbers.TryFirst(n => n % 2 == 0);

IEnumerable<Maybe<string>> names =
[
    Maybe.From("Ada"),
    Maybe<string>.None,
    Maybe.From("Grace"),
];

IEnumerable<string> defined = names.Choose();
IEnumerable<int>    lengths = names.Choose(name => name.Length);
```

## Boundary validation

`Maybe.Optional` is the canonical "null is acceptable, but if a value is present it must be valid" primitive. It returns `Result<Maybe<TOut>>`:

| Input | Result |
|---|---|
| `null` (or `Nullable<T>` without value) | `Ok(Maybe<TOut>.None)` |
| Value present, validator returns `Ok(v)` | `Ok(Maybe.From(v))` |
| Value present, validator returns `Fail(e)` | `Fail(e)` |

Both reference (`where TIn : class`) and value-type (`where TIn : struct`) overloads exist.

```csharp
using Trellis;

static Result<string> NonEmpty(string value) =>
    string.IsNullOrWhiteSpace(value)
        ? Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("nickname"), "validation.error")
            {
                Detail = "Value is required",
            })))
        : Result.Ok(value);

string? input = "Countess";
Result<Maybe<string>> result = Maybe.Optional(input, NonEmpty);
```

## Multi-field invariants

`MaybeInvariant` enforces shape rules across several `Maybe<T>` fields and returns `Result<Unit>`. Failures are collected as `Error.UnprocessableContent` with one `FieldViolation` per offending field; field paths are normalized via `InputPointer.ForProperty(name)`.

| Helper | Rule | Available arities |
|---|---|---|
| `AllOrNone` | All fields present, or all absent. | 2, 3, 4 |
| `Requires` | If `source` is present, `required` must be too. | 2 |
| `MutuallyExclusive` | At most one field present. | 2, 3, 4 |
| `ExactlyOne` | Exactly one field present. | 2, 3, 4 |
| `AtLeastOne` | At least one field present. | 2, 3, 4 |

```csharp
using Trellis;

Result<Unit> shape = MaybeInvariant.ExactlyOne(
    command.Email, command.Phone, "email", "phone");
```

## Bridging to Result

The most important conversion in day-to-day Trellis usage: turn "missing" into a typed `Error` at the boundary.

| Direction | API | Notes |
|---|---|---|
| `Maybe<T>` → `Result<T>` (eager error) | `maybe.ToResult(error)` | Allocates the `Error` even on success. |
| `Maybe<T>` → `Result<T>` (lazy error) | `maybe.ToResult(() => error)` | Use when error construction is expensive or context-dependent. |
| `Task<Maybe<T>>` → `Task<Result<T>>` | `maybeTask.ToResultAsync(error)` / `ToResultAsync(() => error)` | `Task` and `ValueTask` overloads available. |
| `Task<Maybe<T>>` → branch | `maybeTask.MatchAsync(some, none)` | Sync- and async-delegate overloads. |
| `Result<T>` → `Maybe<T>` | `result.ToMaybe()` / `resultTask.ToMaybeAsync()` | Failures collapse to `None`; the error is dropped. |

```csharp
using Trellis;

Maybe<string> maybeEmail = Maybe<string>.None;

Result<string> required = maybeEmail.ToResult(
    new Error.NotFound(ResourceRef.For("Email", "primary"))
    {
        Detail = "Primary email address was not found",
    });

Result<string> requiredLazy = Maybe<string>.None.ToResult(
    () => new Error.NotFound(ResourceRef.For("Email", "primary"))
    {
        Detail = "Primary email address was not found",
    });

Maybe<string> backToMaybe = Result.Ok("Ada").ToMaybe();
```

Use `ToMaybe()` only when discarding the error is genuinely correct.

## Interop with nullable

`MaybeExtensions` provides round-trip helpers between `Maybe<T>` and the BCL nullable forms.

| API | Receiver | Returns | Notes |
|---|---|---|---|
| `AsMaybe<T>(this T?)` `where T : struct` | nullable value type | `Maybe<T>` | `null` → `None`. |
| `AsMaybe<T>(this T)` `where T : class` | nullable reference | `Maybe<T>` | `null` → `None`. |
| `AsNullable<T>(in this Maybe<T>)` `where T : struct` | `Maybe<T>` | `T?` | `None` → `null`. |

```csharp
using Trellis;

int? raw = 7;
Maybe<int> maybeQty = raw.AsMaybe();

string? maybeName = "Ada";
Maybe<string> nameMaybe = maybeName.AsMaybe();

int? back = maybeQty.AsNullable();
```

## JSON serialization

`Trellis.Asp` ships `MaybeScalarValueJsonConverter<TValue, TPrimitive>` and a `MaybeScalarValueJsonConverterFactory` that registers it for any `Maybe<TScalar>` where `TScalar` is a Trellis scalar value object. Absence serializes as JSON `null`; deserialization participates in the validation-error scope used by `ScalarValueValidationFilter` / `ScalarValueValidationEndpointFilter`.

There is no built-in System.Text.Json converter for arbitrary `Maybe<T>` in `Trellis.Core` itself — pair with a scalar value object or supply your own converter.

See [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md) for converter signatures and validation wiring.

## EF Core integration

`Trellis.EntityFrameworkCore` discovers `Maybe<T>` properties via `MaybeConvention` and maps them to a generated `_camelCase` storage member, so direct `.Value` / `.HasValue` / `GetValueOrDefault(d)` calls in LINQ predicates do not translate by default. Two supported paths:

| Approach | API | Use when |
|---|---|---|
| Explicit query helpers | `MaybeQueryableExtensions.WhereHasValue` / `WhereNone` / `WhereEquals` / `WhereLessThan(OrEqual)` / `WhereGreaterThan(OrEqual)` plus `OrderByMaybe` / `ThenByMaybe(Descending)` | You want predictable SQL without registering interceptors. |
| Interceptor rewriting | `optionsBuilder.AddTrellisInterceptors()` (registers `MaybeQueryInterceptor`) | You want natural `.HasValue` / `.Value` / `GetValueOrDefault(d)` syntax to translate. |
| Repo lookups returning optionals | `IQueryable<T>.FirstOrDefaultMaybeAsync` / `SingleOrDefaultMaybeAsync` | Replace EF's `null` returns with `Maybe<T>.None`. |
| Indexing a `Maybe<T>` column | `entityTypeBuilder.HasTrellisIndex(x => x.M)` | Avoids analyzer **TRLS016** by targeting the storage member. |
| `ExecuteUpdate` over `Maybe<T>` | `MaybeUpdateExtensions.SetMaybeValue(...)` / `SetMaybeNone(...)` | Set Some / clear via bulk update. |

Without one of these, raw `.Value` or `GetValueOrDefault(sentinel)` in EF queries either throws at materialization or fails to translate. Analyzer **TRLS013** flags `.Value` in Select-family LINQ projections (in-memory).

See [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md) for full signatures and conventions.

## Composition

Once `Maybe<T>` and `Result<T>` are in the same pipeline they compose via `ToResult` / `ToMaybe` and the standard result combinators:

```csharp
using Trellis;

public sealed record Customer(string Id, Maybe<string> Email);

static Result<Customer> Load(string id) => /* ... */ Result.Ok(new Customer(id, Maybe.From("ada@example.com")));
static Task<Result<Unit>> SendWelcomeAsync(string email, CancellationToken ct) => /* ... */ Task.FromResult(Result.Ok());

public Task<Result<Unit>> WelcomeAsync(string id, CancellationToken ct) =>
    Load(id)
        .Bind(customer => customer.Email.ToResult(
            new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("email"), "validation.error")
                {
                    Detail = "Customer has no email address on file.",
                }))))
        .BindAsync((email, token) => SendWelcomeAsync(email, token), ct);
```

For the "no payload" success case, prefer `Result<Unit>` (`Result.Ok()` returns `Result<Unit>`); `Maybe<T>` is for *values*, not for "success/failure".

## Practical guidance

- Use `Maybe<T>` for optional **domain values**, especially value objects; keep optionality on the containing entity rather than encoded inside a value object's invariants.
- Prefer `Maybe<T>.None` over `default(Maybe<T>)` (analyzer **TRLS019**).
- Prefer safe readers (`Match`, `TryGetValue`, `GetValueOrDefault`) over `Value` (analyzer **TRLS003**).
- Use `Maybe.Optional` at boundaries when "null OK; if present, must validate."
- Use `MaybeInvariant.*` for cross-field shape rules instead of bespoke `if`/`else` ladders.
- Use `ToResult` to lift absence into a typed `Error` at the boundary; use `ToMaybe()` only when discarding the error is genuinely correct.
- For EF Core, prefer the `MaybeQueryableExtensions.WhereXxx` helpers; otherwise register `AddTrellisInterceptors()` to make natural syntax translate.
- For ordinary nullable primitives on DTOs, keep using `T?` — `Maybe<T>` is not a blanket replacement for `Nullable<T>`.

## Cross-references

- API surface: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Errors typically paired with `ToResult(...)`: [Error Handling](error-handling.md)
- LINQ query syntax, tuple destructuring, parallel flows: [Advanced Features](advanced-features.md)
- JSON converter for `Maybe<scalar value object>`: [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
- EF Core query/update/index helpers and interceptors: [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md)
- Cookbook recipes that use `Maybe<T>`: [`trellis-api-cookbook.md`](../api_reference/trellis-api-cookbook.md)
