# Why Maybe<T>?

C# already gives you `T?`, `Nullable<T>`, and plain old `null`. So why does Trellis add `Maybe<T>`?

Because sometimes you do not just need “a value that might be missing.” You need that optional value to be:

- explicit in your domain model
- composable in a pipeline
- easy to convert into `Result<T>` when absence becomes an error

That is the job of `Maybe<T>`.

> [!TIP]
> `Maybe<T>` is not a replacement for every nullable value in your application. It shines when optionality is part of the **domain** and needs to compose with Trellis result flows.

## Start Here: the Smallest Useful Example

```csharp
using Trellis;

Maybe<string> middleName = Maybe.From("Byron");
Maybe<string> noNickname = Maybe<string>.None;

Console.WriteLine(middleName.HasValue); // True
Console.WriteLine(noNickname.HasValue); // False

var displayName = middleName.Match(
    some => some,
    () => "(none)");
```

## The Problem `Maybe<T>` Solves

The issue with plain nullable values is not that they are bad. The issue is that they stop being expressive once the code becomes more domain-driven.

Consider an entity with an optional phone number:

```csharp
using Trellis;

public sealed record PhoneNumber(string Value);

public sealed class Customer
{
    public Maybe<PhoneNumber> Phone { get; }

    public Customer(Maybe<PhoneNumber> phone) => Phone = phone;
}
```

That says something precise:

- the customer may or may not have a phone number
- **if** there is a phone number, it is a real `PhoneNumber`
- “empty phone number” is not a separate fake concept inside the value object

That is usually clearer than pushing optionality down into the value object itself.

## When `Maybe<T>` Is Better Than `T?`

### Use `Maybe<T>` when...

| Scenario | Why `Maybe<T>` helps |
| --- | --- |
| Optional value objects | It keeps the value object valid and moves optionality to the containing model |
| Optional data in a pipeline | It composes with `Map`, `Bind`, `Where`, `Tap`, and `ToResult(...)` |
| Absence should become a domain error later | `ToResult(...)` makes that conversion explicit |
| You want equality and operators for optional values | `Maybe<T>` supports `Equals`, `==`, and `!=` |

### Use `T?` when...

| Scenario | Better choice |
| --- | --- |
| Optional primitives on DTOs | `int?`, `DateTime?`, `decimal?` |
| Optional strings or references with no pipeline needs | `string?`, `User?` |
| Interop with APIs that already use nullable reference types | `T?` |
| Performance-sensitive code where nullable semantics are enough | `T?` / `Nullable<T>` |

> [!NOTE]
> A practical rule: use `Maybe<T>` for **optional domain values** that need Trellis composition. Use `T?` for ordinary nullable data.

## Creating `Maybe<T>` Values

### Some value

```csharp
using Trellis;

Maybe<string> some = Maybe.From("Ada");
Maybe<string> alsoSome = Maybe<string>.From("Ada");
Maybe<string> implicitSome = "Ada";
```

### No value

```csharp
using Trellis;

Maybe<string> none = Maybe<string>.None;
Maybe<int> missingCount = Maybe<int>.None;
```

### Null becomes `None`

```csharp
using Trellis;

string? input = null;
Maybe<string> maybeName = Maybe.From(input);

Console.WriteLine(maybeName.HasValue); // False
```

## Reading Values Safely

The problem with optional values is not creating them. It is consuming them without littering your code with `if` statements.

### `Match`

```csharp
using Trellis;

Maybe<string> nickname = Maybe.From("Countess");

var display = nickname.Match(
    some => $"Nickname: {some}",
    () => "No nickname on file");
```

### `TryGetValue`

```csharp
using Trellis;

Maybe<int> count = Maybe.From(3);

if (count.TryGetValue(out var value))
    Console.WriteLine(value);
```

### `GetValueOrDefault`

```csharp
using Trellis;

Maybe<string> title = Maybe<string>.None;

var value = title.GetValueOrDefault("Untitled");
var lazyValue = title.GetValueOrDefault(() => "Generated title");
```

> [!WARNING]
> `Value` throws when the `Maybe<T>` is empty. Prefer `Match`, `TryGetValue`, or `GetValueOrDefault(...)`.

## Transforming Optional Values

This is where `Maybe<T>` starts to earn its keep.

### `Map`

```csharp
using Trellis;

Maybe<string> email = Maybe.From("ada@example.com");
Maybe<string> upper = email.Map(value => value.ToUpperInvariant());
```

### `Bind`

Use `Bind` when the next step also returns a `Maybe<T>`.

```csharp
using Trellis;

static Maybe<string> GetManagerEmail(string userId) =>
    userId == "42" ? Maybe.From("manager@example.com") : Maybe<string>.None;

var email = Maybe.From("42")
    .Bind(GetManagerEmail);
```

### `Where`

```csharp
using Trellis;

Maybe<int> quantity = Maybe.From(3);
Maybe<int> validQuantity = quantity.Where(value => value > 0);
```

### `Tap`

