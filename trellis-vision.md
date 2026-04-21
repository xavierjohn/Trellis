# Trellis — Vision Statement

> **A .NET framework that makes AI-generated enterprise software reliable, not just fast.**

## The Opportunity

AI can now generate enterprise software from a plain-language specification. The question is no longer *can AI write code* — it's *can you trust the code AI writes.*

Today, AI generates code that compiles but silently mishandles errors, skips validation, or allows invalid state transitions. It looks correct. It passes a few tests. But it breaks in production in ways that are expensive to find and fix.

Trellis solves this by providing structural guardrails that make entire categories of bugs impossible — whether the code is written by AI, a senior developer, or a junior engineer. The compiler catches what code review misses.

## What Trellis Is

Trellis is a .NET framework that combines two proven software engineering disciplines — Functional Programming and Domain-Driven Design — into a set of building blocks for enterprise services.

A trellis is a structure that guides growth in the right direction. In a garden, plants grow along the trellis rather than sprawling randomly. In software, Trellis guides code into correct, composable patterns where errors are handled, domain rules are enforced, and business logic reads like plain English.

**Key properties:**
- **Open source** (MIT license) — no vendor lock-in, no licensing cost
- **Zero runtime overhead** — adds 11–16 nanoseconds per operation (0.002% of a typical database call)
- **Incremental adoption** — install one package into an existing project, no rewrite required
- **.NET 10 / C# 14** — targets the latest platform

## The Vision

Trellis enables a workflow where:

1. A human writes a specification in plain language — business requirements using the domain vocabulary.
2. AI consumes that specification and produces enterprise software using Trellis as the structural foundation.
3. Domain terms map directly to Trellis constructs — the spec says "Order," the code has an `Order` aggregate with typed IDs, state machine, and domain events.
4. The compiler enforces correctness — it is impossible to skip error handling, construct invalid objects, or make illegal state transitions.
5. When requirements change, the human updates the spec. The AI modifies the code. Trellis ensures changes propagate correctly at compile time.

This is the spec-to-working-software pipeline. The human owns the *what*. The AI handles the *how*. Trellis guarantees the *correctness*.

## Proof: The Order Management Evaluation

This is not theoretical. We ran it.

**Input:** A 387-line specification describing an Order Management service — customers, products, inventory, a 6-state order lifecycle, role-based authorization, and 14 API endpoints.

**Process:** An AI consumed the spec, the Trellis copilot instructions, and the API reference. No human intervention.

**Output:**
- 75+ source files across 4 architectural layers
- 12 validated domain types, 5 domain events, a 6-state state machine
- Role-based and resource-based authorization
- Database persistence with full test coverage
- **Build: 0 errors, 0 warnings**
- **Tests: 82 out of 82 passing**

The AI also generated a structured feedback report identifying 4 friction points in the framework — each with severity, context, and suggested fixes. Those improvements were incorporated, making the next AI's experience smoother.

**Time from spec to working, tested software: one AI session.**

## Why Now

Three trends are converging:

1. **AI code generation is production-ready** — but only when the output is structurally guided. Unguided AI produces code that looks right but fails unpredictably.

2. **Enterprise software costs are dominated by correctness, not speed** — finding and fixing bugs in production dwarfs the cost of writing code. A framework that prevents bugs at compile time shifts cost left dramatically.

3. **Developer productivity is the bottleneck** — teams need to deliver more services faster. Trellis + AI turns a spec into a working service in hours instead of weeks, with higher correctness than manual development.

## How It Works (Without the Technical Details)

### The AI Development Workflow

```
Install template → Scaffold project → Give AI the spec → Working software
```

1. **`dotnet new trellis-asp`** — scaffolds a complete project in under a second: solution structure, build system, test infrastructure, API versioning, telemetry, and health checks. Also installs AI instructions directly into the repository.

2. **AI reads the project** — the repository includes a copilot instructions file and a complete API reference. The AI knows how to build with Trellis without being told. Zero-token setup.

3. **AI implements the spec** — the human pastes the specification. The AI produces the full implementation: domain model, business logic, API endpoints, database persistence, and comprehensive tests.

4. **Compiler validates correctness** — 19 built-in code analyzers catch common mistakes at build time. The type system prevents entire categories of bugs from compiling.

5. **AI reports framework friction** — after every project, the AI generates a structured feedback report. This creates a continuous improvement loop: every AI-generated project makes the framework better for the next one.

### The Continuous Improvement Loop

```
AI builds with Trellis → AI reports what was hard → Trellis team fixes gaps → Next AI is more effective
```

