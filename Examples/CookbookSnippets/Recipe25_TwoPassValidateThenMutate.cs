// Cookbook Recipe 25 — Two-pass validate-then-mutate over a collection of related aggregates.
//
// This snippet pins the worked example used in cookbook Recipe 25: every fallible domain check
// across every participating aggregate must succeed BEFORE the first state-changing call.
// A matching Can* is not enough: its inputs must stay stable until its mutator runs, including
// through earlier Pass 2 mutations. A clock can change even without threads or intervening awaits.
//
// Key invariants the snippet demonstrates:
//   1. Aggregates expose a pure Can* predicate alongside the matching mutator (Product.CanReserve
//      + Product.Reserve, Order.CanSubmit + Order.Submit). Same shape as Recipe 9's CanFire/Fire.
//   2. Duplicate keys in the input collection are aggregated into a stable "mutation plan" so
//      the Can* checks operate on the same quantity the mutator will deduct. Without this step,
//      two line items for the same product each pass CanReserve against unchanged stock; the
//      sequential mutation then fails on the second item — the exact bug the recipe prevents.
//   3. Recipe 22's presence preflight runs first so dictionary lookups in Pass 2 cannot throw.
//   4. These guards are time-independent. Each Product is mutated once; changing its stock cannot
//      invalidate another Product's guard or Order.CanSubmit. With exclusive instance ownership,
//      this proves the domain results discarded in Pass 2 cannot fail.
//   5. The internal return examples contrast live transition-time eligibility with a captured
//      decision instant. Their tests also show a freeze invalidating a later stock-release guard.
namespace CookbookSnippets.Recipe25;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Trellis;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed partial class ProductId : RequiredGuid<ProductId>;

public sealed class LineItem
{
    public LineItem(ProductId productId, int quantity)
    {
        // Per-line invariant: Quantity must be positive. Grouping in Pass 1 sums quantities
        // by ProductId; without this guard a (5, -4) pair would group to a valid 1, slipping
        // a negative line item past validation. In production code Quantity is typically a
        // value object (e.g. RequiredInt<Quantity> validating > 0); the constructor check
        // here is the minimum equivalent.
        if (quantity <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");

        ProductId = productId;
        Quantity = quantity;
    }

    public ProductId ProductId { get; }

    public int Quantity { get; }
}

public sealed class Product : Aggregate<ProductId>
{
    private Product(ProductId id, int stock) : base(id) => Stock = stock;

    public int Stock { get; private set; }

    public static Product ForTesting(ProductId id, int stock) => new(id, stock);

    // Pure predicate — runs in Pass 1, mutates nothing.
    public Result<Trellis.Unit> CanReserve(int quantity) =>
        Result.Ensure(
            quantity > 0 && quantity <= Stock,
            () => Error.InvalidInput.ForRule(
                code: "stock.insufficient",
                detail: $"Cannot reserve {quantity} from stock of {Stock}."));

    // Mutator — re-checks via CanReserve so the method is safe to call outside the
    // two-pass orchestration. Success remains proved only while this quantity and Stock stay
    // unchanged from validation until this call; the guard has no external/time-dependent input.
    public Result<Trellis.Unit> Reserve(int quantity) =>
        CanReserve(quantity).Tap(() => Stock -= quantity);
}

public sealed class Order : Aggregate<OrderId>
{
    private Order(OrderId id, IReadOnlyList<LineItem> lineItems) : base(id) => LineItems = lineItems;

    public IReadOnlyList<LineItem> LineItems { get; }

    public bool IsSubmitted { get; private set; }

    public static Order ForTesting(OrderId id, IReadOnlyList<LineItem> lineItems) => new(id, lineItems);

    // Pure predicate — runs in Pass 1, mutates nothing.
    public Result<Trellis.Unit> CanSubmit() =>
        Result.Ensure(
            LineItems.Count > 0,
            static () => Error.InvalidInput.ForRule(
                code: "order.empty",
                detail: "Order must have at least one line item to submit."));

