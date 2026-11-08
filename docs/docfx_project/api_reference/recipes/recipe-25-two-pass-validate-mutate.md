---
name: recipe-25-two-pass-validate-mutate
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows two-pass validate-then-mutate over related aggregates with no partial mutation. Use when applying an operation to every element of a set, such as reserving stock per line."
---
# Recipe 25 — Two-pass validate-then-mutate over a collection of related aggregates

**Requires:** `Trellis.Core`, plus `Trellis.Mediator`.

## Contents

- Problem
- The invariant the recipe teaches
- Two design moves in the stock example
- Worked example
- The Pass-2-cannot-fail invariant — what makes it hold
- Execution-time eligibility
- Snapshot-time eligibility
- Choosing fail-fast vs accumulating for the validation pass
- How this fits with sibling recipes
- Anti-pattern → fix

**Problem.** A handler iterates a collection that maps one-to-many onto related aggregates and applies an operation that can fail per element (e.g., `SubmitOrderCommand` reserves stock on the `Product` referenced by every `LineItem`). The naïve single-loop shape — call the mutating operation, check the `Result`, return on failure — is the common mistake, and the bug only surfaces on tests that arrange a *later* element to fail.

```csharp
// ❌ Single-loop mutate-as-you-validate. Line 1 reserves; line 3 fails InsufficientStock;
// line 1's product is left in the reserved state with no compensating rollback.
foreach (var li in order.LineItems)
{
    var r = byId[li.ProductId].Reserve(li.Quantity);
    if (r.Error is { } err)
        return Result.Fail<Order>(err);
}
return order.Submit();
```

