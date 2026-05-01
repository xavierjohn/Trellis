---
title: With Trellis vs Without Trellis
package: Trellis (multiple)
topics: [ai, comparison, study, scaffolding, security, review]
related_api_reference: []
last_verified: 2026-05-01
audience: [developer]
---
# With Trellis vs Without Trellis

If you are using AI to build production software, the real question is not "Can it generate code?"

The real question is: **Which setup gives you code that is safer to review and harder to get subtly wrong?**

This study compared both approaches on the same Order Management specification.

## The study in one minute

Three models built the same service twice:

- **With Trellis** — starting from the `trellis-asp` template plus Trellis documentation and instructions
- **Without Trellis** — starting from an empty folder with only the spec and standard .NET requirements

Then different models reviewed the implementations.

## Results at a glance

| Metric | With Trellis | Without Trellis |
| --- | ---: | ---: |
| Build succeeds | 3/3 | 3/3 |
| All tests pass | 3/3 | 3/3 |
| Average test count | 62 | 65 |
| Endpoints correct | **16/16** | 13-16/16 |
| Auth vulnerabilities found | **0** | 2 of 3 implementations |
| Average cross-review score | **8.2/10** | **7.1/10** |

The surprising part is not that both approaches worked.

The surprising part is **where the failures clustered** when Trellis was absent:

- endpoint path drift
- permission mistakes
- inconsistent error handling
- security gaps that were easy to miss in casual review

## Why this matters

On day one, the non-Trellis code often looked more familiar.

But as the spec grew, Trellis gave the AI stronger rails:

- the template scaffolded the correct endpoint shapes
- result-based flows made failure handling explicit
- the authorization and error-handling patterns were less ad hoc
- reviewers saw more uniform code across features

## Before/after: the kind of difference reviewers felt

### Without Trellis

This style is ordinary ASP.NET Core. It is also easy for an AI to make inconsistent across many endpoints.

```csharp
using Microsoft.AspNetCore.Builder;

public sealed record RegisterUserRequest(string FirstName, string LastName, string Email);
public sealed record RegisterUserResponse(string FirstName, string LastName, string Email);

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/users/register", (RegisterUserRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.FirstName))
        return Results.BadRequest(new { code = "validation.error", detail = "First name is required." });

    if (string.IsNullOrWhiteSpace(request.LastName))
        return Results.BadRequest(new { code = "validation.error", detail = "Last name is required." });

    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        return Results.BadRequest(new { code = "validation.error", detail = "Email is invalid." });

    return Results.Ok(new RegisterUserResponse(request.FirstName, request.LastName, request.Email));
});

app.Run();
```

### With Trellis

The Trellis version pushes the code into a more uniform shape.

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

Neither version is impossible to review. The Trellis version is simply **more constrained**, which makes large-scale AI output easier to keep consistent.

## Five findings that mattered most

### 1. Trellis reduced spec drift

The biggest difference was not raw code quality. It was **compliance with the spec**.

One non-Trellis implementation got **6 of 16 endpoint paths wrong**. The same model, working with Trellis scaffolding, got them right.

Why? Because the template turned correctness into the default starting point.

### 2. Trellis reduced security mistakes

Two of the three non-Trellis implementations had authorization vulnerabilities.

The issues included:

- defaulting to an admin-like actor when test headers were missing or malformed
- leaking internal exception details into HTTP responses

The Trellis-based implementations avoided those classes of mistakes structurally.

> [!NOTE]
> In the Trellis setup, `DevelopmentActorProvider` is explicitly for development, and the error-handling middleware returns a generic 500 response instead of exposing internals.

### 3. Cross-review exposed the gap more clearly than self-review

When models reviewed their own code, the difference looked smaller.

When a different model reviewed the code, the Trellis implementations scored better more consistently. That suggests Trellis improved not just generation, but **reviewability by another reader**.

### 4. The template mattered a lot

Reviewers repeatedly credited the template for preventing a whole class of mistakes before business logic even started.

The template contributed:

- correct endpoint paths and verbs
- permission scaffolding
- testing infrastructure
- a consistent project layout
- instructions that guide implementation order

### 5. Non-Trellis code was easier to read at first

This was the strongest point in favor of the non-Trellis approach.

For a developer seeing the code for the first time, plain ASP.NET Core often felt more familiar.

But reviewers also agreed that this advantage fades as the codebase grows. Once you have more aggregates, more rules, and more contributors, consistency begins to matter more than first-contact familiarity.

## Where each approach wins

### Choose Trellis when

- the service will grow beyond a small prototype
- multiple developers will touch the code over time
- authorization rules are non-trivial
- AI is generating a meaningful amount of the implementation
- you want compile-time and template-level guardrails

### Skip Trellis when

- you are building a tiny throwaway prototype
- the team strongly prefers plain ASP.NET Core patterns
- short-term onboarding speed matters more than long-term uniformity

## The bigger lesson

The study does **not** say that AI without Trellis cannot succeed.

It says something more practical:

> [!TIP]
> When AI has stronger structural constraints, it is less likely to drift away from the spec in ways that still compile and still look plausible.

That is the real value.

## Bottom line

Without Trellis, AI can absolutely produce working software.

With Trellis, it more consistently produced software that was:

- closer to the spec
- safer around auth and errors
- easier for another reviewer to assess quickly

That is why teams using AI for serious work should care.