```csharp
using Trellis;

var maybeUser = Maybe.From("Ada")
    .Tap(value => Console.WriteLine($"Found {value}"));
```

### `Or`

```csharp
using Trellis;

Maybe<string> preferred = Maybe<string>.None;
Maybe<string> legal = Maybe.From("Ada Lovelace");

var name = preferred.Or(legal).Or("Unknown");
```

## Converting `Maybe<T>` to `Result<T>`

This is the most important bridge in day-to-day Trellis usage.

The problem it solves: sometimes “missing” is fine in the middle of the workflow, but becomes a real error at the boundary.

```csharp
using Trellis;

Maybe<string> maybeEmail = Maybe<string>.None;

Result<string> emailResult = maybeEmail.ToResult(
    new Error.NotFound(ResourceRef.For("Email", "primary")) { Detail = "Primary email address was not found" });
```

There is also a lazy overload when creating the error is expensive or needs runtime context.

```csharp
using Trellis;

var result = Maybe<string>.None.ToResult(
    () => new Error.NotFound(ResourceRef.For("Email", "primary")) { Detail = "Primary email address was not found" });
```

## Converting `Result<T>` to `Maybe<T>`

Sometimes you want the opposite tradeoff: “keep the value if successful, otherwise treat it as missing.”

```csharp
using Trellis;

Maybe<string> existing = Result.Ok("Ada").ToMaybe();
Maybe<string> missing = Result.Fail<string>(new Error.NotFound(ResourceRef.For("User")) { Detail = "User not found" }).ToMaybe();
```

Use this only when dropping the error is the right thing to do.

## Optional Input with `Maybe.Optional(...)`

A very common problem at system boundaries is “null is acceptable, but if a value is present, it must be valid.”

That is exactly what `Maybe.Optional(...)` is for.

### Reference input

```csharp
using Trellis;

static Result<string> NonEmpty(string value) =>
    string.IsNullOrWhiteSpace(value)
        ? Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("nickname"), "validation.error") { Detail = "Value is required" })))
        : Result.Ok(value);

string? input = "Countess";

Result<Maybe<string>> result = Maybe.Optional(input, NonEmpty);
```

### Nullable value-type input

```csharp
using Trellis;

static Result<int> Positive(int value) =>
    value > 0
        ? Result.Ok(value)
        : Result.Fail<int>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("quantity"), "validation.error") { Detail = "Value must be positive" })));

int? input = 3;

Result<Maybe<int>> result = Maybe.Optional(input, Positive);
```

What `Maybe.Optional(...)` does:

- `null` / no value -> success with `Maybe<T>.None`
- value present and valid -> success with `Maybe.From(...)`
- value present and invalid -> failure with the validation error

## Equality and Operators

This is another easy detail to miss.

> [!TIP]
> `Maybe<T>` supports `Equals`, `==`, and `!=`.

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

## LINQ Query Syntax

Optional values often read nicely in query form.

```csharp
using Trellis;

Maybe<string> first = Maybe.From("Ada");
Maybe<string> last = Maybe.From("Lovelace");

Maybe<string> fullName =
    from f in first
    from l in last
    select $"{f} {l}";
```

If any step is `None`, the whole query becomes `None`.

## Collection Helpers

Trellis also adds a few helpers that make working with collections of optional values pleasant.

### `TryFirst` and `TryLast`

```csharp
using Trellis;

var numbers = new[] { 1, 2, 3, 4 };

Maybe<int> first = numbers.TryFirst();
Maybe<int> even = numbers.TryFirst(n => n % 2 == 0);
Maybe<int> last = numbers.TryLast();
```

### `Choose`

```csharp
using Trellis;

IEnumerable<Maybe<string>> names =
[
    Maybe.From("Ada"),
    Maybe<string>.None,
    Maybe.From("Grace")
];

IEnumerable<string> values = names.Choose();
IEnumerable<int> lengths = names.Choose(name => name.Length);
```

## `Maybe<T>` and Unit Results

Sometimes the next question is: “what if my operation has no payload?”

For Trellis unit results:

- prefer `Result.Ok()` for a successful `Result`
- use `new Unit()` or `default` if you need a `Unit` value explicitly
- do **not** use `Unit.Value` — that API does not exist

```csharp
using Trellis;

Result ok = Result.Ok();
Unit unit = new Unit();
Unit alsoUnit = default;
```

## Practical Rules of Thumb

- Use `Maybe<T>` for optional **domain values**, especially value objects
- Keep optionality on the containing model, not inside a value object's invariants
- Use `ToResult(...)` when absence should become a real error
- Use `Maybe.Optional(...)` for boundary validation of optional inputs
- Use `ToMaybe()` only when you truly want to discard the error
- Prefer safe readers like `Match`, `TryGetValue`, and `GetValueOrDefault(...)`

## Next Steps

- Read [Error Handling](error-handling.md) for the errors typically paired with `ToResult(...)`
- Read [Advanced Features](advanced-features.md) for LINQ, tuple destructuring, and parallel flows
