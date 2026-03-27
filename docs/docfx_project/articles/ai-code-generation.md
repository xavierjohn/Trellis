# Trellis for AI Code Generation

**Level:** Overview | **Time:** 15-20 min

Trellis is designed so that both humans and AI can produce correct, maintainable, enterprise-grade code by following the structure the framework provides. This article explains how domain specifications map to Trellis constructs and why the framework is uniquely suited for AI-driven development.

## The Problem with AI-Generated Enterprise Code

The hardest part of AI-generated code isn't generating it — it's reading it. Trellis makes AI output predictable. Every service follows the same 4-layer architecture, the same ROP patterns, the same value object conventions. A developer who knows Trellis can review any AI-generated service in minutes, regardless of which AI wrote it.

Without structure, traditional C# enterprise code gives AI no guardrails:

- **Nested if-statements** obscure business logic and make errors easy to miss
- **Primitive obsession** (using `string` for email, `int` for order ID) lets bugs through that the compiler should catch
- **Inconsistent error handling** — some code throws, some returns null, some returns error codes
- **State transitions enforced by convention**, not by the type system

AI generating this style of code can produce code that compiles but silently does the wrong thing.

## How Trellis Solves This

Trellis's building blocks **constrain what is possible to write**. An AI (or a junior developer) cannot produce invalid code because the framework prevents it. The compiler is the guardrail.

### Make Illegal States Unrepresentable

```csharp
// ❌ AI can generate this — compiles, but the email might be invalid
public class User
{
    public string Email { get; set; }  // Could be anything
    public string FirstName { get; set; }  // Could be null or empty
}

// ✅ Trellis — if a value object exists, it's valid
public class User : Aggregate<UserId>
{
    public EmailAddress Email { get; private set; }  // Always valid
    public FirstName FirstName { get; private set; }  // Always non-empty
}
```

### Errors Are Values, Not Exceptions

```csharp
// ❌ AI might forget to catch exceptions
var user = await _repository.GetUserAsync(id);  // Throws? Returns null? Who knows?

// ✅ Trellis — the type system forces error handling
Result<User> result = await _repository.GetUserAsync(id);
// Can't access the value without handling the error case
```

### Code Reads Like English

```csharp
// Application layer — pure business logic chain
public async Task<Result<User>> CreateUserAsync(
    string firstName, string lastName, string email, CancellationToken ct)
    => FirstName.TryCreate(firstName)
        .Combine(LastName.TryCreate(lastName))
        .Combine(EmailAddress.TryCreate(email))
        .Bind((first, last, email) => User.TryCreate(first, last, email))
        .Ensure(user => !_repository.EmailExists(user.Email), Error.Conflict("Email exists"))
        .Tap(user => _repository.Save(user))
        .Tap(user => _emailService.SendWelcome(user.Email));

// API layer — one line to convert Result to HTTP response
[HttpPost]
public async Task<ActionResult<UserResponse>> CreateUser(
    CreateUserRequest request, CancellationToken ct)
    => await _userService.CreateUserAsync(
        request.FirstName, request.LastName, request.Email, ct)
        .ToActionResult(this);
```

## Spec-to-Code Mapping

When a specification says:

> "An Order has an OrderId, a Customer, line items, and a status. The status transitions from Draft → Submitted → Approved → Shipped. An order can be cancelled from any state except Shipped."

Trellis provides a direct, mechanical mapping:

| Specification Concept | Trellis Construct | Example |
|----------------------|-------------------|---------|
| **OrderId** | `RequiredGuid`-derived value object | `public partial class OrderId : RequiredGuid<OrderId> { }` |
| **Customer** | Aggregate reference via typed ID | `public CustomerId CustomerId { get; }` |
| **Order** | `Aggregate<OrderId>` with domain events | `public class Order : Aggregate<OrderId>` |
| **Status transitions** | State machine returning `Result<Order>` | `Fire(OrderTrigger.Submit)` returns `Result<Order>` |
| **Line items** | Collection of `Entity<LineItemId>` | `public IReadOnlyList<LineItem> Lines { get; }` |
| **"Can be cancelled except from Shipped"** | Guard clause on the Cancel trigger | `stateMachine.Configure(Shipped).Ignore(Cancel)` |
| **"Display all overdue orders over $500 in the West region"** | Composable `Specification<Order>` | `OverdueOrderSpec().And(OrderValueExceedsSpec(500m)).And(CustomerInRegionSpec("West"))` |

The AI doesn't need to invent patterns. It follows the structure Trellis provides.

## The Workflow

1. **A human writes a specification** describing business requirements in plain language, using ubiquitous language from the domain.
2. **An AI consumes that specification** and produces enterprise software using Trellis as the structural foundation.
3. **The terms in the ubiquitous language map directly** to Trellis constructs — aggregates, value objects, entities, domain events, and state machines.
4. **The type system and compiler enforce correctness** — it is impossible to skip error handling, construct invalid domain objects, or make illegal state transitions.
5. **When requirements change**, the human updates the specification and the AI modifies the code. Trellis's structure ensures changes propagate correctly — new error cases must be handled, new states must be accounted for, new validation rules are enforced at compile time.

## Why Trellis for AI?

### Compiler as Guardrail

Trellis provides 19 Roslyn analyzers that catch incorrect usage at compile time. When AI generates code that doesn't follow the patterns, the compiler flags it immediately. See [Analyzer Rules](analyzers/index.md) for the complete list.

### Mechanical Mapping

Every domain concept has a single, obvious Trellis construct. There is no ambiguity about which pattern to use:

- Business entity with identity → `Entity<TId>` or `Aggregate<TId>`
- Validated value → Value object with `TryCreate`/`Create`
- Optional value → `Maybe<T>`
- Operation that can fail → Returns `Result<T>`
- Business rule → `Ensure` or `Specification<T>`
- Side effect → `Tap`
- Error → Discriminated error type (`Error.Validation`, `Error.NotFound`, etc.)

### Evolving Specifications

When requirements change, the type system guides the update:

- **New error case added** → Compiler error: all `MatchError` calls must handle the new case
- **New state added to state machine** → Compiler error: transitions must be defined
- **New required field on aggregate** → Compiler error: constructors and factories must be updated
- **New validation rule** → Add an `Ensure` step; existing pipeline structure unchanged

## Next Steps

- [Introduction](intro.md) — Core concepts and why Trellis exists
- [Basics](basics.md) — Learn the ROP operations
- [Clean Architecture](clean-architecture.md) — Full architecture patterns
- [Examples](examples.md) — Real-world code samples