This feedback loop is a structural advantage. Every project the AI builds with Trellis generates data that improves the framework. Competing approaches — manual coding, unguided AI — have no equivalent mechanism.

## Competitive Positioning

### vs. AI Generating Code Without Guardrails

This is the real comparison. Without a structural framework, AI-generated enterprise code:
- Compiles and passes a few tests
- Silently mishandles errors, skips validation, or allows invalid state transitions
- Requires extensive manual code review to find the problems
- Breaks in production in ways that are difficult to diagnose

With Trellis, the compiler catches these issues before the code runs. Correctness is structural, not incidental.

### vs. Building the Same Guardrails In-House

Every team that adopts structured patterns in C# ends up writing the same glue code: error-to-HTTP mapping, validation bridging, value object boilerplate, state machine wrappers. Trellis provides all of this out of the box — tested, maintained, and optimized for AI consumption. Building it in-house is 3–6 months of senior engineering time with ongoing maintenance cost.

### Unique Differentiator

No other .NET framework provides the full AI-native pipeline:
- A project template with baked-in AI instructions
- A mechanical spec-to-code mapping
- Compiler-enforced correctness via 19 analyzers
- A structured feedback loop where every AI-generated project reports framework friction

The combination makes Trellis uniquely suited for AI-driven enterprise development.

## Risk and Adoption

### No Lock-in

Trellis imposes patterns, not protocols. Every Trellis type has a clear migration path:
- No runtime services, hosted processes, or network dependencies
- Thin base classes replaceable with a few lines of manual code
- Incremental adoption — old and new patterns coexist in the same project
- Exit strategy: swap the types, update the imports, keep the same code shape

### Brownfield-Safe

Trellis does not require a rewrite. Teams adopt it one endpoint, one method, one service at a time. Each conversion is isolated — changing one endpoint doesn't affect others. There is no flag day.

### Production Readiness

The underlying patterns — Result types, Railway-Oriented Programming, Domain-Driven Design, value objects — are battle-tested across the industry. The Trellis packaging is newer, but the engineering disciplines are decades old. The Order Management reference implementation serves as a fully working, browsable, testable example.

## Packaging and Distribution

Trellis is distributed as a set of NuGet packages (the standard .NET package manager). Teams install only the packages they need:

| Category | What It Covers |
|----------|---------------|
| **Core** (5 packages) | Error handling, domain building blocks, typed value objects, code analyzers, source generator |
| **Integration** (9 packages) | ASP.NET Core, HTTP clients, validation, testing, state machines, authorization, CQRS, database persistence |
| **Template** (1 package) | Project scaffolding with AI instructions |

All packages are versioned together and follow semantic versioning. MIT licensed.

## Identity

**Name:** Trellis
**Tagline:** Structured building blocks for AI-driven enterprise software
**Repository:** github.com/xavierjohn/Trellis
**Documentation:** xavierjohn.github.io/Trellis/
**License:** MIT
**Target platform:** .NET 10 / C# 14

---

## Appendix: Technical Details

*The sections below are reference material for engineering teams evaluating Trellis.*

### What Trellis Provides

- **Result\<T\> and Maybe\<T\>** — composable error handling and optional values. Errors flow through a pipeline without exceptions.
- **Strongly-typed value objects** — `FirstName`, `EmailAddress`, `OrderId` validate on construction. If it exists, it's valid.
- **Aggregates and entities** with domain events — enforce business rules and consistency boundaries.
- **State machine integration** — transitions return `Result<T>` instead of throwing, composable with the pipeline.
- **Composable Specifications** — business rules as reusable, storage-agnostic expression trees.
- **19 Roslyn analyzers** — catch incorrect usage at compile time.
- **Source generator** for value object boilerplate.
- **CQRS pipeline behaviors** — validation, authorization, logging, and tracing, all short-circuiting on failure.
- **Authorization building blocks** — Actor, permissions, resource-based auth, returning `Result<T>` on failure.

### Code Example

Business logic reads like plain English:

```csharp
public async Task<Result<User>> CreateUserAsync(
    string firstName, string lastName, string email, CancellationToken ct)
    => FirstName.TryCreate(firstName)
        .Combine(LastName.TryCreate(lastName))
        .Combine(EmailAddress.TryCreate(email))
        .Bind((first, last, email) => User.TryCreate(first, last, email))
        .Ensure(user => !_repository.EmailExists(user.Email), new Error.Conflict(null, "conflict") { Detail = "Email exists" })
        .Tap(user => _repository.Save(user))
        .Tap(user => _emailService.SendWelcome(user.Email));
```