    // Mutator — re-checks via CanSubmit. Same defense-in-depth shape as Product.Reserve.
    public Result<Order> Submit() =>
        CanSubmit().Tap(() => IsSubmitted = true).Map(_ => this);
}

public interface IOrderRepository
{
    Task<Result<Order>> FindByIdAsync(OrderId id, CancellationToken cancellationToken);
}

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<ProductId> ids, CancellationToken cancellationToken);
}

public sealed record SubmitOrderCommand(OrderId OrderId) : ICommand<Result<Order>>;

public sealed class SubmitOrderHandler(
    IOrderRepository orders,
    IProductRepository products) : ICommandHandler<SubmitOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(SubmitOrderCommand command, CancellationToken cancellationToken)
    {
        // Load the primary aggregate.
        var orderResult = await orders.FindByIdAsync(command.OrderId, cancellationToken);
        if (!orderResult.TryGetValue(out var order))
            return orderResult;

        // Recipe 22 preflight (presence check) — every line-item ProductId must resolve
        // before we touch stock. Without this, the byId[g.Key] lookup in the plan step
        // would throw KeyNotFoundException, bypassing the Result pipeline.
        var productIds = order.LineItems.Select(li => li.ProductId).Distinct().ToArray();
        var loaded = await products.GetByIdsAsync(productIds, cancellationToken);
        var byId = loaded.ToDictionary(p => p.Id);

        var presence = productIds
            .Select(id => Result.Ensure(byId.ContainsKey(id),
                () => new Error.NotFound(ResourceRef.For<Product>(id))))
            .SequenceAll();
        if (presence.IsFailure)
            return Result.Fail<Order>(presence.Error);

        // Build a stable mutation plan: aggregate duplicate line items by ProductId so the
        // CanReserve checks operate on the SAME quantity the matching Reserve call will deduct.
        // Without this step, two line items for the same product each pass CanReserve against
        // unchanged stock; the actual sequential mutation then fails on the second item.
        var plan = order.LineItems
            .GroupBy(li => li.ProductId)
            .Select(g => (Product: byId[g.Key], Quantity: g.Sum(li => li.Quantity)))
            .ToArray();

        // PASS 1 — validate every fallible domain check across every participating aggregate.
        // No mutations. SequenceAll accumulates every violation so the response enumerates them
        // rather than reporting only the first; switch to .Sequence() for fail-fast semantics
        // (see Recipe 20 for the decision criteria).
        var validation = plan
            .Select(p => p.Product.CanReserve(p.Quantity))
            .Append(order.CanSubmit())
            .SequenceAll();

        if (validation.Error is { } err)
            return Result.Fail<Order>(err);

        // PASS 2 — each distinct Product is reserved once using the validated total. These
        // mutations cannot invalidate another Product's guard or the time-independent
        // CanSubmit guard. Exclusive ownership keeps their inputs stable until each call.
        // Discard() acknowledges this proved domain success; it does not enforce the proof.
        foreach (var (product, quantity) in plan)
            product.Reserve(quantity).Discard();

        return order.Submit();
    }
}

internal sealed class ReturnWindowOrder(OrderId id, System.DateTimeOffset deadline) : Aggregate<OrderId>(id)
{
    public System.DateTimeOffset? ReturnedAt { get; private set; }

    public Result<Trellis.Unit> CanReturn(System.DateTimeOffset decisionAt) =>
        Result.Ensure(ReturnedAt is null,
                static () => Error.InvalidInput.ForRule(code: "order.already-returned"))
            .Bind(_ => Result.Ensure(decisionAt <= deadline,
                static () => Error.InvalidInput.ForRule(code: "order.return-window-expired")));

    public Result<Trellis.Unit> Return(System.DateTimeOffset decisionAt) =>
        CanReturn(decisionAt).Tap(() =>
        {
            ReturnedAt = decisionAt;
            DomainEvents.Add(new ReturnCompleted(Id, decisionAt));
        });
}

