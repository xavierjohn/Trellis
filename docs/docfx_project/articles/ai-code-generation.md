---
title: Trellis for AI Code Generation
package: Trellis (multiple)
topics: [ai, llm, code-generation, value-objects, result, prompting]
related_api_reference: [trellis-api-core.md, trellis-api-primitives.md, trellis-api-asp.md, trellis-api-statemachine.md]
last_verified: 2026-05-01
audience: [developer]
---
# Trellis for AI Code Generation

AI is good at producing code quickly.

The hard part is getting code that is still understandable, reviewable, and correct **after the first happy-path demo**. That is where Trellis helps.

Trellis gives an AI a constrained set of good moves:

- validate input with value objects
- represent failure with `Result<T>` instead of exceptions or `null`
- keep domain rules explicit
- map results to HTTP consistently
- model state transitions explicitly when the workflow matters

That structure makes AI-generated code easier for humans to review.

## Start with a practical example

Imagine the specification says:

> Register a user. First name, last name, and email are required. Return a structured validation error when any value is invalid.

A Trellis-shaped implementation is straightforward:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Trellis.Primitives;

public sealed record RegisterUserRequest(string FirstName, string LastName, string Email);
public sealed record RegisterUserResponse(string FirstName, string LastName, string Email);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrellisAsp();

var app = builder.Build();

app.MapPost("/users/register", (RegisterUserRequest request) =>
    FirstName.TryCreate(request.FirstName)
        .Combine(LastName.TryCreate(request.LastName))
        .Combine(EmailAddress.TryCreate(request.Email))
        .Bind((firstName, lastName, emailAddress) =>
            Result.Ok(new RegisterUserResponse(
                firstName.Value,
                lastName.Value,
                emailAddress.Value)))
        .ToHttpResponse());

app.Run();
```

Why this works well for AI-generated code:

- the validation pattern is obvious
- the failure path is explicit
- the HTTP mapping is a single, predictable step
- the reviewer can scan the whole flow in seconds

## Why Trellis helps AI more than unstructured C# does

### 1. Illegal states become harder to express

Without Trellis, an AI can easily generate this:

```csharp
public sealed class User
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
}
```

That compiles, but it tells you almost nothing about validity.

With Trellis, the shape pushes the AI toward validated types instead:

```csharp
using Trellis.Primitives;

public sealed record RegisteredUser(FirstName FirstName, EmailAddress Email);
```

Now the compiler and the construction path help enforce the rules.

### 2. Errors are values, not side effects

When AI-generated code mixes exceptions, `null`, and ad-hoc status flags, review gets expensive.

Trellis makes the contract visible in the type:

```csharp
using Trellis;
using Trellis.Primitives;

public interface IPrimaryEmailLookup
{
    Result<EmailAddress> GetPrimaryEmail(string userName);
}
```

The caller knows it must handle success or failure. The ambiguity disappears.

### 3. The same patterns repeat across the codebase

That repetition is a feature.

A reviewer learns one Trellis service and can usually review the next one much faster because the shapes stay familiar:

- validate with `TryCreate`
- compose with `Bind`, `Map`, `Ensure`, `Tap`, `Combine`
- branch with `Match` (with a `switch` expression on the closed `Error` ADT)
- map to HTTP with `ToHttpResponse()` or `ToHttpResponse().AsActionResult<T>()`

## Spec-to-code mapping

This is where Trellis becomes especially useful for AI-assisted development.

| Specification idea | Trellis construct | Typical API |
| --- | --- | --- |
| A validated input value | Value object | `EmailAddress.TryCreate(...)` |
| An operation that can fail | Result-returning method | `Result<T>` |
| Optional domain data | `Maybe<T>` | `Maybe<T>` |
| A business rule on a success value | Validation step | `.Ensure(...)` |
| Independent validations | Aggregation | `.Combine(...)` |
| A side effect that should not change the value | Side-effect step | `.Tap(...)` |
| HTTP response mapping | ASP.NET integration | `.ToHttpResponse()` / `.AsActionResult<T>()` |
| Workflow transition | Stateless integration | `machine.FireResult(trigger)` |

## State transitions are explicit too

If your specification describes a workflow, Trellis gives the AI a better pattern than "throw an exception when the state is wrong."

```csharp
using Stateless;
using Trellis;
using Trellis.StateMachine;

public enum OrderState { Draft, Submitted, Approved }
public enum OrderTrigger { Submit, Approve }

var state = OrderState.Draft;
var machine = new StateMachine<OrderState, OrderTrigger>(() => state, s => state = s);

machine.Configure(OrderState.Draft)
    .Permit(OrderTrigger.Submit, OrderState.Submitted);

machine.Configure(OrderState.Submitted)
    .Permit(OrderTrigger.Approve, OrderState.Approved);

Result<OrderState> result = machine.FireResult(OrderTrigger.Submit);
```

That gives an AI a clear, typed path for workflow behavior instead of hidden conventions.

## What humans get back from this structure

### Faster review

A reviewer can spot missing validation, skipped error handling, or suspicious state transitions quickly because the code follows familiar Trellis shapes.

### Better prompts

When a team prompts an AI with Trellis vocabulary, prompts become more precise:

- "Use value objects for validated inputs."
- "Return `Result<T>` from application services."
- "Use `Combine` for independent validation."
- "Map endpoint results with `ToHttpResponse()`."

That is easier to execute than vague instructions like "follow good architecture."

### Compiler and analyzer support

Trellis is not just a style guide. It also gives AI-generated code more compiler-visible structure and analyzer-visible patterns, which makes problems easier to catch early.

## A good mental model

Trellis does not make AI "smarter."

It makes AI output **more bounded**.

That matters because bounded output is easier to:

- review
- test
- refactor
- regenerate safely
- keep consistent across a team

## Prompting advice for teams using AI with Trellis

If you want better results, ask for these things explicitly:

- use `TryCreate` for validated inputs
- return `Result<T>` from operations that can fail
- use `Combine` before `Bind` for independent validation steps
- use `Ensure` for business-rule checks
- use `ToHttpResponse()` or `ToHttpResponse().AsActionResult<T>()` at the API boundary
- use `FireResult` for state-machine transitions instead of throwing for invalid transitions

## Bottom line

AI works best when the target codebase has strong rails.

That is exactly what Trellis provides: not magic, but **predictable structure**.

And predictable structure is what makes AI-generated enterprise code reviewable by humans.
