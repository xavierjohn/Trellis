# <img src="images/icon.png" alt="Trellis logo" width="48" style="vertical-align: middle; margin-right: 8px;" /> Trellis

> **Structured building blocks for readable, explicit enterprise code**

Trellis helps you model your domain with **type-safe value objects**, compose workflows with **Railway-Oriented Programming**, and return consistent errors without filling your codebase with null checks and exception plumbing.

## Why teams reach for Trellis

The big win is simple: **your happy path stays readable even when the real world is messy**.

- **Validate once, trust everywhere** with value objects such as `FirstName`, `OrderId`, and `CustomerEmail`
- **Compose workflows safely** with `Combine`, `Bind`, `Ensure`, `Tap`, and `Match`
- **Return structured errors** with concrete types like `Error.UnprocessableContent`, `Error.NotFound`, and `Error.Conflict`
- **Keep domain code expressive** with aggregates, entities, specifications, and domain events
- **Integrate with ASP.NET Core** using `ToActionResult()` and `ToHttpResult()` when you are ready to expose APIs

> [!NOTE]
> Trellis error codes end with `.error` by default. For example, `new Error.NotFound(new ResourceRef("Resource")) { Detail = ... }` produces the code `not.found.error`.

---

## Before and after

The problem: everyday application code often turns into defensive boilerplate.

### Traditional approach

```csharp
var firstName = ValidateFirstName(input.FirstName);
if (firstName is null)
    return BadRequest("Invalid first name.");

var lastName = ValidateLastName(input.LastName);
if (lastName is null)
    return BadRequest("Invalid last name.");

var email = ValidateEmail(input.Email);
if (email is null)
    return BadRequest("Invalid email.");

if (_repository.EmailExists(email))
    return Conflict("Email already registered.");

var user = new User(firstName, lastName, email);
_repository.Save(user);
_emailService.SendWelcome(email);

return Ok(user);
```

### With Trellis

```csharp
using Trellis;

public partial class FirstName : RequiredString<FirstName> { }
public partial class LastName : RequiredString<LastName> { }
public partial class CustomerEmail : RequiredString<CustomerEmail> { }

public sealed record RegisterUserInput(string FirstName, string LastName, string Email);

public sealed record User(FirstName FirstName, LastName LastName, CustomerEmail Email)
{
    public static Result<User> TryCreate(FirstName firstName, LastName lastName, CustomerEmail email) =>
        Result.Ok(new User(firstName, lastName, email));
}

public static Result<User> RegisterUser(
    RegisterUserInput input,
    Func<CustomerEmail, bool> emailExists,
    Action<User> saveUser,
    Action<CustomerEmail> sendWelcomeEmail)
{
    return FirstName.TryCreate(input.FirstName)
        .Combine(LastName.TryCreate(input.LastName))
        .Combine(CustomerEmail.TryCreate(input.Email, fieldName: "email"))
        .Bind((firstName, lastName, email) => User.TryCreate(firstName, lastName, email))
        .Ensure(user => !emailExists(user.Email), new Error.Conflict(null, "conflict") { Detail = "Email already registered." })
        .Tap(saveUser)
        .Tap(user => sendWelcomeEmail(user.Email));
}
```

**Same workflow, less ceremony:** validate -> create -> check -> save -> notify.

---

## Quick start

Start with the packages most developers need first:

```bash
dotnet add package Trellis.Core
dotnet add package Trellis.Primitives
dotnet add package Trellis.Analyzers
```

> The source generator that backs the `Required*<TSelf>` base classes is bundled inside `Trellis.Core` (under `analyzers/dotnet/cs/`); no separate `Trellis.Core.Generator` package is needed.

Then create your first value object and use it in a result flow.

```csharp
using Trellis;

public partial class OrderNumber : RequiredString<OrderNumber> { }

Result<OrderNumber> orderNumber = OrderNumber.TryCreate("SO-2025-0001");

string message = orderNumber.Match(
    onSuccess: value => $"Created order number {value}.",
    onFailure: error => $"Validation failed: {error.Detail}"
);
```

> [!TIP]
> For quick custom value objects, inherit from the generic base type such as `RequiredString<OrderNumber>` or `RequiredGuid<OrderId>`. The generic parameter is required.

---

## What you get

| Capability | Why it matters | Learn more |
| --- | --- | --- |
| **`Result<T>` and `Maybe<T>`** | Make success and failure explicit instead of hiding them in exceptions and nulls | [Basics](articles/basics.md) |
| **Generated value objects** | Turn raw primitives into domain language the compiler understands | [Introduction](articles/intro.md) |
| **DDD building blocks** | Model aggregates, entities, value objects, and specifications directly | [Aggregate Factory Pattern](articles/aggregate-factory-pattern.md) |
| **Structured error types** | Return meaningful failures with default HTTP mappings | [Error Handling](articles/error-handling.md) |
| **ASP.NET integration** | Convert results to MVC or Minimal API responses without repetitive switch logic | [ASP.NET Core Integration](articles/integration-aspnet.md) |
| **Roslyn analyzers** | Catch unsafe `.Value` access and other ROP mistakes during development | [Analyzers](articles/analyzers/index.md) |

---

## A practical learning path

If you are new to Trellis, follow this order:

1. **[Introduction](articles/intro.md)** - understand the problems Trellis is solving
2. **[Basics](articles/basics.md)** - learn the core result operators you will use every day
3. **[Examples](articles/examples.md)** - copy real patterns for APIs, async work, and validation
4. **[ASP.NET Core Integration](articles/integration-aspnet.md)** - wire domain results into HTTP endpoints

If you want the full API surface, jump to the **[API reference](api/index.md)** after you understand the concepts.

---

## A few accuracy notes worth knowing early

- `Unit` is `record struct Unit;` and has **no** `Unit.Value` property. Use `Result.Ok()` for a success-without-payload flow, or `new Unit()` / `default` when you need a `Unit` instance.
- `Error.Equals(...)` compares **only the error code**, not the detail text.
- `new Error.NotFound(new ResourceRef("Resource")) { Detail = ... }`, `new Error.Conflict(null, "conflict") { Detail = ... }`, and the other factory methods create specific error subtypes with default `.error` codes.

---

## Learn more

| Goal | Start here |
| --- | --- |
| Build your first result pipeline | [Basics](articles/basics.md) |
| Understand the mental model | [Introduction](articles/intro.md) |
| See working scenarios | [Examples](articles/examples.md) |
| Expose APIs | [ASP.NET Core Integration](articles/integration-aspnet.md) |
| Dive into reference material | [API Documentation](api/index.md) |
