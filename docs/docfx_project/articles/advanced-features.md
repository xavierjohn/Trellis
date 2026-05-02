---
title: Advanced Features
package: Trellis.Core
topics: [pattern-matching, combine, try, parallel, linq, maybe]
related_api_reference: [trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Advanced Features

Once you know `Map`, `Bind`, and `Ensure`, the next question is usually: **how do I keep the same style when the workflow gets more complex?**

This article covers the Trellis features that help when you need to:

- branch cleanly at the end of a pipeline
- combine several successful values without tuple plumbing
- capture exceptions from throw-happy APIs
- run independent async work in parallel
- write more declarative pipelines with LINQ

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Decide success vs failure at the end of a pipeline | `Match(onSuccess, onFailure)` / `MatchAsync(...)` | [Pattern matching](#pattern-matching-make-the-end-of-the-pipeline-obvious) |
| Combine several `Result<T>` values without tuple plumbing | `Combine(...)` then `Bind` / `Map` / `Match` with destructured tuple | [Tuple destructuring](#tuple-destructuring-combine-values-without-manual-unpacking) |
| Bridge a throwing API into a `Result<T>` | `Result.Try(...)` / `Result.TryAsync(...)` (optionally with exception mapper) | [Exception capture](#exception-capture-keep-throwing-apis-at-the-edge) |
| Run independent async result-producing work concurrently | `Result.ParallelAsync(...).WhenAllAsync()` | [Parallel operations](#parallel-operations-run-independent-async-work-together) |
| Express sequential composition with query syntax | `from x in result1 from y in result2 select ...` | [LINQ query syntax](#linq-query-syntax-write-sequential-intent-clearly) |
| Drop the error and keep only “did it succeed?” | `result.ToMaybe()` / `ToMaybeAsync()` | [Result-to-Maybe conversion](#result-to-maybe-conversion) |

## Start Here: a Compact Example

```csharp
using Trellis;

var result = Result.Ok("Ada")
    .Combine(Result.Ok("Lovelace"))
    .Map((first, last) => $"{first} {last}")
    .Match(
        onSuccess: name => $"Hello, {name}",
        onFailure: error => $"Failed: {error.Code}");
```

That example uses three advanced ideas at once:

- tuple-aware `Combine(...)`
- tuple-aware `Map(...)`
- terminal pattern matching with `Match(...)`

## Pattern Matching: Make the End of the Pipeline Obvious

The problem pattern matching solves is simple: after a chain of operations, you need one clear place to decide what happens on success and failure.

### `Match`

```csharp
using Trellis;

var description = Result.Ok("Ada")
    .Match(
        onSuccess: value => $"Success: {value}",
        onFailure: error => $"Failure: {error.Code}");
```

### `MatchAsync`

```csharp
using Trellis;

public record User(string Id, string Name);

static Task<Result<User>> GetUserAsync(string id) =>
    Task.FromResult(id == "42"
        ? Result.Ok(new User(id, "Ada"))
        : Result.Fail<User>(new Error.NotFound(new ResourceRef("Resource", id)) { Detail = $"User {id} not found" }));

var message = await GetUserAsync("42").MatchAsync(
    onSuccess: user => $"Loaded {user.Name}",
    onFailure: error => $"Failed: {error.Code}");
```

> [!TIP]
> Use `Match(...)` when you want one final value. Use `Match(_, e => e switch { ... })` (covered in the [error-handling guide](error-handling.md)) when different error cases need different outcomes.

## Tuple Destructuring: Combine Values Without Manual Unpacking

When several validations or lookups all need to succeed, the annoying part is usually the tuple handling. Trellis solves that by letting `Bind`, `Map`, `Tap`, and `Match` destructure combined tuples for you.

### `Combine(...)` + `Bind(...)`

```csharp
using Trellis;

public record OrderDraft(string CustomerId, string Sku, int Quantity);

var draft = Result.Ok("customer-42")
    .Combine(Result.Ok("sku-123"))
    .Combine(Result.Ok(3))
    .Bind((customerId, sku, quantity) =>
        Result.Ok(new OrderDraft(customerId, sku, quantity)));
```

### `Combine(...)` + `Map(...)`

```csharp
using Trellis;

var summary = Result.Ok("Ada")
    .Combine(Result.Ok("Lovelace"))
    .Combine(Result.Ok("admin"))
    .Map((first, last, role) => $"{first} {last} ({role})");
```

### `Combine(...)` + `Match(...)`

```csharp
using Trellis;

var message = Result.Ok("Ada")
    .Combine(Result.Ok("Lovelace"))
    .Match(
        onSuccess: (first, last) => $"User: {first} {last}",
        onFailure: error => error.Detail);
```

> [!NOTE]
> Generated tuple overloads support arities from 2 through 9.

## Exception Capture: Keep Throwing APIs at the Edge

Many .NET APIs still throw exceptions for normal operational problems. Trellis gives you a clean bridge so the rest of your code can stay in `Result<T>`.

### `Result.Try(...)`

```csharp
using Trellis;

static Result<string> LoadFile(string path) =>
    Result.Try(() => File.ReadAllText(path));

var content = LoadFile("settings.json")
    .Ensure(text => !string.IsNullOrWhiteSpace(text), new Error.BadRequest("bad.request") { Detail = "settings.json is empty" });
```

### `Result.TryAsync(...)`

```csharp
using Trellis;

static Task<Result<string>> LoadFileAsync(string path) =>
    Result.TryAsync(() => File.ReadAllTextAsync(path));

var content = await LoadFileAsync("settings.json")
    .EnsureAsync(text => Task.FromResult(!string.IsNullOrWhiteSpace(text)), new Error.BadRequest("bad.request") { Detail = "settings.json is empty" });
```

### Custom exception mapping

```csharp
using Trellis;

var result = Result.Try(
    () => File.ReadAllText("settings.json"),
    exception => exception switch
    {
        FileNotFoundException => new Error.NotFound(ResourceRef.For("File", "settings.json")) { Detail = "settings.json was not found" },
        UnauthorizedAccessException => new Error.Forbidden("policy.id") { Detail = "Access denied" },
        _ => new Error.InternalServerError("fault-id") { Detail = exception.Message }
    });
```

> [!TIP]
> A good default rule is: **use exceptions at the integration boundary, then convert them once**.

## Parallel Operations: Run Independent Async Work Together

The problem `ParallelAsync(...)` solves is wasted time. If three async operations do not depend on each other, running them one after another is just latency.

### The basic pattern

```csharp
using Trellis;

public record User(string Id, string Name);
public record Order(string Id);
public record Preferences(bool DarkMode);
public record Dashboard(User User, IReadOnlyList<Order> Orders, Preferences Preferences);

static Task<Result<User>> GetUserAsync(string userId) =>
    Task.FromResult(Result.Ok(new User(userId, "Ada")));

static Task<Result<IReadOnlyList<Order>>> GetOrdersAsync(string userId) =>
    Task.FromResult(Result.Ok<IReadOnlyList<Order>>([new Order("ord-1")]));

static Task<Result<Preferences>> GetPreferencesAsync(string userId) =>
    Task.FromResult(Result.Ok(new Preferences(true)));

var combined = await Result.ParallelAsync(
    () => GetUserAsync("42"),
    () => GetOrdersAsync("42"),
    () => GetPreferencesAsync("42"))
    .WhenAllAsync();

var dashboard = combined.Bind((user, orders, preferences) =>
    Result.Ok(new Dashboard(user, orders, preferences)));
```

### What `ParallelAsync(...)` actually does

- accepts factory functions like `Func<Task<Result<T>>>`
- invokes those factories immediately
- returns a tuple of tasks
- lets `WhenAllAsync()` wait for all of them
- combines successes into a tuple result
- combines failures using normal Trellis error aggregation rules

> [!WARNING]
> `WhenAllAsync()` combines **failed `Result<T>` values**, but if one of the tasks itself faults or is canceled, that exception still escapes.

### When to use it

Use `ParallelAsync(...)` when:

- operations are independent
- operations are safe to run concurrently
- latency matters
- collecting multiple failures is useful

Avoid it when:

- step B depends on step A
- operations mutate shared state unsafely
- ordering is part of the business rule

## LINQ Query Syntax: Write Sequential Intent Clearly

LINQ syntax is handy when your pipeline is conceptually “do this, then that, then that.”

### Result LINQ

```csharp
using Trellis;

var confirmation =
    from customerId in Result.Ok("customer-42")
    from sku in Result.Ok("sku-123")
    from quantity in Result.Ok(3)
        .Ensure(value => value > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("quantity"), "validation.error") { Detail = "Quantity must be positive" })))
    select $"{customerId}:{sku}:{quantity}";
```

### `where` in LINQ

```csharp
using Trellis;

var result =
    from name in Result.Ok("Ada")
    where name.Length >= 3
    select name.ToUpperInvariant();
```

> [!NOTE]
> In a Result LINQ expression, `where` uses Trellis' generic “filtered out” failure. If you need a domain-specific message, prefer `Ensure(...)`.

### Async LINQ over `Task<Result<T>>` and `ValueTask<Result<T>>`

`Task<Result<T>>` and `ValueTask<Result<T>>` participate in query syntax directly — no need to `await` each step into a sync block.

```csharp
using Trellis;

// All-async chain — every step returns Task<Result<T>>.
var orderDto = await (
    from user  in GetUserAsync(id)         // Task<Result<User>>
    from order in GetOrderAsync(user)      // Task<Result<Order>>
    select new OrderDto(user, order));

// Mixed sync and async — sync source, async continuation.
var summary = await (
    from u in LoadCachedUser(id)           // Result<User>
    from o in FetchOrderAsync(u)           // Task<Result<Order>>
    select new Summary(u, o));

// And the reverse — async source, sync validation step.
var validated = await (
    from u in LoadUserAsync(id)            // Task<Result<User>>
    from p in ValidatePermissions(u)       // Result<Permissions>
    select new Authorized(u, p));
```

Failures short-circuit with the same semantics as sync `Bind` — the next selector is not invoked. Exceptions thrown inside async selectors propagate through `await` (matching `BindAsync` / `MapAsync`); they are not converted to `Result.Fail`. To honor cancellation, capture a `CancellationToken` from the surrounding method's closure and call `ct.ThrowIfCancellationRequested()` inside the selector.

### Maybe LINQ

`Maybe<T>` supports LINQ too, which makes optional flows much easier to read.

```csharp
using Trellis;

Maybe<string> first = Maybe.From("Ada");
Maybe<string> last = Maybe.From("Lovelace");

Maybe<string> fullName =
    from f in first
    from l in last
    select $"{f} {l}";
```

### Async LINQ over `Task<Maybe<T>>` and `ValueTask<Maybe<T>>`

`Task<Maybe<T>>` and `ValueTask<Maybe<T>>` participate in query syntax directly — no need to `await` each step into a sync block. Common shape: repository finders that return `Task<Maybe<T>>` chained without ceremony.

```csharp
using Trellis;

// All-async chain — every step returns Task<Maybe<T>>.
Task<Maybe<string>> managerEmail = (
    from user    in repo.FindAsync(userId, ct)        // Task<Maybe<User>>
    from manager in repo.FindAsync(user.ManagerId, ct) // Task<Maybe<Manager>>
    select manager.Email);

// Mixed sync and async — sync source, async continuation.
var summary =
    from u in cache.LookupUser(id)         // Maybe<User>
    from o in repo.FetchOrderAsync(u, ct)   // Task<Maybe<Order>>
    select new Summary(u, o);

// And the reverse — async source, sync filter.
var active =
    from u in repo.LoadUserAsync(id, ct)    // Task<Maybe<User>>
    where u.IsActive
    select u;
```

`None` short-circuits the chain — subsequent selectors are not invoked. Exceptions thrown inside async selectors propagate through `await`.

## Result-to-Maybe Conversion

Sometimes the only question is “did I get a value?” — not “why did it fail?” That is what `ToMaybe()` is for.

```csharp
using Trellis;

Maybe<string> cachedName = Result.Ok("Ada").ToMaybe();
Maybe<string> missingName = Result.Fail<string>(new Error.NotFound(ResourceRef.For("User")) { Detail = "User not found" }).ToMaybe();
```

And the async version:

```csharp
using Trellis;

Maybe<string> value = await Task.FromResult(Result.Ok("Ada")).ToMaybeAsync();
```

## Practical Rules of Thumb

- Use **pattern matching** to make the end of the pipeline obvious
- Use **tuple destructuring** instead of manual tuple unpacking
- Use **`Try` / `TryAsync`** at integration boundaries
- Use **`ParallelAsync(...).WhenAllAsync()`** only for truly independent async work
- Use **LINQ syntax** when it reads more clearly than nested `Bind(...)`
- Use **`ToMaybe()`** only when discarding the error is intentional

## Next Steps

- Read [Error Handling](error-handling.md) for type-specific matching and aggregation
- Read [Why Maybe?](maybe-type.md) for domain optionality and `ToResult(...)`

## Cross-references

- `Match` / `MatchAsync`, `Combine`, tuple-aware verbs, `Result.Try` / `TryAsync`, `ParallelAsync`, `WhenAllAsync`, LINQ extensions, `ToMaybe` / `ToMaybeAsync`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
