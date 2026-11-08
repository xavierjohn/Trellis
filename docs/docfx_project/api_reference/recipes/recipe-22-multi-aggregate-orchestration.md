---
name: recipe-22-multi-aggregate-orchestration
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows orchestrating several related aggregates and failing loudly when one is missing. Use when a handler changes more than one aggregate."
---
# Recipe 22 — Multi-aggregate orchestration: fail-loud on missing related aggregates

**Requires:** `Trellis.Core`, plus `Trellis.Mediator`.

## Contents

- Problem
- The test-design principle: Partial-Failure Atomicity
- Generalisation
- Anti-pattern → fix

**Problem.** A command needs to mutate one primary aggregate (an Order) and trigger a side effect on each element of a related-aggregate set (release reserved stock on every Product referenced by the Order's line items). The naive shape is a `foreach` with a dictionary lookup of the related aggregate by id. If a referenced related aggregate is missing from that lookup — invariant violation; under normal flow it should never happen — what is the correct behaviour?

The wrong answer: `if (!dict.TryGetValue(id, out var related)) continue;` and move on. The handler appears to succeed, the primary aggregate transitions, the unit of work commits, and the side effect is silently skipped for the missing element. The orchestration has broken its own post-condition ("stock is released for every line item") with no signal to the caller and no rollback.

The right answer: fail-loud before mutation. Return `Error.NotFound` referencing the missing related aggregate so the ordinary failed command is not automatically committed. Do not rely on the unit of work to restore a primary aggregate already mutated in memory. The presence invariant — "every line item resolves to a Product, or no mutation begins" — is now enforced and observable.

Two operating principles drive the snippet below:

- **Preflight before mutating, not during.** An in-memory aggregate mutation persists through the rest of the handler's object graph even if the later database commit rolls back. So a release-then-discover pattern leaks partially-released stock into any subsequent read of the same aggregate within the request scope. Compute the missing-set *before* any side effect.
- **Report the full problem.** That's the value of preflighting — surface every missing id in one round-trip via `Error.Aggregate`, with each missing id keeping its own structured `ResourceRef`. (This recipe assumes product ids are non-sensitive; if your threat model differs, drop the per-id refs and emit a single generic `NotFound`.)

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Trellis;

public sealed class ReturnOrderHandler(
    IOrderRepository orders,
    IProductRepository products,
    TimeProvider timeProvider) : ICommandHandler<ReturnOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(ReturnOrderCommand command, CancellationToken cancellationToken)
    {
        // Repository find returns Result<T> — matches the repository shape in recipe-21-parallel-loads;
        // .TryGetValue extracts the success value or short-circuits on the existing Error.
        var orderResult = await orders.FindByIdAsync(command.OrderId, cancellationToken);
        if (!orderResult.TryGetValue(out var order))
            return orderResult;

        // Batch fetch returns what it found — the set-difference is the orchestrator's job.
        var productIds = order.LineItems.Select(li => li.ProductId).Distinct().ToArray();
        var loaded = await products.GetByIdsAsync(productIds, cancellationToken);
        var byId = loaded.ToDictionary(p => p.Id);

        // Preflight: prove EVERY related aggregate is reachable BEFORE any side effect.
        // NEVER `continue` past a missing related aggregate, and NEVER mutate before the
        // set is fully reachable. Report ALL missing ids via Error.Aggregate.
        static Error NotFoundFor(ProductId id) => new Error.NotFound(ResourceRef.For<Product>(id))
        {
            Detail = "Product referenced by line item is missing — cannot release stock.",
        };
        var presence = productIds
            .Select(id => Result.Ensure(byId.ContainsKey(id), () => NotFoundFor(id)))
            .SequenceAll();
        if (presence.IsFailure)
            return Result.Fail<Order>(presence.Error);

        // All related aggregates reachable. Preflight the per-aggregate domain
        // invariants (recipe-25-two-pass-validate-mutate: pure CanReleaseStock predicate paired with the
        // mutating ReleaseStock) before any mutation. Releasing stock on Product
        // A and then failing on Product B's release would leave A in a partially-
        // released state that TransactionalCommandBehavior cannot roll back from
        // the in-memory aggregate graph within the same request.
        var returnPreflight = order.CanReturn(command.Reason);
        if (returnPreflight.IsFailure)
            return Result.Fail<Order>(returnPreflight.Error);

        var releasePlan = order.LineItems
            .GroupBy(li => li.ProductId)
            .Select(group => (Product: byId[group.Key], Quantity: group.Sum(li => (long)li.Quantity)))
            .ToArray();
        var preflight = releasePlan
            .Select(item => item.Product.CanReleaseStock(item.Quantity))
            .SequenceAll();
        if (preflight.IsFailure)
            return Result.Fail<Order>(preflight.Error);

        // Each Product is released once using the validated total. These time-independent
        // guards remain valid: earlier releases affect neither other Products nor CanReturn.
        // The clock below supplies only an event timestamp, not an eligibility input.
        foreach (var item in releasePlan)
            item.Product.ReleaseStock(item.Quantity).Discard();

        return order.Return(command.Reason, timeProvider.GetUtcNow()).Map(_ => order);
    }
}
```

`CanReturn(reason)` is an application-defined pure predicate, also called by `Return`: it checks the reason, eligible order state, and positive line quantities before inventory changes. The sample's `CanReleaseStock(long)` / `ReleaseStock(long)` accept the grouped total; summing into `long` prevents an `int` overflow before validation. Both passes use the same materialized plan, once per product, with exclusive instance ownership and no mutations that invalidate later predicates. The sample's return eligibility is time-independent; `timeProvider.GetUtcNow()` supplies only the event timestamp. If a return window is added, follow [Recipe 25's time policy](recipe-25-two-pass-validate-mutate.md#time-dependent-eligibility-execution-time-versus-snapshot-time) rather than assuming the same provider freezes time. Checking two separate quantities of 3 against 5 reserved is not equivalent to checking their total of 6. Add regression cases for duplicate product ids, invalid reasons, and an already-returned order, asserting unchanged inventory and no new domain events on failure.

**The test-design principle: Partial-Failure Atomicity.**

> For every post-condition phrased as *"X is done for every Y in a set,"* write a test where one Y is unreachable at operation time. The post-condition must hold under one of two regimes:
>
> 1. **All-or-nothing:** the operation fails atomically; no X is performed.
> 2. **Best-effort with explicit signal:** every reachable Y has X performed; every unreachable Y produces a recorded error in the result.
>
> The implementation must pick one explicitly. If a "for every" handler silently `continue`s past an unreachable Y with neither failure nor signal, it has broken the post-condition and the unit of work has committed an inconsistent state.
>
> The test must hold the related-aggregate's pre-operation state and assert that no partial side-effect leaked through.

The generator test (the one a coverage-driven author would never produce from a happy-path-only mental model):

```csharp
[Fact]
public async Task Return_with_missing_product_fails_atomically_and_does_not_release_stock()
{
    // Seed two products A and B; build a Delivered order with line items referencing both.
    var (order, productA, productB) = await SeedDeliveredWithTwoProductsAsync();
    var aStockBefore = productA.StockQuantity;
    var bStockBefore = productB.StockQuantity;

    // Disrupt the universally-quantified set: drop productB from the repository.
    _products.Remove(productB);

    var r = await _sender.Send(new ReturnOrderCommand(order.Id, _reason), cancellationToken);

    // The orchestration MUST be atomic: either every line item's stock is released,
    // or the operation fails and NOTHING is released.
    r.Should().BeFailureOfType<Error.NotFound>();
    productA.StockQuantity.Should().Be(aStockBefore);  // A NOT partially released
    productB.StockQuantity.Should().Be(bStockBefore);  // B unmutated (the handler never reached it)
    order.Status.Should().Be(OrderStatus.Delivered);    // Order NOT transitioned (preflight prevented mutation)
}
```

**Generalisation.** Anywhere a handler does:

```csharp
foreach (var x in someSet)
{
    if (!cache.TryGetValue(x.RelatedId, out var related))
        continue;                              // ❌ silent skip — see anti-pattern below
    related.SideEffect(x.Args);
}
```

…there is a hidden missing-related-aggregate path. Apply the Partial-Failure Atomicity test pattern to every such loop. Failing tests will not be caught by a happy-path-only suite because every test sets up a known-good related-aggregate set.

**Anti-pattern → fix.**

```csharp
// ❌ Silent skip — passes every happy-path test, but if a Product disappears between
// the order being created and the return being processed (or if a future refactor
// adds a DeleteProduct endpoint), the return "succeeds" with a partially-released
// stock state. No exception, no Result failure, no log entry. Data corruption.
foreach (var li in order.LineItems)
{
    if (!byId.TryGetValue(li.ProductId, out var product))
        continue;                              // ← invariant violation hidden here
    product.ReleaseStock(li.Quantity);
}