For an ordinary failed result, `TransactionalCommandBehavior` skips its automatic commit; it does not restore in-memory fields or domain events. `Result.FailAfterCommit` or another response with `IPersistOnFailure.PersistOnFailure` set to `true` still requests a commit, so partial staged changes can be persisted despite the failed response; see [persist-on-failure dispatch](../trellis-api-mediator.md#domaineventdispatchbehavior). Those changes remain visible to subsequent code using the same aggregate instances, and to unit tests asserting post-handler state. Even an explicit database rollback is not in-memory compensation.

**The invariant the recipe teaches.**

> Preflight every fallible domain check across every participating aggregate BEFORE the first state-changing call. Then prove that each matching guard remains satisfied until its mutator executes, including through earlier Pass 2 mutations and changes to external inputs. A matching `Can*` plus unchanged fields between passes is not sufficient.

**Two design moves in the stock example.**

- **Pure `Can*` predicate alongside the mutator.** The aggregate exposes a side-effect-free `CanReserve(qty) → Result<Trellis.Unit>` that returns the same domain error the mutator would, and a `Reserve(qty)` that internally delegates to `CanReserve` before mutating. Same shape as [Recipe 9](recipe-09-state-machine.md)'s state-machine `CanFire`+`Fire`/`FireResult` pattern, lifted from "single transition on one aggregate" to "collection of operations across many aggregates."
- **Mutation-plan grouping for duplicate keys.** If the input collection can name the same related aggregate twice (e.g., two line items with the same `ProductId`), aggregate the duplicates into a single `(Product, totalQuantity)` plan entry before validating. Validating each line independently against unchanged stock is **not** equivalent to mutating them sequentially: two `CanReserve(3)` calls against `Stock=5` both pass, but the second `Reserve(3)` then fails. Grouping eliminates the aliasing.

> ⚠️ **Per-line invariants must be enforced before grouping.** The plan step (`g.Sum(li => (long)li.Quantity)`) preserves the aggregated quantity but destroys per-line identity. If `LineItem.Quantity` could be `-4`, then a `(5, -4)` pair would group to a valid `1`, slipping a negative quantity past `CanReserve`. In Trellis services per-line invariants are typically enforced at line-item construction (a `Quantity : RequiredInt<Quantity>` whose `ValidateAdditional` rejects values `<= 0`, or a private constructor + `TryCreate` factory). If your per-line invariants don't aggregate cleanly into a sum — e.g., "no single line may exceed 100 units" rather than "the total across lines must not exceed available stock" — add an explicit per-line validation pass *before* the grouping step and `Sequence` its results into Pass 1. Sum into `long`, not `int`, so valid positive quantities whose total exceeds `int.MaxValue` reach `CanReserve(long)` and return the existing `stock.insufficient` rule instead of throwing `OverflowException` inside `Sum`. `Stock` remains an `int`; `Reserve(long)` narrows the quantity only after `CanReserve` proves `0 < quantity <= Stock`.

Production services should still bound per-line quantities and the number of lines per order according to business and resource limits. Wider arithmetic does not replace those policies.

**Worked example.**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Trellis;

// Constructors and the LineItem type (ProductId, int Quantity > 0) are omitted for brevity;
// Aggregate<TId> requires an id constructor argument.
public sealed class Product : Aggregate<ProductId>
{
    public int Stock { get; private set; }

    // Pure predicate — runs in Pass 1, mutates nothing.
    public Result<Trellis.Unit> CanReserve(long quantity) =>
        Result.Ensure(
            quantity > 0 && quantity <= Stock,
            () => Error.InvalidInput.ForRule(
                code: "stock.insufficient",
                detail: $"Cannot reserve {quantity} from stock of {Stock}."));

    // Mutator — re-checks via CanReserve as defense in depth so it is safe to call
    // outside the two-pass orchestration. Success remains proved only while quantity and
    // Stock stay unchanged from validation until this call. This guard has no clock input.
    public Result<Trellis.Unit> Reserve(long quantity) =>
        CanReserve(quantity).Tap(() => Stock -= (int)quantity);
}

public sealed class Order : Aggregate<OrderId>
{
    public IReadOnlyList<LineItem> LineItems { get; }
    public bool IsSubmitted { get; private set; }

    public Result<Trellis.Unit> CanSubmit() =>
        Result.Ensure(
            LineItems.Count > 0,
            static () => Error.InvalidInput.ForRule(
                code: "order.empty",
                detail: "Order must have at least one line item to submit."));

    public Result<Order> Submit() =>
        CanSubmit().Tap(() => IsSubmitted = true).Map(_ => this);
}

public sealed class SubmitOrderHandler(
    IOrderRepository orders,
    IProductRepository products) : ICommandHandler<SubmitOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(SubmitOrderCommand command, CancellationToken cancellationToken)
    {
        var orderResult = await orders.FindByIdAsync(command.OrderId, cancellationToken);
        if (!orderResult.TryGetValue(out var order))
            return orderResult;

        // recipe-22-multi-aggregate-orchestration preflight (presence check). Every line-item ProductId must resolve
        // BEFORE the plan step, otherwise byId[g.Key] would throw KeyNotFoundException
        // and bypass the Result pipeline. SequenceAll reports every missing id.
        var productIds = order.LineItems.Select(li => li.ProductId).Distinct().ToArray();
        var loaded = await products.GetByIdsAsync(productIds, cancellationToken);
        var byId = loaded.ToDictionary(p => p.Id);
        var presence = productIds
            .Select(id => Result.Ensure(byId.ContainsKey(id),
                () => new Error.NotFound(ResourceRef.For<Product>(id))))
            .SequenceAll();
        if (presence.IsFailure)
            return Result.Fail<Order>(presence.Error);

        // Aggregate duplicate line items into one reservation per product so the
        // CanReserve checks operate on the same quantity the matching Reserve will deduct.
        var plan = order.LineItems
            .GroupBy(li => li.ProductId)
            .Select(g => (Product: byId[g.Key], Quantity: g.Sum(li => (long)li.Quantity)))
            .ToArray();

        // PASS 1 — validate every fallible domain check across every aggregate. No mutations.
        // SequenceAll accumulates every violation so the response enumerates them rather than
        // reporting only the first; use .Sequence() for fail-fast semantics (see recipe-20-fail-fast-vs-accumulating
        // for the decision criteria).
        var validation = plan
            .Select(p => p.Product.CanReserve(p.Quantity))
            .Append(order.CanSubmit())
            .SequenceAll();

        if (validation.Error is { } err)
            return Result.Fail<Order>(err);

        // PASS 2 — each distinct Product is reserved once using the validated total.
        // Earlier reservations affect neither other Products nor the time-independent
        // CanSubmit guard. Exclusive ownership keeps inputs stable until each call.
        // Discard() acknowledges proved domain success; it does not enforce the proof.
        foreach (var (product, quantity) in plan)
            product.Reserve(quantity).Discard();

        return order.Submit();
    }
}
```

The complete compile-checked snippet (including the time-policy examples below and the anti-pattern `WrongHandler` under `#if FALSE`) lives at `Examples/CookbookSnippets/Recipe25_TwoPassValidateThenMutate.cs` in the Trellis source repository (not shipped in the NuGet package). `Examples/CookbookSnippets.Tests/TwoPassMutationTests.cs` exercises the stock example, clock boundary, and intra-pass guard invalidation.