This reads as: "Create a first name, last name, and email. Combine them to create a user. Ensure the email doesn't already exist. Save the user. Send a welcome email." Any step that fails short-circuits the rest — no nested if-statements, no try-catch, no null checks.

### Spec-to-Code Mapping

When a specification says:

> "An Order has an OrderId, a Customer, line items, and a status. The status transitions from Draft → Submitted → Approved → Shipped."

Each term maps directly to a Trellis construct:

| Spec Term | Trellis Construct |
|-----------|------------------|
| OrderId | Typed value object (`RequiredGuid`) |
| Customer | Aggregate reference via typed ID |
| Order | `Aggregate<OrderId>` with domain events |
| Status transitions | State machine returning `Result<Order>` |
| Line items | Collection of `Entity<LineItemId>` |
| Business rules | Guard clauses enforced by the type system |

### Error Types

Trellis provides 10 discriminated error types that map to HTTP status codes:

| Error | HTTP | Use Case |
|-------|------|----------|
| Error.UnprocessableContent | 400 | Invalid input with field-level details |
| Error.NotFound | 404 | Entity doesn't exist |
| Error.Conflict | 409 | Duplicate or concurrency violation |
| Error.Forbidden | 403 | Insufficient permissions |
| Error.Conflict | 422 | Business rule violation |
| Error.InternalServerError | 500 | Unhandled failure |

Plus `Error.BadRequest` (400), `Error.Unauthorized` (401), `Error.TooManyRequests` (429), `Error.ServiceUnavailable` (503).

### Analyzer Rules

19 compile-time rules catch common mistakes. Examples:

| Rule | What It Catches |
|------|----------------|
| Result return value ignored | Discarded error — bug that compiles silently |
| Unsafe `.Value` access | Accessing a value without checking success first |
| `throw` inside a Result chain | Defeats the purpose of structured error handling |
| Blocking on async Result | `.Result` or `.Wait()` on `Task<Result<T>>` |

Full rule table: [Analyzer Documentation](https://xavierjohn.github.io/Trellis/articles/analyzers.html)

### Package Architecture

**Core packages:**
| Package | Purpose |
|---------|---------|
| `Trellis.Core` | Result\<T\>, Maybe\<T\>, error types, pipeline operators |
| `Trellis.DomainDrivenDesign` | Aggregate, Entity, ValueObject, Domain Events, Specification |
| `Trellis.Primitives` | 12 ready-to-use value objects + base types |
| `Trellis.Core.Generator` | Source generator for value object boilerplate |
| `Trellis.Analyzers` | 19 Roslyn analyzers |

**Integration packages:**
| Package | Purpose |
|---------|---------|
| `Trellis.Asp` | Result → HTTP responses for MVC and Minimal API |
| `Trellis.Http` | HttpClient extensions returning Result\<T\> |
| `Trellis.FluentValidation` | Bridge FluentValidation into the pipeline |
| `Trellis.Testing` | FluentAssertions extensions, test builders |
| `Trellis.Stateless` | State machine integration |
| `Trellis.Authorization` | Actor, permissions, resource-based auth (ASP.NET actor providers — Claims, Entra, Development — ship inside `Trellis.Asp`) |
| `Trellis.Mediator` | CQRS pipeline behaviors |
| `Trellis.EntityFrameworkCore` | EF Core conventions, query extensions |

### Performance

ROP adds 11–16 nanoseconds per operation. Zero extra allocations on Combine operations. For enterprise workloads doing I/O (database queries, HTTP calls), this is negligible.

Detailed benchmarks: [BENCHMARKS.md](https://xavierjohn.github.io/Trellis/articles/BENCHMARKS.html)

### Versioning

Semantic versioning. All packages versioned together. Migration guide with every major release.

### Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Name | Trellis | Structure + guided growth. Scales beyond "railway" to the AI-native vision. |
| Target framework | .NET 10 / C# 14 | Latest features, forward-looking audience. |
| Mediator library | martinothamar/Mediator | Source-generated, AOT-friendly, philosophically aligned. |
| License | MIT | Maximum adoption, zero friction. |

### Adoption Path

1. Install `Trellis.Core` — start returning `Result<T>` from new methods
2. Add `Trellis.Primitives` — introduce value objects for new domain concepts
3. Add `Trellis.Asp` — use `ToActionResult()` on new endpoints
4. Migrate gradually — convert existing endpoints one at a time

### Migration from FunctionalDDD

For existing users: rename packages (`FunctionalDdd.*` → `Trellis.*`), update using statements. No API changes.