// ✅ Fail-loud with full preflight — same two-pass shape as the worked example above.
// Compute the missing set BEFORE any side effect; only enter the release loop
// when every related aggregate is reachable.
var presence = order.LineItems.Select(li => li.ProductId).Distinct()
    .Select(id => Result.Ensure(byId.ContainsKey(id),
        () => new Error.NotFound(ResourceRef.For<Product>(id))))
    .SequenceAll();
if (presence.IsFailure)
    return Result.Fail<Order>(presence.Error);

// Preflight the per-aggregate domain invariants (recipe-25-two-pass-validate-mutate) BEFORE any mutation.
// Releasing stock on Product A then failing on Product B would leave A partially
// released — TransactionalCommandBehavior cannot roll back the in-memory aggregate
// graph within the request. Never mutate-and-bail in the loop.
var returnPreflight = order.CanReturn(command.Reason);
if (returnPreflight.IsFailure)
    return Result.Fail<Order>(returnPreflight.Error);
var releasePlan = order.LineItems
    .GroupBy(li => li.ProductId)
    .Select(group => (Product: byId[group.Key], Quantity: group.Sum(li => (long)li.Quantity)))
    .ToArray();
var preflight = releasePlan
    .Select(item => item.Product.CanReleaseStock(item.Quantity))
    .SequenceAll();
if (preflight.IsFailure)
    return Result.Fail<Order>(preflight.Error);

// Pass 2: grouped Products and time-independent, noninterfering guards preserve the
// preflight proof under exclusive instance ownership, not merely because Can* passed.
foreach (var item in releasePlan)
    item.Product.ReleaseStock(item.Quantity).Discard();
```