**The Pass-2-cannot-fail invariant — what makes it hold.**

The recipe's domain-success proof requires all of these conditions:

1. **Matching guards and inputs.** Every mutator has a pure preflight guard that covers all its domain rejection conditions, using the same arguments and eligibility policy. Group repeated keys and enforce per-line invariants before grouping.
2. **Stable guard dependencies until each call.** Aggregate state, aliased collections, clocks, permissions, and other external inputs cannot invalidate a guard after preflight but before its own mutator. Exclusive ownership matters; request scope, single-threaded execution, and absence of `await` do not freeze time or protect externally shared state. Reusing one `TimeProvider` object is not reusing one decision instant.
3. **No intra-pass invalidation.** Earlier Pass 2 mutations must preserve every later guard. Grouping repeated product keys solves the stock example's aliasing problem, not every ordering problem: freezing inventory before releasing it invalidates a guard requiring unfrozen inventory, even when both guards passed and nothing changed between passes.

The worked stock example meets these conditions under exclusive instance ownership: guards are time-independent, the materialized plan mutates each product once, and stock reservations do not change another product's stock or the order's line count. Its `Discard()` calls therefore acknowledge proved domain success; they do not validate or enforce the proof, and suppressing TRLS001 is not evidence of atomicity. This proof does not cover exceptions, external I/O failure, or concurrent database ownership changes.

When a later operation can still reject, propagate its result through `Bind` or an explicit failure branch. **Propagation alone does not undo earlier mutations.** Reorder a potentially rejecting operation before the first side effect only if its success cannot invalidate later guards, or use an explicitly designed preparation/isolation/compensation strategy. Do not discard an unproved result or claim database rollback repairs the object graph.

## Time-dependent eligibility: execution-time versus snapshot-time

Consider a return accepted until an inclusive deadline. Preflight reads one tick before that deadline. The same clock object later returns one tick after it. Releasing stock first and then returning the order leaves released stock and its event behind when the return rejects; no thread or intervening `await` is needed.

Choose the business policy explicitly. The following compile-checked examples use application-defined `ReturnWindowOrder` and `ReturnInventory` types from that snippet: the order's return method checks eligibility before changing its return timestamp or raising an event; inventory has a time-independent release guard. The order transition cannot invalidate that guard, and neither example freezes inventory.

The two policy blocks below mirror the compiled snippet.

**Execution-time eligibility:** recheck the live clock at the transition attempt, and perform that potentially rejecting transition before inventory changes, after all other preflight checks. A request admitted by preflight can be rejected with unchanged stock, return metadata, and events.

```csharp
public static Result<Trellis.Unit> AtExecutionTime(
    ReturnWindowOrder order, ReturnInventory inventory, int quantity, TimeProvider timeProvider)
{
    var validation = inventory.CanReleaseStock(quantity)
        .Bind(_ => order.CanReturn(timeProvider.GetUtcNow()));
    if (validation.IsFailure)
        return validation;

    var executionAt = timeProvider.GetUtcNow();
    return order.Return(executionAt)
        .Bind(_ => inventory.ReleaseStock(quantity, executionAt));
}
```

**Snapshot-time eligibility:** if the business policy accepts eligibility at one chosen decision instant, capture that instant once and pass it to both guard and transition. The same boundary request is consistently accepted even if the clock advances before mutation. This intentionally differs from execution-time rejection; it must not silently replace a requirement to check the live deadline.

```csharp
public static Result<Trellis.Unit> AtSnapshotTime(
    ReturnWindowOrder order, ReturnInventory inventory, int quantity, TimeProvider timeProvider)
{
    var decisionAt = timeProvider.GetUtcNow();
    var validation = inventory.CanReleaseStock(quantity)
        .Bind(_ => order.CanReturn(decisionAt));
    if (validation.IsFailure)
        return validation;

    return order.Return(decisionAt)
        .Bind(_ => inventory.ReleaseStock(quantity, decisionAt));
}
```

