// Cookbook Recipe 25 — Two-pass validate-then-mutate over a collection of related aggregates.
//
// This snippet pins the worked example used in cookbook Recipe 25: every fallible domain check
// across every participating aggregate must succeed BEFORE the first state-changing call.
// After validation succeeds, every Pass 2 call has a matching Can* predicate from Pass 1, so
// the mutation is provably non-failing in a single-threaded handler.
//
// Key invariants the snippet demonstrates:
//   1. Aggregates expose a pure Can* predicate alongside the matching mutator (Product.CanReserve
//      + Product.Reserve, Order.CanSubmit + Order.Submit). Same shape as Recipe 9's CanFire/Fire.
//   2. Duplicate keys in the input collection are aggregated into a stable "mutation plan" so
//      the Can* checks operate on the same quantity the mutator will deduct. Without this step,
//      two line items for the same product each pass CanReserve against unchanged stock; the
//      sequential mutation then fails on the second item — the exact bug the recipe prevents.
//   3. Recipe 22's presence preflight runs first so dictionary lookups in Pass 2 cannot throw.
//   4. Pass 2 calls .Discard() on each mutator's Result<Trellis.Unit>, suppressing TRLS001 with the
//      documented justification ("Can* in Pass 1 passed; mutation cannot fail").
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
            Error.InvalidInput.ForRule(
                "stock.insufficient",
                $"Cannot reserve {quantity} from stock of {Stock}."));

    // Mutator — re-checks via CanReserve so the method is safe to call outside the
    // two-pass orchestration. When called after a matching Pass 1 CanReserve succeeded
    // (single-threaded handler, no intervening mutations), Reserve is provably non-failing.
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
            Error.InvalidInput.ForRule(
                "order.empty",
                "Order must have at least one line item to submit."));

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

        var missing = productIds.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length == 1)
            return Result.Fail<Order>(new Error.NotFound(ResourceRef.For<Product>(missing[0])));
        if (missing.Length > 1)
            return Result.Fail<Order>(new Error.Aggregate(
                missing.Select(id => (Error)new Error.NotFound(ResourceRef.For<Product>(id))).ToArray()));

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

        // PASS 2 — apply every mutation. Each call is provably non-failing because its
        // matched Can* predicate already passed in Pass 1 AND nothing has mutated the
        // in-memory aggregate state between passes (single-threaded handler). Discard() is
        // the idiomatic acknowledged-discard that suppresses TRLS001.
        foreach (var (product, quantity) in plan)
            product.Reserve(quantity).Discard();

        return order.Submit();
    }
}

#if FALSE
// ❌ Single-loop mutate-as-you-validate — the lab anti-pattern. Tests against happy-path
// orders pass; an order that fails on a later line item leaves earlier products in the
// reserved state while the command returns failure. TransactionalCommandBehavior rolls
// back the DB commit, but the in-memory aggregate state stays mutated for the rest of
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