internal sealed record ReturnCompleted(OrderId OrderId, System.DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed class ReturnInventory(ProductId id, int reserved) : Aggregate<ProductId>(id)
{
    public int Reserved { get; private set; } = reserved;

    public bool IsFrozen { get; private set; }

    public Result<Trellis.Unit> CanReleaseStock(int quantity) =>
        Result.Ensure(!IsFrozen && quantity > 0 && quantity <= Reserved,
            static () => Error.InvalidInput.ForRule(code: "stock.release-not-allowed"));

    public Result<Trellis.Unit> ReleaseStock(int quantity, System.DateTimeOffset occurredAt) =>
        CanReleaseStock(quantity).Tap(() =>
        {
            Reserved -= quantity;
            DomainEvents.Add(new StockReleased(Id, quantity, occurredAt));
        });

    public Result<Trellis.Unit> CanFreeze() =>
        Result.Ensure(!IsFrozen,
            static () => Error.InvalidInput.ForRule(code: "stock.already-frozen"));

    public Result<Trellis.Unit> Freeze(System.DateTimeOffset occurredAt) =>
        CanFreeze().Tap(() =>
        {
            IsFrozen = true;
            DomainEvents.Add(new InventoryFrozen(Id, occurredAt));
        });
}

internal sealed record StockReleased(
    ProductId ProductId, int Quantity, System.DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record InventoryFrozen(ProductId ProductId, System.DateTimeOffset OccurredAt) : IDomainEvent;

internal static class ReturnPolicies
{
    public static Result<Trellis.Unit> AtExecutionTime(
        ReturnWindowOrder order, ReturnInventory inventory, int quantity, System.TimeProvider timeProvider)
    {
        var validation = inventory.CanReleaseStock(quantity)
            .Bind(_ => order.CanReturn(timeProvider.GetUtcNow()));
        if (validation.IsFailure)
            return validation;

        var executionAt = timeProvider.GetUtcNow();
        return order.Return(executionAt)
            .Bind(_ => inventory.ReleaseStock(quantity, executionAt));
    }

    public static Result<Trellis.Unit> AtSnapshotTime(
        ReturnWindowOrder order, ReturnInventory inventory, int quantity, System.TimeProvider timeProvider)
    {
        var decisionAt = timeProvider.GetUtcNow();
        var validation = inventory.CanReleaseStock(quantity)
            .Bind(_ => order.CanReturn(decisionAt));
        if (validation.IsFailure)
            return validation;

        return order.Return(decisionAt)
            .Bind(_ => inventory.ReleaseStock(quantity, decisionAt));
    }
}

#if FALSE
// ❌ Single-loop mutate-as-you-validate — the lab anti-pattern. Tests against happy-path
// orders pass; an order that fails on a later line item leaves earlier products in the
// reserved state while the command returns failure. Skipping the DB commit does not
// restore in-memory aggregate state, which stays mutated for the rest of
// the request — visible to any code that reads the same aggregate within the request scope.
internal sealed class WrongHandler(
    IOrderRepository orders,
    IProductRepository products) : ICommandHandler<SubmitOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(SubmitOrderCommand command, CancellationToken cancellationToken)
    {
        var orderResult = await orders.FindByIdAsync(command.OrderId, cancellationToken);
        if (!orderResult.TryGetValue(out var order))
            return orderResult;

        var productIds = order.LineItems.Select(li => li.ProductId).Distinct().ToArray();
        var loaded = await products.GetByIdsAsync(productIds, cancellationToken);
        var byId = loaded.ToDictionary(p => p.Id);

        foreach (var li in order.LineItems)
        {
            // Mutates as it validates — line 1's Reserve succeeds, line 3's fails, line 1
            // is left reserved with no compensating rollback.
            var r = byId[li.ProductId].Reserve(li.Quantity);
            if (r.Error is { } err)
                return Result.Fail<Order>(err);
        }

        return order.Submit();
    }
}
#endif