Both examples require exclusive ownership and stable stock-release inputs. Transition-first ordering is not a general atomicity guarantee if later mutations can still fail. Capturing time stabilizes only the time input, not permissions, inventory, or other mutable dependencies.

**Choosing fail-fast vs accumulating for the validation pass.**

The worked example uses `SequenceAll()` so the response enumerates every violation (typical for form-style and stock-style invariants where the user benefits from seeing all problems at once). Switch to `Sequence()` for fail-fast semantics when later checks are expensive and a single failure is sufficient. See [Recipe 20](recipe-20-fail-fast-vs-accumulating.md) for the full decision criteria. Both forms short-circuit on the same boundary: nothing in Pass 2 runs until validation returns `Ok`. `CanSubmit()` is evaluated when the sequence is built, so it runs even under `.Sequence()`; it is pure.

**How this fits with sibling recipes.**

- [Recipe 9](recipe-09-state-machine.md) — single-aggregate single-transition variant of the same `Can*`+`*` shape. Recipe 25 generalizes it across many aggregates and many operations.
- [Recipe 20](recipe-20-fail-fast-vs-accumulating.md) — the `Sequence`/`SequenceAll` choice for the validation pass.
- [Recipe 22](recipe-22-multi-aggregate-orchestration.md) — presence preflight for *missing* related aggregates (a different failure mode from per-element invariants). The two preflights compose: [Recipe 22](recipe-22-multi-aggregate-orchestration.md) runs first (every required aggregate exists), then Recipe 25 (every present aggregate's invariants admit the operation).

**Anti-pattern → fix.**

```csharp
// ❌ Single-loop mutate-as-you-validate. The bug is invisible to happy-path tests because
// every test sets up a fully-satisfiable order. A test that arranges line 3 to fail —
// e.g., line 3's product has Stock=0 — reveals that line 1's product is left reserved.
foreach (var li in order.LineItems)
{
    var r = byId[li.ProductId].Reserve(li.Quantity);
    if (r.Error is { } err)
        return Result.Fail<Order>(err);
}
return order.Submit();

// ✅ Two-pass validate-then-mutate — same shape as the worked example above. Pass 1
// checks every guard; exclusive ownership, grouped products, and time-independent
// noninterfering guards keep each matching mutation's domain success proved.
var plan = order.LineItems
    .GroupBy(li => li.ProductId)
    .Select(g => (Product: byId[g.Key], Quantity: g.Sum(li => (long)li.Quantity)))
    .ToArray();

var validation = plan
    .Select(p => p.Product.CanReserve(p.Quantity))
    .Append(order.CanSubmit())
    .SequenceAll();
if (validation.Error is { } err)
    return Result.Fail<Order>(err);

foreach (var (product, quantity) in plan)
    product.Reserve(quantity).Discard();

return order.Submit();
```

**Partial-Failure Atomicity test (the test the bug-shipping models never wrote).**

Every two-pass handler needs a test where a *later* element of the collection is unsatisfiable while *earlier* elements are. Happy-path tests cannot reveal the partial-mutation bug.

```csharp
[Fact]
public async Task Submit_with_one_unsatisfiable_line_does_not_reserve_any_stock()
{
    // Two line items; line 2 cannot be reserved (stock=0).
    var (order, productA, productB) = SeedOrderWithTwoLineItemsAsync(
        stockA: 10, qtyA: 3,   // line 1 satisfiable
        stockB: 0,  qtyB: 1);  // line 2 unsatisfiable
    var aStockBefore = productA.Stock;
    var bStockBefore = productB.Stock;

    var r = await _sender.Send(new SubmitOrderCommand(order.Id), cancellationToken);

    r.Should().BeFailureOfType<Error.InvalidInput>();
    productA.Stock.Should().Be(aStockBefore);   // NOT partially reserved
    productB.Stock.Should().Be(bStockBefore);
    order.IsSubmitted.Should().BeFalse();        // primary aggregate not transitioned
}
```

Also test duplicate products whose summed quantity exceeds available stock. Include boundary cases with `Stock = int.MaxValue`: a grouped total exactly `int.MaxValue` succeeds, while two `int.MaxValue` lines reject without changing any product's stock, order state or events. These cases cover the worked stock example, not every cross-aggregate operation. For time-dependent or interacting guards, add deterministic clock changes between preflight and transition, intra-pass invalidation, and each supported business policy's stock/metadata/event assertions. The executable regressions also pin the exact deadline and one tick on either side.
