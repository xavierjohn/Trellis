# Why Maybe&lt;T&gt;?

C# already has `Nullable<T>` for value types and nullable reference types (`T?`) for reference types. So why does Trellis include a `Maybe<T>` type?

This article makes an honest case: `Maybe<T>` solves specific problems in domain modeling and functional pipelines that C#'s built-in nullability can't address — but it's not a universal replacement for `null`.

```csharp
using Trellis;
```

## Table of Contents

- [The Problem](#the-problem)
- [What C# Already Provides](#what-c-already-provides)
- [Where Maybe Wins](#where-maybe-wins)
  - [Pipeline Composition](#1-pipeline-composition)
  - [Optional Value Objects](#2-optional-value-objects)
  - [ASP.NET Boundary Integration](#3-aspnet-boundary-integration)
  - [Where Nullability Lives](#4-where-nullability-lives)
- [When to Use T? Instead](#when-to-use-t-instead)
- [Quick Reference](#quick-reference)
- [API Overview](#api-overview)
- [Next Steps](#next-steps)

## The Problem

In domain-driven design, you frequently model properties that are *intentionally optional* — a customer's phone number, a secondary email address, a middle name. The question is: how do you express "this value may or may not be present" in a way that's type-safe, composable, and semantically clear?

```csharp
public class Customer : Entity<CustomerId>
{
    public CustomerName Name { get; }
    public PhoneNumber? Phone { get; }  // Is this good enough?
}
```

For simple cases, `PhoneNumber?` works fine. But as your domain grows, you'll run into three gaps that C#'s built-in nullability can't fill.

## What C# Already Provides

C# has two nullability mechanisms, and it's important to understand what they do well:

| Mechanism | Works With | Runtime Type? | Enforcement |
|-----------|-----------|---------------|-------------|
| `Nullable<T>` (`int?`, `DateTime?`) | Value types only | Yes — real wrapper struct | Full runtime enforcement |
| `T?` (nullable reference types) | Reference types only | No — compiler annotation only | Warnings only (can be ignored) |

**For most code, these are sufficient.** If you're writing a standard web API or working with primitives, `T?` is the idiomatic C# choice. Trellis doesn't suggest otherwise.

## Where Maybe Wins

`Maybe<T>` addresses specific gaps that matter when you're building domain models with Railway Oriented Programming.

### 1. Pipeline Composition

This is the strongest justification. `T?` gives you `?.` and `??`, but these are limited to simple null-coalescing. They can't inject validation, transformation, or error handling into a chain.

**With `T?` — imperative null checks:**

```csharp
PhoneNumber? phone = FindPhone(customerId);
if (phone is null)
    return Result.Failure<string>(Error.NotFound("No phone on file"));

var formatted = FormatForDisplay(phone);
var result = await SendSmsAsync(formatted);
```

**With `Maybe<T>` — composable pipeline:**

```csharp
var result = FindPhone(customerId)           // returns Maybe<PhoneNumber>
    .ToResult(Error.NotFound("No phone on file"))
    .Map(phone => FormatForDisplay(phone))
    .Bind(formatted => SendSmsAsync(formatted));
```

The `Maybe<T>` version plugs directly into the same `Result<T>` pipeline you're already using for validation and error handling. The `T?` version forces you out of the pipeline and into imperative code.

**The bridge method — `ToResult`:**

```csharp
// Maybe → Result: absence becomes a domain error
Maybe<User> maybeUser = FindUserInCache(id);
Result<User> result = maybeUser.ToResult(Error.NotFound($"User {id} not found"));

// From here, chain with Bind, Map, Ensure, Tap — the full ROP toolkit
```

### 2. Optional Value Objects

Value objects are non-null by design — a `PhoneNumber` is always valid, an `EmailAddress` always contains a well-formed address. The question becomes: how do you express "this entity may or may not have a phone number"?

**The invariant problem:**

A `PhoneNumber` protects its invariants — it cannot be empty, null, or invalid. An "empty phone number" is a contradiction. So **nullability must live on the entity, not inside the value object:**

```csharp
// ✅ Nullability on the entity — PhoneNumber is always valid
public class Customer : Entity<CustomerId>
{
    public Maybe<PhoneNumber> Phone { get; }
}

// ❌ Nullability inside the value object — breaks invariants
public class PhoneNumber : ValueObject
{
    public string? Number { get; }  // What does an empty phone number mean?
}
```

`Maybe<PhoneNumber>` makes the intent explicit: the customer may or may not have a phone number, but if they do, it's always valid.

**Context-dependent optionality:**

The same value object can be required in one entity and optional in another:

```csharp
public class Customer : Entity<CustomerId>
{
    public Maybe<PhoneNumber> Phone { get; }  // Optional for customers
}

public class Employee : Entity<EmployeeId>
{
    public PhoneNumber Phone { get; }         // Required for employees
}
```

**The `notnull` constraint prevents misuse:**

```csharp
Maybe<PhoneNumber> phone;     // ✅ Valid
Maybe<int> count;             // ✅ Valid
Maybe<string?> name;          // ❌ Compile error — T must be notnull
Maybe<Maybe<string>> nested;  // ❌ Analyzer TRLS011 warns against double wrapping
```

### 3. ASP.NET Boundary Integration

At the API boundary, `null` is ambiguous. Did the client omit the field, or did they explicitly send `null`? For value objects, there's a third case: the field was present but invalid.

Trellis provides automatic integration for `Maybe<T>` in DTOs:

```csharp
public record UpdateCustomerRequest(
    FirstName Name,
    Maybe<PhoneNumber> Phone    // Three possible states handled automatically
);
```

| JSON Input | Deserialized As | Behavior |
|-----------|----------------|----------|
| `"phone": "555-1234"` | `Maybe.From(PhoneNumber)` | Validated and wrapped |
| `"phone": null` or field absent | `Maybe<PhoneNumber>.None` | Treated as intentionally empty |
| `"phone": "invalid!"` | Validation error | Returns `400` with field-level error |

This is handled automatically by:
- `MaybeScalarValueJsonConverter` — JSON deserialization
- `MaybeModelBinder` — MVC model binding
- `MaybeSuppressChildValidationMetadataProvider` — prevents MVC validation crashes

All registered with a single call to `AddScalarValueValidation()`. With `T?`, you'd need custom converters to distinguish "absent" from "invalid."

### 4. Where Nullability Lives

When validating optional input from users or APIs, you often need to handle "null means skip, non-null means validate." `Maybe.Optional` solves this cleanly:

```csharp
// Optional input: null is fine, but if provided, it must be valid
string? phoneInput = request.Phone;
string? websiteInput = request.Website;

var result = FirstName.TryCreate(request.FirstName)
    .Combine(LastName.TryCreate(request.LastName))
    .Combine(Maybe.Optional(phoneInput, PhoneNumber.TryCreate))
    .Combine(Maybe.Optional(websiteInput, Url.TryCreate))
    .Bind((first, last, phone, website) =>
        Customer.TryCreate(first, last, phone, website));
```

`Maybe.Optional` encodes the rule: **null input → `Maybe<T>.None` (success), non-null input → validate and wrap in `Maybe.From`**, invalid input → propagate the validation error. This composes naturally with `Combine` and keeps optional fields in the same pipeline as required fields.

## When to Use T? Instead

`Maybe<T>` is not always the right choice. Use `T?` for:

| Scenario | Use | Example |
|----------|-----|---------|
| Optional primitives on entities | `T?` | `DateTime? CancelledAt` |
| Optional strings | `T?` | `string? MiddleName` |
| Optional primitives in DTOs | `T?` | `int? PageSize` |
| Methods consumed directly (not piped) | `T?` | `User? FindUser(int id)` |
| Performance-critical hot paths | `T?` | Zero allocation vs. struct copy |

**The rule of thumb:**

> Use `Maybe<T>` for optional value objects and when composing with `Result<T>` pipelines. Use `T?` for everything else.

## Quick Reference

| I want to... | Use |
|-------------|-----|
| Express "this value object is optional" | `Maybe<PhoneNumber>` |
| Feed optionality into an ROP pipeline | `maybe.ToResult(error).Bind(...)` |
| Validate optional API input | `Maybe.Optional(input, TryCreate)` |
| Express "this primitive is optional" | `DateTime? CancelledAt` |
| Return "not found" from a query | `Maybe<T>` if piped, `T?` if consumed directly |
| Transform an optional value | `maybe.Map(x => x.Format())` |
| Pattern match on presence | `maybe.Match(x => ..., () => ...)` |

## API Overview

### Creating Maybe Values

```csharp
Maybe<PhoneNumber> some = Maybe.From(phoneNumber);            // Wrap a value (type inferred)
Maybe<PhoneNumber> same = Maybe<PhoneNumber>.From(phoneNumber); // Wrap a value (explicit type)
Maybe<PhoneNumber> none = Maybe<PhoneNumber>.None;            // No value
Maybe<string> greeting = "hello";                              // Implicit conversion
```

### Checking and Extracting

```csharp
if (maybe.HasValue)
    Console.WriteLine(maybe.Value);

if (maybe.TryGetValue(out var value))
    Console.WriteLine(value);

var fallback = maybe.GetValueOrDefault(PhoneNumber.Create("000-0000"));
```

### Transforming

```csharp
// Map — transform the inner value
Maybe<string> formatted = maybePhone.Map(p => p.ToString());

// Match — handle both cases
string display = maybePhone.Match(
    p => $"Phone: {p}",
    () => "No phone on file"
);
```

### Bridging to Result

```csharp
// Convert to Result for ROP pipeline
Result<PhoneNumber> result = maybePhone
    .ToResult(Error.NotFound("No phone on file"));

// Lazy error creation
Result<PhoneNumber> result = maybePhone
    .ToResult(() => Error.NotFound($"Phone not found for {customerId}"));
```

### Handling Optional Input

```csharp
// Null → Maybe.None (success), non-null → validate and wrap
Result<Maybe<PhoneNumber>> result = Maybe.Optional(input, PhoneNumber.TryCreate);
```

### LINQ Query Syntax

Compose multiple optional values using C# query syntax:

```csharp
Maybe<string> fullName =
    from first in firstName
    from last in lastName
    select $"{first} {last}";

// Chain optional lookups — any None short-circuits to None
Maybe<Email> managerEmail =
    from user in users.FindById(userId)
    from manager in users.FindById(user.ManagerId)
    from email in manager.Email
    select email;
```

### Bridging from Result (ToMaybe)

```csharp
// Convert Result to Maybe — success→Some, failure→None (error discarded)
Maybe<Avatar> avatar = avatarService.GetByUserId(userId).ToMaybe();

// Async variant
Maybe<Avatar> avatar = await avatarService.GetByUserId(userId).ToMaybeAsync();
```

## Next Steps

- Learn the [Basics](basics.md) of Railway Oriented Programming
- See [Entity Framework Core Integration](integration-ef.md#maybe-property-convention) for persisting `Maybe<T>` properties
- See [ASP.NET Core Integration](integration-aspnet.md#optional-value-objects-with-maybe) for JSON and model binding support
- Review [Error Handling](error-handling.md) for the error types used with `ToResult`
