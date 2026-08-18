// Cookbook Recipe 22 — Multi-aggregate orchestration: fail-loud on missing related aggregates.
//
// The invariant: "stock is released for every line item, or the command fails atomically."
// Preflight the missing set BEFORE any side effect, and report every missing id at once.
namespace CookbookSnippets.Recipe22;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Trellis;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed partial class ProductId : RequiredGuid<ProductId>;

public sealed class LineItem(ProductId productId, int quantity)
{
    public ProductId ProductId { get; } = productId;

    public int Quantity { get; } = quantity;
}

public sealed class Product : Aggregate<ProductId>
{
    private Product(ProductId id, int reserved) : base(id) => Reserved = reserved;

    public int Reserved { get; private set; }

    public static Product ForTesting(ProductId id, int reserved) => new(id, reserved);

    public Result<Trellis.Unit> CanReleaseStock(int quantity) =>
        Result.Ensure(
            quantity > 0 && quantity <= Reserved,
            Error.InvalidInput.ForRule(
                "stock.release-exceeds-reserved",
                $"Cannot release {quantity} against {Reserved} reserved."));

    public Result<Trellis.Unit> ReleaseStock(int quantity) =>
        CanReleaseStock(quantity).Tap(() => Reserved -= quantity);
}

public sealed class Order : Aggregate<OrderId>
{
    private Order(OrderId id, IReadOnlyList<LineItem> lineItems) : base(id) => LineItems = lineItems;

    public IReadOnlyList<LineItem> LineItems { get; }

    public bool IsReturned { get; private set; }

    public static Order ForTesting(OrderId id, IReadOnlyList<LineItem> lineItems) => new(id, lineItems);

    public Result<Trellis.Unit> Return(string reason, System.DateTimeOffset occurredAt) =>
        Result.Ensure(!string.IsNullOrWhiteSpace(reason), Error.InvalidInput.ForField("reason", "required"))
            .Tap(() =>
            {
                IsReturned = true;
                DomainEvents.Add(new OrderReturned(Id, occurredAt));
            });
}

public sealed record OrderReturned(OrderId OrderId, System.DateTimeOffset OccurredAt) : IDomainEvent;

public interface IOrderRepository
{
    Task<Result<Order>> FindByIdAsync(OrderId id, CancellationToken cancellationToken);
}

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<ProductId> ids, CancellationToken cancellationToken);
}

public sealed record ReturnOrderCommand(OrderId OrderId, string Reason) : ICommand<Result<Order>>;

public sealed class ReturnOrderHandler(
    IOrderRepository orders,
    IProductRepository products,
    System.TimeProvider timeProvider) : ICommandHandler<ReturnOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(ReturnOrderCommand command, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(command);

        // Repository find returns Result<T> — .TryGetValue extracts the success value
        // or short-circuits on the existing Error.
        var orderResult = await orders.FindByIdAsync(command.OrderId, cancellationToken).ConfigureAwait(false);
        if (!orderResult.TryGetValue(out var order))
            return orderResult;

        // Batch fetch returns what it found — the set-difference is the orchestrator's job.
        var productIds = order.LineItems.Select(li => li.ProductId).Distinct().ToArray();
        var loaded = await products.GetByIdsAsync(productIds, cancellationToken).ConfigureAwait(false);
        var byId = loaded.ToDictionary(p => p.Id);

        // Preflight: prove EVERY related aggregate is reachable BEFORE any side effect.
        // NEVER `continue` past a missing related aggregate, and NEVER mutate before the
        // set is fully reachable. Report ALL missing ids via Error.Aggregate.
        static Error NotFoundFor(ProductId id) => new Error.NotFound(ResourceRef.For<Product>(id))
        {
            Detail = "Product referenced by line item is missing — cannot release stock.",
        };
        var missing = productIds.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length == 1)
            return Result.Fail<Order>(NotFoundFor(missing[0]));
        if (missing.Length > 1)
            return Result.Fail<Order>(new Error.Aggregate(missing.Select(NotFoundFor).ToArray()));

        // All related aggregates reachable. Preflight the per-aggregate domain invariants
        // (Recipe 25) before any mutation. Releasing stock on Product A and then failing on
        // Product B's release would leave A in a partially-released state that
        // TransactionalCommandBehavior cannot roll back from the in-memory aggregate graph.
        var preflight = order.LineItems
            .Select(li => byId[li.ProductId].CanReleaseStock(li.Quantity))
            .SequenceAll();
        if (preflight.IsFailure)
            return Result.Fail<Order>(preflight.Error);

        // Pass 1 succeeded for every line item — every Pass 2 mutation below has a matching
        // Can* predicate that just returned Ok, so the mutation is provably non-failing.
        foreach (var li in order.LineItems)
            byId[li.ProductId].ReleaseStock(li.Quantity).Discard();

        return order.Return(command.Reason, timeProvider.GetUtcNow()).Map(_ => order);
    }
}

#if FALSE
// ❌ Silent skip — passes every happy-path test, but if a Product disappears between the
// order being created and the return being processed, the return "succeeds" with a
// partially-released stock state. No exception, no Result failure, no log entry.
foreach (var li in order.LineItems)
{
    if (!byId.TryGetValue(li.ProductId, out var product))
        continue;                              // ← invariant violation hidden here
    product.ReleaseStock(li.Quantity);
}
#endif