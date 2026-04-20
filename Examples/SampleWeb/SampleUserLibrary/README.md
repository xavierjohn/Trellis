# SampleUserLibrary

Pure-domain class library shared by [`SampleMinimalApi`](../SampleMinimalApi/).
This is the **canonical place to look for how a Trellis aggregate should be written**.

## What this teaches

| Concept | Where to look |
|---|---|
| Pure-ROP `TryCreate` (no FluentValidation, no `if/throw` for business invariants) | `Aggregate/User.cs` |
| Composite VO construction via `Combine` + `Bind` (axiom A4) | `Aggregate/User.cs` (`Name.TryCreate`) |
| Programmer-error guards on non-nullable VO params (axiom A11) | `Aggregate/User.cs` (`ArgumentNullException.ThrowIfNull`) |
| Scalar value objects (`UserId`, `ProductId`, `CustomerId`, `OrderId`, `OrderLineId`, `ProductName`, `FirstName`, `LastName`) | `ValueObject/` |
| Composite value object (`Name`) | `ValueObject/Name.cs` |
| Wire-DTOs whose fields are VOs (axiom A1a/A1b) — automatic validation on bind | `Model/RegisterUserDto.cs`, `Model/OrderDto.cs` |
| EF Core / state machine specifications | `Specifications/ProductSpecifications.cs` |

## Domain purity (axiom A8)

This project references **only** Trellis (`Trellis.Primitives`,
`Trellis.DomainDrivenDesign`, `Trellis.Stateless`, `Trellis.Results`). It does
**not** reference:

- `Microsoft.AspNetCore.*`
- `Microsoft.EntityFrameworkCore.*`
- `Trellis.Asp.*`
- `Trellis.EntityFrameworkCore.*`
- `Trellis.FluentValidation`

That's the point. The same library can back a Minimal API host today,
an MVC host tomorrow, and a worker service after that — with no changes.

## How to consume it

```csharp
// Inside a workflow / endpoint:
var nameResult = Name.TryCreate(firstName, lastName);
var userResult = nameResult.Bind(name =>
    User.TryCreate(name, email, phone, age, country, password));

// userResult is Result<User>. Convert to HTTP via ToHttpResult / ToActionResult,
// or pipe through .BindAsync / .TapAsync into a workflow.
```

See `SampleMinimalApi/Workflows/UserWorkflow.cs` for a worked example.
