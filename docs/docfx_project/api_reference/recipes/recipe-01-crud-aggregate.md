---
name: recipe-01-crud-aggregate
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows an aggregate with value objects, an entity and a repository contract. Use when creating or loading an aggregate, or when a cancel or return must release stock."
---
# Recipe 1 — CRUD aggregate (DDD value objects + entity + repository contract)

**Requires:** `Trellis.Core`, plus `Trellis.Authorization`.

## Contents

- Problem
- What it shows
- Anti-pattern → fix (wrong attribute namespace)

**Problem.** Model an `Order` aggregate with a typed identifier, a value-object money type, and a repository contract that returns `Result<T>` for not-found.

```csharp
using Trellis;
using Trellis.Authorization;

// Strongly-typed ID: source-generated factory, equality, parsing, JSON converter.
public sealed partial class OrderId : RequiredGuid<OrderId>;

// Value object backed by a 3-letter ISO 4217 currency code.
[StringLength(3, MinimumLength = 3)]
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;

// Composite value object — must be a class (records can't inherit ValueObject).
public sealed class Money : ValueObject
{
    private Money() { } // EF materialization without an EF package dependency.
    public Money(decimal amount, CurrencyCode currency) { Amount = amount; Currency = currency; }
    public decimal Amount { get; private set; }
    public CurrencyCode Currency { get; private set; } = null!;
    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Amount);
        components.Add(Currency);
    }
}

// Aggregate root.
public sealed class Order : Aggregate<OrderId>
{
    public Money Total { get; private set; } = default!;
    public OrderStatus Status { get; private set; }
    public ActorId OwnerId { get; private set; } = default!;

    private Order(OrderId id) : base(id) { }   // EF Core ctor

    // Nullable guards carry the values; Combine accumulates missing-field errors.
    // Combine aggregates per-field errors into a single Error.InvalidInput; Map's
    // tuple-deconstructing overload lets the lambda bind the validated non-null values
    // directly as id/total/ownerId.
    public static Result<Order> TryCreate(OrderId? id, Money? total, ActorId? ownerId) =>
        Result.EnsureNotNull(id, "id", "Order id is required.")
            .Combine(Result.EnsureNotNull(total, "total", "Total is required."))
            .Combine(Result.EnsureNotNull(ownerId, "ownerId", "Owner id is required."))
            .Map((id, total, ownerId) => new Order(id) { Total = total, Status = OrderStatus.Draft, OwnerId = ownerId });
}

// Trellis convention: model finite domain states as RequiredEnum<TSelf>
// (NOT C# enums). The partial keyword triggers the source generator.
public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft     = new();
    public static readonly OrderStatus Submitted = new();
    public static readonly OrderStatus Cancelled = new();
}

// Repository contract — uses Maybe<T> for "may legitimately find nothing"
// Reserve Result<T> for failures the caller can act on.
public interface IOrderRepository
{
    Task<Maybe<Order>> FindAsync(OrderId id, CancellationToken ct);
    void Add(Order order);
}
```

**What it shows.** Each base class in this recipe supplies a complete surface — your derived type adds only domain-specific state. **Do not redeclare members that are already inherited**; that is the most common Recipe 1 mistake.

- `RequiredGuid<TSelf>` source-generates `TryCreate` overloads, `Parse`/`TryParse`, an explicit `Guid` → `TSelf` operator, the `Value` accessor, equality / `GetHashCode` / `IComparable`, JSON and EF Core converters, plus the `NewUniqueV4()`, `NewUniqueV7()`, and `NewUniqueV7(TimeProvider)` factories. It rejects `null` only by default; `Guid.Empty` is accepted. Add `[NotDefault]` to also reject `Guid.Empty`. Do not write your own `TryCreate`, equality members, parse/convert helpers, or JSON/EF converters.
- `RequiredString<TSelf>` source-generates `TryCreate(string?, string?)`, `Parse`/`TryParse`, an explicit `string` → `TSelf` operator, the `Value` accessor, equality, JSON and EF Core converters, plus `Length`/`StartsWith`/`Contains`/`EndsWith` pass-throughs. It rejects `null` only by default; `""` and whitespace-only input are accepted and stored as-is (no auto-trim). Add `[NotDefault]` to also reject `""`, `[Trim]` to enable trimming, or both for strict trim-then-reject-empty behavior. Same rule applies: derived classes add only domain-specific helpers (e.g., a custom `TryCreateWithValidation` that layers extra rules on top of the generated `TryCreate`).
- `ValueObject` (the base of `Money`, `Address`, etc.) supplies `Equals(object?)`, `Equals(ValueObject?)`, `GetHashCode`, `CompareTo`, and the `==`/`!=`/`<`/`<=`/`>`/`>=` operators — all derived from `GetEqualityComponents()`. Your derived type implements **only** `protected override void GetEqualityComponents(ref EqualityComponents components)`. Do not override `Equals`/`GetHashCode`/`CompareTo` or write equality operators yourself — that breaks the contract the base class establishes. For `Maybe<T>` components, use the inherited `protected static IComparable? MaybeComponent<T>(Maybe<T>)` helper rather than unwrapping manually.
- `Aggregate<TId>` already supplies inherited infrastructure members: `Id`, protected `DomainEvents`, persistence-managed `ETag`, `IsChanged` based on pending domain events, and the `CreatedAt`/`LastModified` timestamps (inherited from `Entity<TId>`, managed by `EntityTimestampInterceptor`). Do not redeclare those members on every aggregate; use the inherited surface and add only domain-specific state. Domain events are added via `DomainEvents.Add(...)` from inside the aggregate; the public read-only view is `IAggregate.UncommittedEvents()`.

> **Compiled contract.** The exact signatures of every member listed above are exercised in `Examples/CookbookSnippets/Recipe01_CrudAggregate.cs` → `Recipe1InheritedSurface`. That file is compiled in CI, so if a signature changes in the framework, the build fails and this callout MUST be updated to match. When you need to confirm an exact overload, read the demonstrator — never paraphrase signatures from memory.

`Required*<TSelf>` primitives are lenient by default (rejects `null` only). Use `[NotDefault]` to opt into rejecting the type's sentinel value, and `[Trim]` to opt into string trimming. Both attributes are meaningful; see [trellis-api-primitives.md](../trellis-api-primitives.md#required-defaults-and-opt-ins) for the full behavior table.

`[StringLength]` and `[Range]` come from the **`Trellis` namespace** and are placed on the **class declaration**. The `System.ComponentModel.DataAnnotations` attributes of the same name target properties/fields/parameters, so applying them to a value object is a **compile error** — `CS0104` (ambiguous reference) for an unqualified attribute when both namespaces are in scope, otherwise `CS0592`.

**Anti-pattern → fix (wrong attribute namespace).**

```csharp
// WRONG — importing System.ComponentModel.DataAnnotations alongside Trellis brings a
// second [StringLength] into scope, so the attribute no longer resolves to Trellis.
using Trellis;
using System.ComponentModel.DataAnnotations;     // ← wrong namespace also in scope
[StringLength(3, MinimumLength = 3)]             // CS0104: ambiguous between Trellis and DataAnnotations
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;

// FIX — keep only the Trellis namespace in scope.
using Trellis;                                   // ← Trellis attributes only
[StringLength(3, MinimumLength = 3)]             // resolves to Trellis.StringLength
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;
```
